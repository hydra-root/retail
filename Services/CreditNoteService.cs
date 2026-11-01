using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// INV-11: credit/debit notes — return against an invoice, GSTR-1 CDNR ready.
/// Numbering comes from the CreditNote number series (INV-03), stock moves go
/// through the shared negative-stock policy (STK-03), issuance posts a balanced
/// double-entry journal (ACC-02) and every create/cancel lands in the audit
/// trail (ACC-10). Cancelled notes stay in the table so the GST reports can
/// filter them (Status != "Cancelled").
/// </summary>
public class CreditNoteService : ICreditNoteService
{
    private readonly AppDbContext _dbContext;
    private readonly IFinancialYearService _financialYearService;
    private readonly INumberSeriesService _numberSeriesService;
    private readonly IAuditService _auditService;
    private readonly IStockPolicyService _stockPolicyService;

    /// <summary>Human-readable reason for the most recent validation failure (INV-11).</summary>
    public string? LastValidationError { get; private set; }

    /// <summary>STK-03: non-blocking negative-stock warnings from the last create call.</summary>
    public List<string> StockPolicyWarnings { get; } = new();

    public CreditNoteService(AppDbContext dbContext, IFinancialYearService financialYearService,
        INumberSeriesService numberSeriesService, IAuditService auditService,
        IStockPolicyService stockPolicyService)
    {
        _dbContext = dbContext;
        _financialYearService = financialYearService;
        _numberSeriesService = numberSeriesService;
        _auditService = auditService;
        _stockPolicyService = stockPolicyService;
    }

    public Task<List<CreditNote>> GetAllCreditNotesAsync() =>
        _dbContext.CreditNotes
            .Include(c => c.Items)
            .Include(c => c.Customer)
            .Include(c => c.OriginalInvoice)
            .OrderByDescending(c => c.CreditNoteDate)
            .ToListAsync();

    public Task<CreditNote?> GetCreditNoteByIdAsync(int id) =>
        _dbContext.CreditNotes
            .Include(c => c.Items)
            .Include(c => c.Customer)
            .Include(c => c.OriginalInvoice)
            .FirstOrDefaultAsync(c => c.Id == id);

    /// <summary>
    /// INV-11/INV-03: issue the next number from the CreditNote series of the
    /// active financial year. Falls back to the current April-start Indian FY
    /// label (e.g. "2026-27") when no year has been marked active yet.
    /// </summary>
    public async Task<string> GenerateNextCreditNoteNumberAsync()
    {
        var financialYear = await GetActiveFinancialYearAsync();
        return await _numberSeriesService.GetNextNumberAsync("CreditNote", financialYear);
    }

    /// <summary>The active FY name, or the derived current one when no year is active.</summary>
    private async Task<string> GetActiveFinancialYearAsync()
    {
        var year = await _financialYearService.GetCurrentFinancialYearAsync();
        if (year != null && !string.IsNullOrWhiteSpace(year.Name))
            return year.Name;

        // No active year configured: derive "yyyy-yy" (April-start Indian FY).
        var today = System.DateTime.Today;
        var start = today.Month >= 4 ? today.Year : today.Year - 1;
        return $"{start}-{(start + 1) % 100:D2}";
    }

