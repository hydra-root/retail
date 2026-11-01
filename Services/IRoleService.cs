using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface IRoleService
{
    Task<List<Role>> GetAllRolesAsync();
    ValueTask<Role?> GetRoleByIdAsync(int id);
    Task<bool> AddRoleAsync(Role role);
    Task<bool> UpdateRoleAsync(Role role);
    Task<bool> DeleteRoleAsync(int id);
    Task<int> SaveChangesAsync();
}
