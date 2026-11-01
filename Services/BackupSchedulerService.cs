using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.IO;

namespace RetailBillingApp.Services;

/// <summary>
/// SEC-01: schedules automatic backups on top of <see cref="IBackupService"/>.
/// A coarse one-minute <see cref="System.Threading.Timer"/> (not a DispatcherTimer, so it
/// works before any window exists) fires a backup once the wall clock passes NextRunAt, then
/// advances to the next slot — collapsing any missed slots into a single catch-up run.
/// The scheduler never copies the database itself; it delegates to IBackupService.
/// </summary>
public class BackupSchedulerService : IBackupSchedulerService
{
    // The timer runs on pool threads, so this service must not hold a scoped AppDbContext
    // for its lifetime (a captive dependency shared with the UI thread's context). Every
    // operation opens its own scope instead and gets a private DbContext.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAuthService _authService;

    // Serializes the two DB-writing entry points (the tick and a manual Run Now) so the
    // scheduler's own reads/SaveChanges never interleave with another run.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private System.Threading.Timer? _timer;
    // Non-awaitable guard so a slow run can't let two ticks overlap.
    private int _tickRunning;

    // Cached mirror of the persisted row so the status card can read it without a query.
    private DateTime? _lastRunAt;
    private DateTime? _nextRunAt;
    private string? _lastResult;

    public BackupSchedulerService(IServiceScopeFactory scopeFactory, IAuthService authService)
    {
        _scopeFactory = scopeFactory;
        _authService = authService;
    }

    public bool IsRunning => _timer != null;
    public DateTime? LastRunAt => _lastRunAt;
    public DateTime? NextRunAt => _nextRunAt;
    public string? LastResult => _lastResult;

