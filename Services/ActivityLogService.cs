using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>USR-07: persistent activity log for logins and sensitive actions.</summary>
public class ActivityLogService : IActivityLogService
{
    private readonly AppDbContext _dbContext;

    public ActivityLogService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task LogAsync(int? userId, string? username, int? storeId, string action,
        string? entityType = null, string? entityReference = null, string? details = null)
    {
        try
        {
            _dbContext.ActivityLogs.Add(new ActivityLog
            {
                UserId = userId,
                Username = username,
                StoreId = storeId,
                Action = action,
                EntityType = entityType,
                EntityReference = entityReference,
                Details = details,
                Timestamp = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            // Logging must never break the main flow.
        }
    }

    public Task<List<ActivityLog>> GetRecentLogsAsync(int count = 500) =>
        SearchLogsAsync(count: count);

    public async Task<List<ActivityLog>> SearchLogsAsync(string? username = null, string? action = null,
        DateTime? from = null, DateTime? to = null, int count = 500)
    {
        var query = _dbContext.ActivityLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(username))
            query = query.Where(l => l.Username != null && l.Username.Contains(username));
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(l => l.Action.Contains(action));
        if (from.HasValue) query = query.Where(l => l.Timestamp >= from.Value);
        if (to.HasValue) query = query.Where(l => l.Timestamp < to.Value.AddDays(1));

        return await query
            .OrderByDescending(l => l.Timestamp)
            .Take(count)
            .ToListAsync();
    }
}
