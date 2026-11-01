using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface IInvoiceService
{
    /// <summary>Human-readable reason for the most recent validation failure (STK-06/PAY-05).</summary>
    string? LastValidationError { get; }

    /// <summary>STK-06: non-blocking near-expiry (≤30 days) warnings from the last create call.</summary>
    List<string> NearExpiryWarnings { get; }

    /// <summary>PAY-05: non-blocking credit-limit/override warnings from the last create call.</summary>
    List<string> CreditLimitWarnings { get; }

    /// <summary>PAY-05: reason the manager override did not take effect (bad user/password).</summary>
    string? LastOverrideError { get; }

    /// <summary>STK-03: non-blocking negative-stock warnings from the last create/convert call.</summary>
    List<string> StockPolicyWarnings { get; }

    Task<List<Invoice>> GetAllInvoicesAsync();
    Task<Invoice?> GetInvoiceByIdAsync(int id);
    Task<Invoice?> GetInvoiceByNumberAsync(string invoiceNumber);
    /// <summary>PAY-05: manager override accepts optional admin username/password to pass a credit-limit block.</summary>
    Task<bool> CreateInvoiceAsync(Invoice invoice, string? overrideUsername = null, string? overridePassword = null);
    Task<bool> ConvertEstimateToInvoiceAsync(int invoiceId);
    Task<bool> WriteOffInvoiceAsync(int invoiceId, decimal writeOffAmount);
    Task<bool> UpdateInvoiceAsync(Invoice invoice, string? overrideUsername = null, string? overridePassword = null);
    Task<bool> DeleteInvoiceAsync(int id, string? overrideUsername = null, string? overridePassword = null);
    Task<string> GenerateNextInvoiceNumberAsync();
    Task<List<Invoice>> GetPendingInvoicesAsync();
    Task<int> SaveChangesAsync();
}
