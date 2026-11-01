using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface ICreditNoteService
{
    /// <summary>Human-readable reason for the most recent validation failure (INV-11).</summary>
    string? LastValidationError { get; }

    /// <summary>STK-03: non-blocking negative-stock warnings from the last create call.</summary>
    List<string> StockPolicyWarnings { get; }

    Task<List<CreditNote>> GetAllCreditNotesAsync();
    Task<CreditNote?> GetCreditNoteByIdAsync(int id);
    Task<string> GenerateNextCreditNoteNumberAsync();
    Task<bool> CreateCreditNoteAsync(CreditNote creditNote);
    Task<bool> CancelNoteAsync(int id);
    Task<bool> UpdateCreditNoteAsync(CreditNote creditNote);
    Task<bool> DeleteCreditNoteAsync(int id);
    Task<List<CreditNote>> GetCreditNotesByInvoiceAsync(int invoiceId);
    Task<int> SaveChangesAsync();
}
