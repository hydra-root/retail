using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class AccountGroupService : IAccountGroupService
{
    private readonly AppDbContext _dbContext;

    public AccountGroupService(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<List<AccountGroup>> GetAllAccountGroupsAsync() => _dbContext.AccountGroups.ToListAsync();

    public ValueTask<AccountGroup?> GetAccountGroupByIdAsync(int id) => _dbContext.AccountGroups.FindAsync(id);

    public async Task<bool> AddAccountGroupAsync(AccountGroup group)
    {
        try
        {
            _dbContext.AccountGroups.Add(group);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateAccountGroupAsync(AccountGroup group)
    {
        try
        {
            _dbContext.AccountGroups.Update(group);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteAccountGroupAsync(int id)
    {
        try
        {
            var group = await _dbContext.AccountGroups.FindAsync(id);
            if (group == null) return false;
            _dbContext.AccountGroups.Remove(group);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
}