    public async Task<BackupScheduleSetting> GetSettingsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = await GetOrCreateSettingsAsync(Db(scope));
        UpdateCache(settings);
        return settings;
    }

    public async Task<(bool Success, string? Error)> SaveSettingsAsync(BackupScheduleSetting setting)
    {
        if (setting == null)
            return (false, "No schedule to save.");

        if (!BackupScheduleSetting.FrequencyOptions.Contains(setting.Frequency))
            return (false, $"Unknown frequency '{setting.Frequency}'.");

        await _gate.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = Db(scope);
            var current = await GetOrCreateSettingsAsync(db);
            var before = Snapshot(current);

            current.IsEnabled = setting.IsEnabled;
            current.Frequency = setting.Frequency;
            current.IntervalDays = Math.Max(1, setting.IntervalDays);
            current.DayOfWeek = string.IsNullOrWhiteSpace(setting.DayOfWeek) ? "Sunday" : setting.DayOfWeek.Trim();
            current.RunAtHour = Math.Clamp(setting.RunAtHour, 0, 23);
            current.RunAtMinute = Math.Clamp(setting.RunAtMinute, 0, 59);
            current.DestinationFolder = string.IsNullOrWhiteSpace(setting.DestinationFolder)
                ? null : setting.DestinationFolder.Trim();
            current.KeepGenerations = Math.Max(1, setting.KeepGenerations);
            current.IncludeManualBackupsInPrune = setting.IncludeManualBackupsInPrune;

            // Re-anchor the slot to "now" so a freshly (re)saved schedule waits for its next
            // occurrence instead of firing immediately on the timer's next tick.
            current.NextRunAt = ComputeNextRun(current, DateTime.Now);
            current.UpdatedAt = DateTime.UtcNow;
            current.UpdatedBy = _authService.CurrentUser?.Username;

            try
            {
                await db.SaveChangesAsync();
            }
            catch
            {
                return (false, "Failed to save the backup schedule.");
            }

            UpdateCache(current);
            await scope.ServiceProvider.GetRequiredService<IAuditService>().RecordAsync("Update", "BackupSchedule", current.Id,
                "Automatic backup schedule", before, Snapshot(current));

            return (true, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Start()
    {
        if (_timer != null) return;
        _timer = new System.Threading.Timer(OnTick, null,
            dueTime: TimeSpan.FromSeconds(20),
            period: TimeSpan.FromMinutes(1));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTick(object? state)
    {
        if (Interlocked.CompareExchange(ref _tickRunning, 1, 0) != 0) return;

        // Timer callbacks run on a pool thread; RunIfDueAsync swallows its own exceptions, but
        // guard here too so nothing can ever surface on the timer thread.
        _ = Task.Run(async () =>
        {
            try { await RunIfDueAsync(); }
            catch { /* defensive */ }

            // PAY-08: the same coarse tick arms the automatic reminder run. The reminder
            // service swallows its own failures, but guard here too so nothing can ever
            // surface on the timer thread or interfere with the backup run above.
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reminders = scope.ServiceProvider.GetRequiredService<IReminderService>();
                await reminders.RunAutoRemindersAsync(DateTime.Now);
            }
            catch { /* defensive: the auto-run must never break the backup tick */ }

            finally { Interlocked.Exchange(ref _tickRunning, 0); }
        });
    }

    public async Task<bool> RunIfDueAsync()
    {
        try
        {
            var now = DateTime.Now;
            await _gate.WaitAsync();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = Db(scope);
                var s = await GetOrCreateSettingsAsync(db);
                UpdateCache(s);
                if (!s.IsEnabled) return false;

                if (s.NextRunAt == null)
                {
                    s.NextRunAt = ComputeNextRun(s, now);
                    await db.SaveChangesAsync();
                    UpdateCache(s);
                    return false;
                }

                if (now < s.NextRunAt) return false;

                var outcome = await PerformBackupAsync(scope.ServiceProvider, s);

                s.LastRunAt = DateTime.Now;
                s.LastResult = outcome.Message;
                s.UpdatedAt = DateTime.UtcNow;
                s.UpdatedBy = _authService.CurrentUser?.Username;
                // Compute from the just-completed run so missed slots collapse into one.
                s.NextRunAt = ComputeNextRun(s, s.LastRunAt.Value);
                await db.SaveChangesAsync();
                UpdateCache(s);

                await scope.ServiceProvider.GetRequiredService<IAuditService>().RecordAsync("Run", "BackupSchedule", s.Id,
                    "Automatic backup run", null,
                    new { outcome.Success, outcome.Message, Path = outcome.Path });

                return outcome.Success;
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            // Never let an exception escape the tick. Record it for the status card; if the
            // context is unusable the in-memory mirror still shows the reason until the next load.
            _lastResult = $"Scheduled backup failed: {ex.Message}";
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = Db(scope);
                var s = await GetOrCreateSettingsAsync(db);
                s.LastResult = _lastResult;
                await db.SaveChangesAsync();
            }
            catch { /* ignore secondary persistence failure */ }
            return false;
        }
    }

    public async Task<(bool Success, string Message, string? Path)> RunNowAsync()
    {
        await _gate.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = Db(scope);
            var s = await GetOrCreateSettingsAsync(db);
            var outcome = await PerformBackupAsync(scope.ServiceProvider, s);

            s.LastRunAt = DateTime.Now;
            s.LastResult = outcome.Message;
            s.UpdatedAt = DateTime.UtcNow;
            s.UpdatedBy = _authService.CurrentUser?.Username;
            s.NextRunAt = ComputeNextRun(s, s.LastRunAt.Value) ?? s.NextRunAt;
            await db.SaveChangesAsync();
            UpdateCache(s);

            await scope.ServiceProvider.GetRequiredService<IAuditService>().RecordAsync("Run", "BackupSchedule", s.Id,
                "Manual backup run", null,
                new { outcome.Success, outcome.Message, Path = outcome.Path });

            return (outcome.Success, outcome.Message, outcome.Path);
        }
        catch (Exception ex)
        {
            return (false, $"Backup run failed: {ex.Message}", null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public DateTime? ComputeNextRun(BackupScheduleSetting setting, DateTime from)
    {
        if (setting == null) return null;

        var slot = new TimeSpan(
            Math.Clamp(setting.RunAtHour, 0, 23),
            Math.Clamp(setting.RunAtMinute, 0, 59),
            0);

        switch (setting.Frequency)
        {
            case BackupScheduleSetting.FrequencyWeekly:
            {
                var target = ParseDayOfWeek(setting.DayOfWeek);
                var candidate = from.Date + slot;
                var delta = ((int)target - (int)candidate.DayOfWeek + 7) % 7;
                candidate = candidate.AddDays(delta);
                if (candidate <= from) candidate = candidate.AddDays(7);
                return candidate;
            }

            case BackupScheduleSetting.FrequencyEveryN:
            {
                var n = Math.Max(1, setting.IntervalDays);
                var candidate = from.Date + slot;
                if (candidate <= from) candidate = candidate.AddDays(1);

                // Honour the interval relative to the last run: don't schedule closer than
                // N days. With no prior run the first slot is simply the next daily one.
                if (n > 1 && setting.LastRunAt is { } last)
                {
                    var earliest = last.Date + slot + TimeSpan.FromDays(n);
                    var guard = 0;
                    while (candidate < earliest && guard++ < 800)
                        candidate = candidate.AddDays(1);
                }
                return candidate;
            }

            default: // Daily
            {
                var candidate = from.Date + slot;
                if (candidate <= from) candidate = candidate.AddDays(1);
                return candidate;
            }
        }
    }

    // ---- backup + prune internals ----

    private record BackupOutcome(bool Success, string Message, string? Path);

    private static AppDbContext Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<AppDbContext>();

    private static async Task<BackupScheduleSetting> GetOrCreateSettingsAsync(AppDbContext db)
    {
        var settings = await db.BackupScheduleSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            settings = new BackupScheduleSetting { Id = 1 };
            db.BackupScheduleSettings.Add(settings);
            await db.SaveChangesAsync();
        }
        return settings;
    }

    private async Task<BackupOutcome> PerformBackupAsync(IServiceProvider scoped, BackupScheduleSetting setting)
    {
        var backupService = scoped.GetRequiredService<IBackupService>();
        var (folder, targetPath) = ResolveTargetPath(backupService, setting);
        var result = await backupService.BackupDatabaseAsync(targetPath);

        if (!result.Success)
            return new BackupOutcome(false, result.Message, result.Path);

        var pruneReport = PruneOldCopies(setting, folder);
        var path = result.Path ?? targetPath;
        return new BackupOutcome(true, $"{result.Message}{pruneReport}", path);
    }

    private static (string Folder, string FullPath) ResolveTargetPath(IBackupService backupService, BackupScheduleSetting setting)
    {
        // BackupService treats destinationPath as a full file path (it File.Copy's onto it),
        // so we build an AutoBackup_-prefixed file name inside the chosen folder. When no folder
        // is set we reuse the folder GetDefaultBackupPath writes to (it returns a file path).
        var folder = string.IsNullOrWhiteSpace(setting.DestinationFolder)
            ? Path.GetDirectoryName(backupService.GetDefaultBackupPath())
              ?? backupService.GetDefaultBackupPath()
            : setting.DestinationFolder;

        Directory.CreateDirectory(folder);

        // Millisecond precision avoids same-second collisions between a manual run and a tick,
        // which would otherwise overwrite:true clobber an earlier copy.
        var fileName = $"{BackupScheduleSetting.AutoFilePrefix}{DateTime.Now:yyyyMMdd_HHmmss_fff}.db";
        return (folder, Path.Combine(folder, fileName));
    }

    private static string PruneOldCopies(BackupScheduleSetting setting, string folder)
    {
        var keep = Math.Max(1, setting.KeepGenerations);
        List<FileInfo> candidates;

        try
        {
            if (!Directory.Exists(folder)) return string.Empty;

            var all = new DirectoryInfo(folder).GetFiles("*.db");
            // Without the flag, only ever consider files this scheduler created (AutoBackup_*);
            // manual backup_*.db files are never touched.
            candidates = (setting.IncludeManualBackupsInPrune
                    ? all
                    : all.Where(f => f.Name.StartsWith(BackupScheduleSetting.AutoFilePrefix, StringComparison.Ordinal)))
                .OrderByDescending(f => f.CreationTimeUtc)
                .ToList();
        }
        catch (Exception ex)
        {
            return $" (pruning skipped: {ex.Message})";
        }

        var stale = candidates.Skip(keep).ToList();
        if (stale.Count == 0) return string.Empty;

        var deleted = 0;
        var failedNames = new List<string>();
        foreach (var file in stale)
        {
            try
            {
                file.Delete();
                deleted++;
            }
            catch
            {
                failedNames.Add(file.Name);
            }
        }

        // Report removals explicitly rather than silently deleting.
        var report = $" Pruned {deleted} old automatic backup(s).";
        if (failedNames.Count > 0)
            report += $" {failedNames.Count} could not be removed ({string.Join(", ", failedNames.Take(3))}).";

        return report;
    }

    private static DayOfWeek ParseDayOfWeek(string? name) =>
        System.Enum.TryParse(name, ignoreCase: true, out DayOfWeek day) ? day : DayOfWeek.Sunday;

    private static object Snapshot(BackupScheduleSetting s) => new
    {
        s.IsEnabled,
        s.Frequency,
        s.IntervalDays,
        s.DayOfWeek,
        s.RunAtHour,
        s.RunAtMinute,
        s.DestinationFolder,
        s.KeepGenerations,
        s.IncludeManualBackupsInPrune,
        s.NextRunAt
    };

    private void UpdateCache(BackupScheduleSetting s)
    {
        _lastRunAt = s.LastRunAt;
        _nextRunAt = s.NextRunAt;
        _lastResult = s.LastResult;
    }
}
