using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.Reflection;
using System.Text.Json;

namespace RetailBillingApp.Services;

/// <summary>
/// ACC-10: records field-level before/after diffs for tracked entities.
/// Logging must never break the main flow, so every write is best-effort.
/// </summary>
public class AuditService : IAuditService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuthService _authService;

    public AuditService(AppDbContext dbContext, IAuthService authService)
    {
        _dbContext = dbContext;
        _authService = authService;
    }

    public async Task RecordAsync(string action, string entityName, int entityId, string? entityLabel = null,
        object? before = null, object? after = null)
    {
        try
        {
            var changes = Diff(before, after);
            var snapshot = after != null ? JsonSerializer.Serialize(Sanitize(after)) : null;

            _dbContext.AuditRecords.Add(new AuditRecord
            {
                Username = _authService.CurrentUser?.Username ?? "system",
                Action = action,
                EntityName = entityName,
                EntityId = entityId,
                EntityLabel = entityLabel,
                ChangesJson = changes,
                SnapshotJson = snapshot,
                Timestamp = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            // Logging must never break the main flow.
        }
    }

    public Task<List<AuditRecord>> QueryAsync(string? username = null, string? action = null,
        string? entityName = null, DateTime? from = null, DateTime? to = null, int count = 500)
    {
        var query = _dbContext.AuditRecords.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(username))
            query = query.Where(a => a.Username.Contains(username));
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action.Contains(action));
        if (!string.IsNullOrWhiteSpace(entityName))
            query = query.Where(a => a.EntityName.Contains(entityName));
        if (from.HasValue) query = query.Where(a => a.Timestamp >= from.Value);
        if (to.HasValue) query = query.Where(a => a.Timestamp < to.Value.AddDays(1));

        return query
            .OrderByDescending(a => a.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    /// <summary>Build { "Prop": { "before": ..., "after": ... } } for every changed scalar property.</summary>
    private static string? Diff(object? before, object? after)
    {
        if (after == null) return null; // delete: nothing to diff yet

        var dict = new Dictionary<string, object>();
        var beforeProps = Props(before);
        var afterProps = Props(after);

        foreach (var (name, afterValue) in afterProps)
        {
            beforeProps.TryGetValue(name, out var beforeValue);
            if (!Equals(beforeValue, afterValue))
                dict[name] = new { @before = beforeValue, @after = afterValue };
        }

        return dict.Count == 0
            ? "{}"
            : JsonSerializer.Serialize(dict, JsonOpts);
    }

    private static Dictionary<string, object?> Props(object? obj)
    {
        if (obj == null) return new();
        return obj.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && IsSimple(p.PropertyType))
            .ToDictionary(p => p.Name, p => p.GetValue(obj));
    }

    private static bool IsSimple(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)
            || t == typeof(DateTime) || t == typeof(Guid);
    }

    /// <summary>Snapshot serialization keeps it readable: entity scalars only, no navigation collections.</summary>
    private static object Sanitize(object obj) =>
        obj.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && IsSimple(p.PropertyType))
            .ToDictionary(p => p.Name, p => p.GetValue(obj));

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
}
