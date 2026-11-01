namespace RetailBillingApp.Services;

/// <summary>One line in the last-action log of a privacy operation.</summary>
public class PrivacyResultRow
{
    public string Action { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;
}

/// <summary>
/// SEC-10: audit/activity retention purging, retention preview and GDPR-style
/// personal data export.
/// </summary>
public interface IPrivacyService
{
    /// <summary>Deletes audit trail records older than the cutoff. Returns (rowsBefore, rowsAfter).</summary>
    Task<(int RowsBefore, int RowsAfter)> PurgeAuditTrailAsync(int olderThanDays);

    /// <summary>Deletes activity log records older than the cutoff. Returns (rowsBefore, rowsAfter).</summary>
    Task<(int RowsBefore, int RowsAfter)> PurgeActivityLogsAsync(int olderThanDays);

    /// <summary>
    /// Exports a customer's personal data (profile, invoices, payments) as a CSV
    /// on the Desktop. Returns the written file path.
    /// </summary>
    Task<string> ExportCustomerDataAsync(int customerId, string? formatHint = null);

    /// <summary>Counts records older than the cutoff without deleting anything.</summary>
    Task<(int AuditRecords, int ActivityLogs)> GetRetentionPreviewAsync(int days);
}
