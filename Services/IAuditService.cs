using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

/// <summary>ACC-10 Audit trail service: field-level before/after change records.</summary>
public interface IAuditService
{
    Task RecordAsync(string action, string entityName, int entityId, string? entityLabel = null,
        object? before = null, object? after = null);

    Task<List<AuditRecord>> QueryAsync(string? username = null, string? action = null,
        string? entityName = null, System.DateTime? from = null, System.DateTime? to = null,
        int count = 500);
}
