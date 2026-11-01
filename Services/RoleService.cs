using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class RoleService : IRoleService
{
    private readonly AppDbContext _dbContext;

    public RoleService(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<List<Role>> GetAllRolesAsync() => _dbContext.Roles.ToListAsync();

    public ValueTask<Role?> GetRoleByIdAsync(int id) => _dbContext.Roles.FindAsync(id);

    public async Task<bool> AddRoleAsync(Role role)
    {
        try
        {
            _dbContext.Roles.Add(role);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateRoleAsync(Role role)
    {
        try
        {
            _dbContext.Roles.Update(role);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteRoleAsync(int id)
    {
        try
        {
            var role = await _dbContext.Roles.FindAsync(id);
            if (role == null) return false;
            if (role.IsSystem) return false;
            _dbContext.Roles.Remove(role);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
}