    public async Task<bool> CreateCreditNoteAsync(CreditNote creditNote)
    {
        try
        {
            LastValidationError = null;
            StockPolicyWarnings.Clear();

            // INV-11 validation with reasons (LastValidationError convention).
            if (creditNote.Items == null || creditNote.Items.Count == 0)
            {
                LastValidationError = "A credit/debit note must carry at least one item.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(creditNote.Reason))
            {
                LastValidationError = "Reason is required on the note.";
                return false;
            }
            foreach (var item in creditNote.Items)
            {
                if (item.Quantity <= 0)
                {
                    LastValidationError = $"Quantity for '{item.ProductName}' must be greater than zero.";
                    return false;
                }
                if (item.Rate < 0)
                {
                    LastValidationError = $"Rate for '{item.ProductName}' cannot be negative.";
                    return false;
                }
            }

            creditNote.SubTotal = creditNote.Items.Sum(i => i.Amount);
            creditNote.TaxAmount = creditNote.Items.Sum(i => i.TaxAmount);
            creditNote.TotalAmount = creditNote.SubTotal + creditNote.TaxAmount;

            // INV-11: return against invoice — the note cannot exceed the invoice's remaining value.
            if (creditNote.OriginalInvoiceId.HasValue)
            {
                var invoice = await _dbContext.Invoices.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == creditNote.OriginalInvoiceId.Value);
                if (invoice == null)
                {
                    LastValidationError = "The linked original invoice no longer exists.";
                    return false;
                }
                if (invoice.IsEstimate)
                {
                    LastValidationError = "Estimates cannot carry credit/debit notes; convert the estimate to an invoice first.";
                    return false;
                }

                var remaining = invoice.TotalAmount - invoice.PaidAmount - invoice.WriteOffAmount;
                if (creditNote.TotalAmount > remaining)
                {
                    LastValidationError =
                        $"{creditNote.Type} note total {creditNote.TotalAmount:C} exceeds the remaining value {remaining:C} of invoice {invoice.InvoiceNumber}.";
                    return false;
                }
                creditNote.OriginalInvoiceNumber ??= invoice.InvoiceNumber;
            }

            using var txn = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                creditNote.CreatedAt = System.DateTime.UtcNow;
                if (string.IsNullOrWhiteSpace(creditNote.CreditNoteNumber))
                    creditNote.CreditNoteNumber = await GenerateNextCreditNoteNumberAsync();
                if (string.IsNullOrWhiteSpace(creditNote.Status))
                    creditNote.Status = "Issued";

                // STK-03: a Debit note deducts stock, so run every line through the
                // shared negative-stock policy before the movement is applied.
                if (creditNote.Type == "Debit" && !await CheckStockPolicyAsync(creditNote))
                    return false;

                _dbContext.CreditNotes.Add(creditNote);
                await _dbContext.SaveChangesAsync();

                await ApplyStockMovementAsync(creditNote);

                await _dbContext.SaveChangesAsync();

                // ACC-02: balanced double-entry posting, same in-transaction pattern as the invoice write-off.
                await PostNoteJournalAsync(creditNote, reversed: false);

                await _dbContext.SaveChangesAsync();
                await txn.CommitAsync();

                // ACC-10: audit the issuance after a successful commit.
                await _auditService.RecordAsync("Create", "CreditNote", creditNote.Id,
                    creditNote.CreditNoteNumber, null, creditNote);
                return true;
            }
            catch
            {
                await txn.RollbackAsync();
                throw;
            }
        }
        catch
        {
            LastValidationError ??= "Failed to create the credit/debit note.";
            return false;
        }
    }

    /// <summary>
    /// INV-11: cancel keeps the note visible for GSTR-1 CDNR (reports filter
    /// Status != "Cancelled") but reverses its stock effect and mirrors its
    /// journal posting.
    /// </summary>
    public async Task<bool> CancelNoteAsync(int id)
    {
        try
        {
            LastValidationError = null;
            StockPolicyWarnings.Clear();

            var creditNote = await _dbContext.CreditNotes
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (creditNote == null)
            {
                LastValidationError = "Note not found.";
                return false;
            }
            if (creditNote.Status == "Cancelled")
            {
                LastValidationError = $"Note {creditNote.CreditNoteNumber} is already cancelled.";
                return false;
            }

            var before = new { creditNote.Status };

            using var txn = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                foreach (var item in creditNote.Items)
                    await ReverseStockItemAsync(creditNote, item, $"Cancelled {creditNote.CreditNoteNumber}");

                creditNote.Status = "Cancelled";

                // ACC-02: mirror the original posting so the ledger nets to zero for this note.
                await PostNoteJournalAsync(creditNote, reversed: true);

                await _dbContext.SaveChangesAsync();
                await txn.CommitAsync();

                // ACC-10: audit the cancellation.
                await _auditService.RecordAsync("Cancel", "CreditNote", creditNote.Id,
                    creditNote.CreditNoteNumber, before, new { creditNote.Status });
                return true;
            }
            catch
            {
                await txn.RollbackAsync();
                throw;
            }
        }
        catch
        {
            LastValidationError ??= "Failed to cancel the note.";
            return false;
        }
    }

    public async Task<bool> UpdateCreditNoteAsync(CreditNote creditNote)
    {
        try
        {
            _dbContext.CreditNotes.Update(creditNote);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    /// <summary>INV-11: delete reverses the live stock effect before removing the note.</summary>
    public async Task<bool> DeleteCreditNoteAsync(int id)
    {
        try
        {
            LastValidationError = null;

            var creditNote = await _dbContext.CreditNotes
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (creditNote == null) return false;

            var before = new { creditNote.Status, creditNote.TotalAmount };

            using var txn = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                // A cancelled note already reversed its stock at cancel time; only a live one moves stock now.
                if (creditNote.Status != "Cancelled")
                {
                    foreach (var item in creditNote.Items)
                        await ReverseStockItemAsync(creditNote, item, $"Deleted {creditNote.CreditNoteNumber}");
                }

                _dbContext.CreditNotes.Remove(creditNote);
                await _dbContext.SaveChangesAsync();
                await txn.CommitAsync();

                // ACC-10: audit the delete (items cascade with the note).
                await _auditService.RecordAsync("Delete", "CreditNote", creditNote.Id,
                    creditNote.CreditNoteNumber, before, null);
                return true;
            }
            catch
            {
                await txn.RollbackAsync();
                throw;
            }
        }
        catch
        {
            LastValidationError ??= "Failed to delete the note.";
            return false;
        }
    }

    public Task<List<CreditNote>> GetCreditNotesByInvoiceAsync(int invoiceId) =>
        _dbContext.CreditNotes
            .Include(c => c.Items)
            .Where(c => c.OriginalInvoiceId == invoiceId)
            .ToListAsync();

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();

    /// <summary>
    /// STK-03: judge the debit-note deduction per product against the configured
    /// policy before anything moves. Returns false only in Block mode; Warn mode
    /// records the shortfall on StockPolicyWarnings and lets the note through.
    /// </summary>
    private async Task<bool> CheckStockPolicyAsync(CreditNote creditNote)
    {
        var blocked = new List<string>();

        foreach (var group in creditNote.Items.Where(i => i.ProductId.HasValue).GroupBy(i => i.ProductId!.Value))
        {
            var product = await _dbContext.Products.FindAsync(group.Key);
            if (product == null) continue;

            var asked = group.Sum(i => i.Quantity);
            var (allowed, message) = await _stockPolicyService.EvaluateAsync(
                product.Name, product.StockQty, -asked, $"Debit Note {creditNote.CreditNoteNumber}");

            if (!allowed) blocked.Add(message!);
            else if (message != null) StockPolicyWarnings.Add(message);
        }

        if (blocked.Count > 0)
        {
            LastValidationError = "Not enough stock: " + string.Join(" ", blocked) +
                ". Reduce the quantity, receive a purchase, or relax the policy in Stock Policy.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Apply the issuance stock effect: Credit restores qty (customer return),
    /// Debit deducts qty (goods out without a fresh invoice).
    /// </summary>
    private async Task ApplyStockMovementAsync(CreditNote note)
    {
        foreach (var item in note.Items)
        {
            if (!item.ProductId.HasValue) continue;
            var product = await _dbContext.Products.FindAsync(item.ProductId.Value);
            if (product == null) continue;

            if (note.Type == "Debit")
            {
                product.StockQty -= item.Quantity;
                _dbContext.StockTransactions.Add(new StockTransaction
                {
                    ProductId = item.ProductId.Value,
                    TransactionType = "Debit Note",
                    QuantityChange = -item.Quantity,
                    Reference = $"Debit Note {note.CreditNoteNumber}",
                    TransactionDate = System.DateTime.UtcNow
                });
            }
            else
            {
                product.StockQty += item.Quantity;
                _dbContext.StockTransactions.Add(new StockTransaction
                {
                    ProductId = item.ProductId.Value,
                    TransactionType = "Return",
                    QuantityChange = item.Quantity,
                    Reference = $"Credit Note {note.CreditNoteNumber}",
                    TransactionDate = System.DateTime.UtcNow
                });
            }
            product.UpdatedAt = System.DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Compensating movement for cancel/delete: undo what the issuance did
    /// (Credit restore → deduct again, Debit deduction → add back).
    /// </summary>
    private async Task ReverseStockItemAsync(CreditNote note, CreditNoteItem item, string referenceSuffix)
    {
        if (!item.ProductId.HasValue) return;
        var product = await _dbContext.Products.FindAsync(item.ProductId.Value);
        if (product == null) return;

        var change = note.Type == "Debit" ? item.Quantity : -item.Quantity;
        product.StockQty += change;
        product.UpdatedAt = System.DateTime.UtcNow;

        _dbContext.StockTransactions.Add(new StockTransaction
        {
            ProductId = item.ProductId.Value,
            TransactionType = note.Type == "Debit" ? "Debit Note Reversal" : "Return Reversal",
            QuantityChange = change,
            Reference = $"{note.Type} Note {referenceSuffix}",
            TransactionDate = System.DateTime.UtcNow
        });
    }

    /// <summary>
    /// ACC-02: post the note inside the caller's transaction, mirroring
    /// InvoiceService.WriteOffInvoiceAsync. The app bills gross through Sales and
    /// receives through Debtors (AutoPostingService.PostSaleAsync), so a Credit
    /// note debits Sales (contra-revenue) and credits Debtors; a Debit note is
    /// the reverse. Cancellation posts the mirror image. Only the seeded chart
    /// accounts are used — nothing is invented.
    /// </summary>
    private async Task PostNoteJournalAsync(CreditNote creditNote, bool reversed)
    {
        var amount = creditNote.TotalAmount;
        if (amount <= 0) return;

        var salesAccount = await _dbContext.Accounts.FirstOrDefaultAsync(a => a.Name == "Sales");
        var debtorsAccount = await _dbContext.Accounts.FirstOrDefaultAsync(a => a.Name == "Debtors");
        if (salesAccount == null || debtorsAccount == null) return; // nothing to post to

        // Credit note: Dr Sales / Cr Debtors. Debit note flips it; a reversal flips again.
        var debitSales = (creditNote.Type != "Debit") != reversed;

        var entry = new JournalEntry
        {
            EntryDate = System.DateTime.UtcNow,
            ReferenceType = creditNote.Type == "Debit" ? "Debit Note" : "Credit Note",
            ReferenceId = creditNote.Id.ToString(),
            Narration = (reversed ? "Cancellation of " : "")
                + $"{creditNote.Type} note {creditNote.CreditNoteNumber}"
                + (string.IsNullOrWhiteSpace(creditNote.CustomerName) ? string.Empty : $" for {creditNote.CustomerName}"),
            Lines = new List<JournalLine>
            {
                new JournalLine
                {
                    AccountId = salesAccount.Id,
                    Debit = debitSales ? amount : 0m,
                    Credit = debitSales ? 0m : amount,
                    Narration = debitSales ? "Sales return" : "Sales uplift"
                },
                new JournalLine
                {
                    AccountId = debtorsAccount.Id,
                    Debit = debitSales ? 0m : amount,
                    Credit = debitSales ? amount : 0m,
                    Narration = debitSales ? "Receivable reduced" : "Receivable increased"
                }
            }
        };

        _dbContext.JournalEntries.Add(entry);
    }
}
