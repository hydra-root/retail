using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class AccountService : IAccountService
{
    private readonly AppDbContext _dbContext;

    public AccountService(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<List<Account>> GetAllAccountsAsync() => _dbContext.Accounts.Include(a => a.Group).ToListAsync();

    public Task<Account?> GetAccountByIdAsync(int id) => _dbContext.Accounts.Include(a => a.Group).FirstOrDefaultAsync(a => a.Id == id);

    public async Task<bool> AddAccountAsync(Account account)
    {
        try
        {
            _dbContext.Accounts.Add(account);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateAccountAsync(Account account)
    {
        try
        {
            _dbContext.Accounts.Update(account);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteAccountAsync(int id)
    {
        try
        {
            var account = await _dbContext.Accounts.FindAsync(id);
            if (account == null) return false;
            _dbContext.Accounts.Remove(account);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
}
