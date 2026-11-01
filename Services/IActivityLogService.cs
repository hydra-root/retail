using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public interface IActivityLogService
{
    Task LogAsync(int? userId, string? username, int? storeId, string action,
        string? entityType = null, string? entityReference = null, string? details = null);
    Task<List<ActivityLog>> GetRecentLogsAsync(int count = 500);
    Task<List<ActivityLog>> SearchLogsAsync(string? username = null, string? action = null,
        DateTime? from = null, DateTime? to = null, int count = 500);
}
