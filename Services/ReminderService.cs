using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RetailBillingApp.Services;

/// <summary>
/// PAY-08: reminder rule engine. Classifies every open receivable against the
/// ReminderSetting window and hands the queued rows to the Messaging Center, which
/// owns the actual delivery and the message log the dedupe reads back.
/// </summary>
public class ReminderService : IReminderService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuthService _authService;
    private readonly IAuditService _auditService;
    private readonly IMessagingService _messagingService;

    /// <summary>Replaced from the invoice template's HeaderText on every candidate build.</summary>
    private string _companyName = "our store";

    private bool _companyNameResolved;

    public List<string> Channels { get; } = new() { "WhatsApp", "SMS", "Email" };

    public List<string> Stages { get; } = new() { ReminderStages.Upcoming, ReminderStages.Overdue, ReminderStages.SecondNotice };

    public string? LastValidationError { get; private set; }

    public List<string> Errors { get; } = new();

    public ReminderService(AppDbContext dbContext, IAuthService authService,
        IAuditService auditService, IMessagingService messagingService)
    {
        _dbContext = dbContext;
        _authService = authService;
        _auditService = auditService;
        _messagingService = messagingService;
    }

    public async Task<ReminderSetting> GetSettingsAsync()
    {
        var settings = await _dbContext.ReminderSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            settings = new ReminderSetting { Id = 1 };
            _dbContext.ReminderSettings.Add(settings);
            await _dbContext.SaveChangesAsync();
        }
        return settings;
    }

    public async Task<(bool Success, string? Error)> SaveSettingsAsync(ReminderSetting setting)
    {
        var normalized = new ReminderSetting
        {
            Id = 1,
            IsEnabled = setting.IsEnabled,
            FirstNoticeDaysBeforeDue = setting.FirstNoticeDaysBeforeDue,
            SecondNoticeDaysAfterDue = setting.SecondNoticeDaysAfterDue,
            Channel = (setting.Channel ?? string.Empty).Trim(),
            MessageTemplate = (setting.MessageTemplate ?? string.Empty).Trim(),
            MaxPerCustomerPerDay = setting.MaxPerCustomerPerDay,
            AutoRunEnabled = setting.AutoRunEnabled,
            AutoRunHour = Math.Clamp(setting.AutoRunHour, 0, 23),
            AutoRunMinute = Math.Clamp(setting.AutoRunMinute, 0, 59),
        };

        LastValidationError = Validate(normalized);
        if (LastValidationError != null)
            return (false, LastValidationError);

        var stored = await GetSettingsAsync();
        var before = new
        {
            stored.IsEnabled,
            stored.FirstNoticeDaysBeforeDue,
            stored.SecondNoticeDaysAfterDue,
            stored.Channel,
            stored.MessageTemplate,
            stored.MaxPerCustomerPerDay,
            stored.AutoRunEnabled,
            stored.AutoRunHour,
            stored.AutoRunMinute,
        };

        stored.IsEnabled = normalized.IsEnabled;
        stored.FirstNoticeDaysBeforeDue = normalized.FirstNoticeDaysBeforeDue;
        stored.SecondNoticeDaysAfterDue = normalized.SecondNoticeDaysAfterDue;
        stored.Channel = normalized.Channel;
        stored.MessageTemplate = normalized.MessageTemplate;
        stored.MaxPerCustomerPerDay = normalized.MaxPerCustomerPerDay;
        stored.AutoRunEnabled = normalized.AutoRunEnabled;
        stored.AutoRunHour = normalized.AutoRunHour;
        stored.AutoRunMinute = normalized.AutoRunMinute;
        stored.UpdatedAt = DateTime.UtcNow;
        stored.UpdatedBy = _authService.CurrentUser?.Username;

        try
        {
            _dbContext.ReminderSettings.Update(stored);
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            return (false, "Failed to save the reminder settings.");
        }

        await _auditService.RecordAsync("Update", "ReminderSetting", stored.Id, "Payment reminder rules",
            before,
            new
            {
                stored.IsEnabled,
                stored.FirstNoticeDaysBeforeDue,
                stored.SecondNoticeDaysAfterDue,
                stored.Channel,
                stored.MessageTemplate,
                stored.MaxPerCustomerPerDay,
                stored.AutoRunEnabled,
                stored.AutoRunHour,
                stored.AutoRunMinute,
            });

        return (true, null);
    }

    public async Task<List<ReminderCandidate>> GetCandidatesAsync(DateTime asOf)
    {
        Errors.Clear();
        var settings = await GetSettingsAsync();
        _companyName = await ResolveCompanyNameAsync();

        // Same receivable definition as the reports: unpaid, money still outstanding, not an estimate.
        var invoices = await _dbContext.Invoices.AsNoTracking()
            .Include(i => i.Customer)
            .Where(i => i.Status != "Paid" && i.TotalAmount - i.PaidAmount - i.WriteOffAmount > 0 && !i.IsEstimate)
            .OrderBy(i => i.InvoiceDate)
            .ToListAsync();

        var today = asOf.Date;
        var candidates = new List<ReminderCandidate>();

        foreach (var invoice in invoices)
        {
            var creditDays = invoice.Customer?.CreditDays ?? 0;
            var dueDate = invoice.InvoiceDate.Date.AddDays(creditDays);
            var daysToOrFromDue = (dueDate - today).Days;
            var daysPastDue = daysToOrFromDue < 0 ? -daysToOrFromDue : 0;

            var stage = daysToOrFromDue switch
            {
                >= 0 when daysToOrFromDue <= settings.FirstNoticeDaysBeforeDue => ReminderStages.Upcoming,
                < 0 when settings.SecondNoticeDaysAfterDue > 0 && daysPastDue >= settings.SecondNoticeDaysAfterDue
                    => ReminderStages.SecondNotice,
                < 0 => ReminderStages.Overdue,
                _ => ReminderStages.None,
            };

            var needsEmail = string.Equals(settings.Channel, "Email", StringComparison.OrdinalIgnoreCase);
            var recipient = (needsEmail ? invoice.Customer?.Email : invoice.Customer?.Mobile)?.Trim() ?? string.Empty;

            var candidate = new ReminderCandidate
            {
                InvoiceId = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                InvoiceDate = invoice.InvoiceDate,
                DueDate = dueDate,
                CustomerId = invoice.CustomerId,
                CustomerName = invoice.Customer?.Name ?? invoice.CustomerName ?? "Unknown",
                Recipient = recipient,
                Outstanding = invoice.TotalAmount - invoice.PaidAmount - invoice.WriteOffAmount,
                DaysToOrFromDue = daysToOrFromDue,
                Stage = stage,
            };
            candidate.Message = RenderTemplate(settings, candidate);
            candidate.Included = !string.IsNullOrWhiteSpace(recipient)
                                 && stage != ReminderStages.None
                                 && settings.IsEnabled;
            candidate.Reason = Explain(candidate, settings, stage, needsEmail, recipient, creditDays);
            candidates.Add(candidate);
        }

        return candidates
            .OrderByDescending(c => c.Included)
            .ThenBy(c => c.DueDate)
            .ToList();
    }

    public async Task<ReminderSendResult> SendAsync(IEnumerable<ReminderCandidate> candidates)
    {
        Errors.Clear();
        var result = new ReminderSendResult();
        var list = candidates.ToList();
        if (list.Count == 0)
            return result;

        var settings = await GetSettingsAsync();
        if (!_companyNameResolved)
            await ResolveCompanyNameAsync();

        // The message log is the only record of what already went out, so the dedupe reads it back.
        // Only rows on the configured channel count, and "Failed" rows never block a retry.
        var dayStart = DateTime.UtcNow.Date;
        var logged = (await _dbContext.MessagingLogs.AsNoTracking()
            .Where(l => l.Timestamp >= dayStart && l.Timestamp < dayStart.AddDays(1))
            .Select(l => new { l.Channel, l.Recipient, l.Reference, l.Status })
            .ToListAsync())
            .Where(l => string.Equals(l.Channel, settings.Channel, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(l.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // An invoice only sits in one stage on a given day, so "recipient + invoice for today"
        // is exactly "never the same invoice + stage twice today".
        var alreadySent = new HashSet<string>(
            logged.Select(l => InvoiceKey(l.Recipient, l.Reference)), StringComparer.OrdinalIgnoreCase);

        // The daily cap is per customer, counted on their channel address across all their invoices.
        var perRecipient = logged
            .GroupBy(l => RecipientKey(l.Recipient), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var attempts = 0;

        foreach (var candidate in list)
        {
            var row = new ReminderSendRow
            {
                InvoiceNumber = candidate.InvoiceNumber,
                CustomerName = candidate.CustomerName,
                Recipient = candidate.Recipient,
                Stage = candidate.Stage,
            };

            var skip = Screen(candidate, settings, alreadySent, perRecipient);
            if (skip != null)
            {
                row.Outcome = "Skipped";
                row.Detail = skip;
                result.Skipped++;
                result.Rows.Add(row);
                continue;
            }

            var key = InvoiceKey(candidate.Recipient, candidate.InvoiceNumber);
            var recipientKey = RecipientKey(candidate.Recipient);
            var message = RenderTemplate(settings, candidate);
            var (ok, status, detail) = settings.Channel switch
            {
                "Email" => await _messagingService.SendEmailAsync(
                    candidate.Recipient, $"{candidate.InvoiceNumber} - payment reminder", message, candidate.InvoiceNumber),
                "SMS" => await _messagingService.SendSmsAsync(candidate.Recipient, message, candidate.InvoiceNumber),
                _ => await _messagingService.SendWhatsAppAsync(candidate.Recipient, message, candidate.InvoiceNumber),
            };
            attempts++;

            // "Draft" means the Messaging Center logged it but nothing left the machine (no SMS gateway).
            row.Outcome = ok ? "Sent" : status == "Draft" ? "Skipped" : "Failed";
            row.Status = status;
            row.Detail = detail;
            alreadySent.Add(key);
            perRecipient[recipientKey] = perRecipient.TryGetValue(recipientKey, out var count) ? count + 1 : 1;

            if (ok) result.Sent++;
            else if (row.Outcome == "Skipped") result.Skipped++;
            else
            {
                result.Failed++;
                Errors.Add($"{candidate.InvoiceNumber}: {detail}");
            }

            result.Rows.Add(row);

            await _auditService.RecordAsync("Send", "ReminderSetting", candidate.InvoiceId,
                $"{candidate.Stage} reminder for {candidate.InvoiceNumber}", null,
                new { candidate.InvoiceNumber, candidate.CustomerName, settings.Channel, candidate.Recipient, row.Outcome, row.Status });
        }

        if (attempts > 0)
        {
            settings.LastRunAt = DateTime.UtcNow;
            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                Errors.Add("Could not record the last-run time for the reminder run.");
            }
        }

        return result;
    }

    public async Task<string?> RunAutoRemindersAsync(DateTime now)
    {
        try
        {
            var settings = await GetSettingsAsync();
            if (!settings.AutoRunEnabled)
                return null;

            // The scheduler ticks every minute, but the window may open at most once per day.
            if (settings.LastAutoRunAt?.Date == now.Date)
                return null;

            var scheduled = new TimeSpan(
                Math.Clamp(settings.AutoRunHour, 0, 23),
                Math.Clamp(settings.AutoRunMinute, 0, 59),
                0);
            if (now.TimeOfDay < scheduled)
                return null;

            // Reuse the exact manual pipeline: same rule set, same dedupe, same daily cap.
            var candidates = await GetCandidatesAsync(now);
            var result = await SendAsync(candidates);

            var summary = $"Auto-run at {now:dd-MMM-yyyy HH:mm}: {result.Summary}";
            if (Errors.Count > 0)
                summary += $" First problem: {Errors[0]}";

            await RecordAutoRunAsync(settings, now, summary);
            return summary;
        }
        catch (Exception ex)
        {
            // Never throw into the scheduler tick: report the failure on the settings row instead.
            var text = $"error: automatic reminder run failed: {ex.Message}";
            try
            {
                var settings = await GetSettingsAsync();
                await RecordAutoRunAsync(settings, now, text);
            }
            catch { /* the tick must swallow everything */ }
            return text;
        }
    }

    /// <summary>
    /// Persists the auto-run stamp + outcome on the settings row. Setting LastAutoRunAt even on
    /// failure stops the following ticks from retrying the same day.
    /// </summary>
    private async Task RecordAutoRunAsync(ReminderSetting settings, DateTime now, string result)
    {
        settings.LastAutoRunAt = now;
        settings.LastAutoResult = result.Length > 300 ? result[..300] : result;
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            // Non-fatal: the next tick simply re-enters (and the once-per-day guard may
            // let it rerun), nothing on the timer thread is affected.
        }
    }

    public string RenderTemplate(ReminderSetting setting, ReminderCandidate candidate)
    {
        var template = string.IsNullOrWhiteSpace(setting.MessageTemplate)
            ? ReminderSetting.DefaultTemplate
            : setting.MessageTemplate;

        return Regex.Replace(template, @"\{(\w+)\}", match => match.Groups[1].Value.ToUpperInvariant() switch
        {
            "CUSTOMER" => candidate.CustomerName,
            "INVOICENUMBER" => candidate.InvoiceNumber,
            "AMOUNT" => "₹" + candidate.Outstanding.ToString("#,##0.00", CultureInfo.InvariantCulture),
            "DUEDATE" => candidate.DueDate.ToString("dd-MMM-yyyy"),
            "DAYS" => ReminderCandidate.FormatDays(candidate.DaysToOrFromDue),
            "COMPANY" => _companyName,
            // Unknown tokens are left as written so a typo stays visible in the preview.
            _ => match.Value,
        });
    }

    // --- helpers ---

    private string Explain(ReminderCandidate candidate, ReminderSetting settings, string stage,
        bool needsEmail, string recipient, int creditDays)
    {
        if (!settings.IsEnabled)
            return $"Would be {candidate.StageLabel}, but automatic reminders are switched off.";

        if (stage == ReminderStages.None)
            return $"Not queued: due on {candidate.DueDate:dd-MMM-yyyy} ({ReminderCandidate.FormatDays(candidate.DaysToOrFromDue)}); " +
                   $"the first notice only opens {settings.FirstNoticeDaysBeforeDue} day(s) before the due date " +
                   $"(credit days {creditDays}).";

        if (string.IsNullOrWhiteSpace(recipient))
            return candidate.CustomerId == null
                ? "Not queued: the invoice has no customer record, so there is nobody to message."
                : needsEmail
                    ? "Not queued: no email address on the customer record for the Email channel."
                    : "Not queued: no mobile number on the customer record for the WhatsApp/SMS channel.";

        return $"Queued as {candidate.StageLabel} — {ReminderCandidate.FormatDays(candidate.DaysToOrFromDue)}, over {settings.Channel}.";
    }

    private static string? Screen(ReminderCandidate candidate, ReminderSetting settings,
        HashSet<string> alreadySent, Dictionary<string, int> perRecipient)
    {
        if (!candidate.Included)
            return string.IsNullOrWhiteSpace(candidate.Reason)
                ? "Not queued by the reminder rules."
                : candidate.Reason;

        if (string.IsNullOrWhiteSpace(candidate.Recipient))
            return "Skipped: no recipient for the configured channel.";

        if (alreadySent.Contains(InvoiceKey(candidate.Recipient, candidate.InvoiceNumber)))
            return $"Skipped: a {settings.Channel} reminder for invoice {candidate.InvoiceNumber} is already in today's message log.";

        if (perRecipient.TryGetValue(RecipientKey(candidate.Recipient), out var sent)
            && sent >= settings.MaxPerCustomerPerDay)
            return $"Skipped: daily cap of {settings.MaxPerCustomerPerDay} message(s) per customer reached.";

        return null;
    }

    // The log columns are TEXT NULL in SQLite even though the model marks them non-nullable.
    private static string InvoiceKey(string? recipient, string? reference) =>
        $"{(recipient ?? string.Empty).Trim()}|{(reference ?? string.Empty).Trim()}";

    private static string RecipientKey(string? recipient) => (recipient ?? string.Empty).Trim();

    private string? Validate(ReminderSetting setting)
    {
        if (!Channels.Contains(setting.Channel))
            return $"Unknown reminder channel '{setting.Channel}'.";

        if (setting.FirstNoticeDaysBeforeDue is < 0 or > 90)
            return "The first notice must be between 0 and 90 days before the due date.";

        if (setting.SecondNoticeDaysAfterDue is < 0 or > 180)
            return "The second notice must be between 0 and 180 days after the due date (0 disables it).";

        if (setting.MaxPerCustomerPerDay is < 1 or > 20)
            return "The per-customer daily cap must be between 1 and 20.";

        if (setting.AutoRunHour is < 0 or > 23)
            return "The automatic run hour must be between 0 and 23.";

        if (setting.AutoRunMinute is < 0 or > 59)
            return "The automatic run minute must be between 0 and 59.";

        if (string.IsNullOrWhiteSpace(setting.MessageTemplate))
            return "The message template cannot be empty.";

        if (setting.MessageTemplate.Length > 500)
            return "The message template must be 500 characters or fewer.";

        if (!Regex.IsMatch(setting.MessageTemplate, @"\{(Customer|InvoiceNumber|Amount|DueDate|Days|Company)\}", RegexOptions.IgnoreCase))
            return "The message template must use at least one placeholder: {Customer} {InvoiceNumber} {Amount} {DueDate} {Days} {Company}.";

        return null;
    }

    private async Task<string> ResolveCompanyNameAsync()
    {
        var header = await _dbContext.InvoiceTemplateSettings.AsNoTracking()
            .Select(t => t.HeaderText)
            .FirstOrDefaultAsync();
        _companyNameResolved = true;
        _companyName = string.IsNullOrWhiteSpace(header) ? "our store" : header.Trim();
        return _companyName;
    }
}
