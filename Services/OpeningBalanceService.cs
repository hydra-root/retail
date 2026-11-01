using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class OpeningBalanceService : IOpeningBalanceService
{
    private readonly AppDbContext _dbContext;

    public OpeningBalanceService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<List<Account>> GetAccountsWithOpeningAsync()
    {
        return await _dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.AccountType)
            .ThenBy(a => a.Name)
            .ToListAsync();
    }

    public async Task<(bool Ok, string? Error, decimal Difference)> SaveOpeningBalanceAsync(int accountId, decimal newOpeningBalance)
    {
        try
        {
            var account = await _dbContext.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
            if (account == null)
                return (false, "Account not found.", 0m);

            var difference = newOpeningBalance - account.OpeningBalance;
            account.OpeningBalance = newOpeningBalance;
            await _dbContext.SaveChangesAsync();

            return (true, null, difference);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, 0m);
        }
    }

    public async Task<List<OpeningBalanceSummaryRow>> GetOpeningBalancesSummaryAsync()
    {
        return await _dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.IsActive)
            .GroupBy(a => a.AccountType)
            .Select(g => new OpeningBalanceSummaryRow(g.Key, g.Sum(a => a.OpeningBalance), g.Count()))
            .OrderBy(r => r.AccountType)
            .ToListAsync();
    }
}
