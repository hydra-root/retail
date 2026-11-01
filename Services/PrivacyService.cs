using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.IO;

namespace RetailBillingApp.Services;

/// <summary>
/// SEC-10: privacy and compliance. Audit/activity-log retention purging via
/// raw SQL delete, retention previews, and CSV data export for GDPR-style
/// access requests.
/// </summary>
public class PrivacyService : IPrivacyService
{
    private readonly AppDbContext _dbContext;

    public PrivacyService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<(int RowsBefore, int RowsAfter)> PurgeAuditTrailAsync(int olderThanDays)
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-olderThanDays);
        var before = await CountAuditAsync(cutoff);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "DELETE FROM AuditRecords WHERE Timestamp < {0}", cutoff);
        var after = await _dbContext.AuditRecords
            .CountAsync(a => a.Timestamp < cutoff);
        return (before, after);
    }

    public async Task<(int RowsBefore, int RowsAfter)> PurgeActivityLogsAsync(int olderThanDays)
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-olderThanDays);
        var before = await CountLogsAsync(cutoff);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "DELETE FROM ActivityLogs WHERE Timestamp < {0}", cutoff);
        var after = await _dbContext.ActivityLogs
            .CountAsync(l => l.Timestamp < cutoff);
        return (before, after);
    }

    public async Task<(int AuditRecords, int ActivityLogs)> GetRetentionPreviewAsync(int days)
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-days);
        return (await CountAuditAsync(cutoff), await CountLogsAsync(cutoff));
    }

    public async Task<string> ExportCustomerDataAsync(int customerId, string? formatHint = null)
    {
        var customer = await _dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId)
            ?? throw new InvalidOperationException($"Customer {customerId} not found.");

        var invoices = await _dbContext.Invoices.AsNoTracking()
            .Where(i => i.CustomerId == customerId)
            .OrderBy(i => i.InvoiceDate)
            .Select(i => new { i.InvoiceNumber, i.InvoiceDate, i.TotalAmount, i.PaidAmount, i.Status })
            .ToListAsync();

        var payments = await _dbContext.Payments.AsNoTracking()
            .Where(p => p.PartyType == "Customer" && p.PartyId == customerId)
            .OrderBy(p => p.PaymentDate)
            .Select(p => new { p.PaymentNumber, p.PaymentDate, p.Amount, p.PaymentMode })
            .ToListAsync();

        var sb = new System.Text.StringBuilder();
        var exportedAt = System.DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        sb.AppendLine($"Customer Data Export,,{exportedAt} UTC");
        sb.AppendLine();
        sb.AppendLine("CUSTOMER PROFILE");
        sb.AppendLine("Field,Value");
        sb.AppendLine($"CustomerId,{customer.Id}");
        sb.AppendLine($"Name,{Escape(customer.Name)}");
        sb.AppendLine($"Mobile,{Escape(customer.Mobile)}");
        sb.AppendLine($"Email,{Escape(customer.Email)}");
        sb.AppendLine($"Address,{Escape(customer.Address)}");
        sb.AppendLine($"GSTIN,{Escape(customer.GSTIN)}");
        sb.AppendLine($"State,{Escape(customer.State)}");
        sb.AppendLine($"CustomerGroup,{Escape(customer.CustomerGroup)}");
        sb.AppendLine($"CreditLimit,{customer.CreditLimit:F2}");
        sb.AppendLine($"CreditDays,{customer.CreditDays}");
        sb.AppendLine($"CreatedAt,{customer.CreatedAt:yyyy-MM-dd}");
        sb.AppendLine($"InvoiceCount,{invoices.Count}");
        sb.AppendLine($"PaymentCount,{payments.Count}");
        sb.AppendLine();

        sb.AppendLine("INVOICES");
        sb.AppendLine("InvoiceNumber,Date,Total,Paid,Status");
        foreach (var i in invoices)
            sb.AppendLine($"{Escape(i.InvoiceNumber)},{i.InvoiceDate:yyyy-MM-dd},{i.TotalAmount:F2},{i.PaidAmount:F2},{Escape(i.Status)}");
        sb.AppendLine();

        sb.AppendLine("PAYMENTS");
        sb.AppendLine("PaymentNumber,Date,Amount,Mode");
        foreach (var p in payments)
            sb.AppendLine($"{Escape(p.PaymentNumber)},{p.PaymentDate:yyyy-MM-dd},{p.Amount:F2},{Escape(p.PaymentMode)}");

        var fileName = $"CustomerData_Export_{customerId}_{DateTime.UtcNow:yyyyMMdd}.csv";
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
        await File.WriteAllTextAsync(path, sb.ToString());
        return path;
    }

    private async Task<int> CountAuditAsync(DateTime cutoff) =>
        await _dbContext.AuditRecords.AsNoTracking().CountAsync(a => a.Timestamp < cutoff);

    private async Task<int> CountLogsAsync(DateTime cutoff) =>
        await _dbContext.ActivityLogs.AsNoTracking().CountAsync(l => l.Timestamp < cutoff);

    private static string Escape(string? value) =>
        $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
