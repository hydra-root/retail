using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// ACC-06: bank reconciliation - import statement CSVs, auto-match against
/// Cash/Bank journal lines, manual reconcile, summary.
/// </summary>
public interface IBankReconciliationService
{
    /// <summary>Import a bank statement CSV (flexible headers). Returns number of rows imported.</summary>
    Task<(int Imported, List<string> Warnings)> ImportCsvAsync(string filePath);

    /// <summary>All statement lines, newest first. statusFilter: "All" / "Unmatched" / "Reconciled".</summary>
    Task<List<BankStatementLine>> GetLinesAsync(string statusFilter);

    /// <summary>
    /// Auto-match unmatched lines against Cash/Bank journal lines by equal amount
    /// and date within ±5 days. Returns number of lines matched.
    /// </summary>
    Task<int> AutoMatchAsync();

    /// <summary>Manually mark one line reconciled (without a journal match). Returns false if not found.</summary>
    Task<bool> MarkReconciledAsync(int lineId);

    /// <summary>Clear a line's match/reconciliation back to unmatched.</summary>
    Task<bool> ClearMatchAsync(int lineId);

    Task<BankReconciliationSummary> GetSummaryAsync();
}

/// <summary>ACC-06: reconciliation progress summary.</summary>
public class BankReconciliationSummary
{
    public int TotalLines { get; set; }
    public int ReconciledLines { get; set; }
    public int UnmatchedLines => TotalLines - ReconciledLines;

    /// <summary>Sum of statement amounts (deposits positive, withdrawals negative).</summary>
    public decimal TotalStatementAmount { get; set; }

    /// <summary>Net amount of matched lines - what the books agree with.</summary>
    public decimal MatchedAmount { get; set; }

    public decimal UnmatchedAmount => TotalStatementAmount - MatchedAmount;
}
