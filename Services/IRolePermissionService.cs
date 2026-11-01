using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface IRolePermissionService
{
    Task<List<RolePermission>> GetRolePermissionsAsync(int roleId);
    Task<bool> AssignPermissionToRoleAsync(int roleId, int permissionId);
    Task<bool> RemovePermissionFromRoleAsync(int roleId, int permissionId);
    Task<bool> HasPermissionAsync(int roleId, string permissionName);
    Task<int> SaveChangesAsync();
}
