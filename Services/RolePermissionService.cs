using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class RolePermissionService : IRolePermissionService
{
    private readonly AppDbContext _dbContext;

    public RolePermissionService(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<List<RolePermission>> GetRolePermissionsAsync(int roleId) =>
        _dbContext.RolePermissions
            .Include(rp => rp.Permission)
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync();

    public async Task<bool> AssignPermissionToRoleAsync(int roleId, int permissionId)
    {
        try
        {
            var exists = await _dbContext.RolePermissions
                .AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);
            if (exists) return true;

            _dbContext.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId
            });
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> RemovePermissionFromRoleAsync(int roleId, int permissionId)
    {
        try
        {
            var rp = await _dbContext.RolePermissions
                .FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);
            if (rp == null) return true;
            _dbContext.RolePermissions.Remove(rp);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> HasPermissionAsync(int roleId, string permissionName)
    {
        return await _dbContext.RolePermissions
            .Include(rp => rp.Permission)
            .AnyAsync(rp => rp.RoleId == roleId && rp.Permission != null && rp.Permission.Name == permissionName);
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
}
