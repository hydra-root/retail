using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public interface IOpeningBalanceService
{
    /// <summary>All active accounts (no tracking) ordered by AccountType then Name.</summary>
    Task<List<Account>> GetAccountsWithOpeningAsync();

    /// <summary>
    /// Updates an account's opening balance. Opening balances are not journaled;
    /// the amount changed (difference) is returned so the UI can report the adjustment.
    /// </summary>
    Task<(bool Ok, string? Error, decimal Difference)> SaveOpeningBalanceAsync(int accountId, decimal newOpeningBalance);

    /// <summary>Opening balance totals grouped by AccountType for the wizard's summary card.</summary>
    Task<List<OpeningBalanceSummaryRow>> GetOpeningBalancesSummaryAsync();
}

public record OpeningBalanceSummaryRow(string AccountType, decimal TotalOpeningBalance, int AccountCount);
