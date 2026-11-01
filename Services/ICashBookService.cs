using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// PAY-10: cash &amp; bank book - cash/bank account listing, combined or
/// per-account cash book, and contra (internal) transfers.
/// </summary>
public interface ICashBookService
{
    /// <summary>Accounts whose name contains Cash or Bank (case-insensitive).</summary>
    Task<List<Account>> GetCashBankAccountsAsync();

    /// <summary>
    /// Cash book for one account, or the combined book of all cash/bank
    /// accounts when accountId is null. Internal transfers are contra rows
    /// that cancel out in the combined book.
    /// </summary>
    Task<CashBookReport> GetCashBookAsync(int? accountId, DateTime startDate, DateTime endDate);

    /// <summary>Move money between two cash/bank accounts (journal contra). Returns an error message on failure.</summary>
    Task<(bool Success, string? Error)> TransferAsync(int fromAccountId, int toAccountId, decimal amount, DateTime date, string? narration);
}

/// <summary>PAY-10: one cash book line.</summary>
public class CashBookRow
{
    public DateTime Date { get; set; }

    /// <summary>Account name, or "From → To" for combined-book transfer rows.</summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>Receipt / Payment / Transfer (Contra In) / Transfer (Contra Out).</summary>
    public string Type { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;

    public string? Narration { get; set; }

    /// <summary>Money into the book (journal debit on cash/bank).</summary>
    public decimal In { get; set; }

    /// <summary>Money out of the book (journal credit on cash/bank).</summary>
    public decimal Out { get; set; }

    public decimal RunningBalance { get; set; }
}

/// <summary>PAY-10: cash book period summary.</summary>
public class CashBookReport
{
    public decimal OpeningBalance { get; set; }
    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }
    public decimal ClosingBalance => OpeningBalance + TotalIn - TotalOut;
    public List<CashBookRow> Rows { get; set; } = new();
}
