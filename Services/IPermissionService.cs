using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface IPermissionService
{
    Task<List<Permission>> GetAllPermissionsAsync();
    ValueTask<Permission?> GetPermissionByIdAsync(int id);
    Task<bool> AddPermissionAsync(Permission permission);
    Task<bool> UpdatePermissionAsync(Permission permission);
    Task<bool> DeletePermissionAsync(int id);
    Task<int> SaveChangesAsync();
}
