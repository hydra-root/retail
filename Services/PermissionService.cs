using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class PermissionService : IPermissionService
{
    private readonly AppDbContext _dbContext;

    public PermissionService(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<List<Permission>> GetAllPermissionsAsync() => _dbContext.Permissions.ToListAsync();

    public ValueTask<Permission?> GetPermissionByIdAsync(int id) => _dbContext.Permissions.FindAsync(id);

    public async Task<bool> AddPermissionAsync(Permission permission)
    {
        try
        {
            _dbContext.Permissions.Add(permission);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UpdatePermissionAsync(Permission permission)
    {
        try
        {
            _dbContext.Permissions.Update(permission);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeletePermissionAsync(int id)
    {
        try
        {
            var permission = await _dbContext.Permissions.FindAsync(id);
            if (permission == null) return false;
            _dbContext.Permissions.Remove(permission);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
}
