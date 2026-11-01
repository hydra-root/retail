using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface IAccountGroupService
{
    Task<List<AccountGroup>> GetAllAccountGroupsAsync();
    ValueTask<AccountGroup?> GetAccountGroupByIdAsync(int id);
    Task<bool> AddAccountGroupAsync(AccountGroup group);
    Task<bool> UpdateAccountGroupAsync(AccountGroup group);
    Task<bool> DeleteAccountGroupAsync(int id);
    Task<int> SaveChangesAsync();
}
