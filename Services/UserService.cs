using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _dbContext;

    public UserService(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<List<User>> GetAllUsersAsync() =>
        _dbContext.Users
            .Include(u => u.Store)
            .Include(u => u.RoleObj)
            .OrderBy(u => u.Username)
            .ToListAsync();

    public ValueTask<User?> GetUserByIdAsync(int id) => _dbContext.Users.FindAsync(id);

    public async Task<bool> AddUserAsync(User user, string password)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(password)) return false;
            user.Username = user.Username.Trim();
            user.PasswordHash = PasswordHasher.Hash(password);
            user.CreatedAt = DateTime.UtcNow;
            user.FailedLoginAttempts = 0;
            user.LockedUntil = null;
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateUserAsync(User user)
    {
        try
        {
            _dbContext.Users.Update(user);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteUserAsync(int id)
    {
        try
        {
            var user = await _dbContext.Users.FindAsync(id);
            if (user == null) return false;
            if (string.Equals(user.Username, "admin", StringComparison.OrdinalIgnoreCase)) return false;
            _dbContext.Users.Remove(user);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> ResetPasswordAsync(int userId, string newPassword)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(newPassword)) return false;
            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null) return false;
            user.PasswordHash = PasswordHasher.Hash(newPassword);
            user.FailedLoginAttempts = 0;
            user.LockedUntil = null;
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> SetActiveAsync(int userId, bool isActive)
    {
        try
        {
            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null) return false;
            if (string.Equals(user.Username, "admin", StringComparison.OrdinalIgnoreCase) && !isActive) return false;
            user.IsActive = isActive;
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UnlockUserAsync(int userId)
    {
        try
        {
            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null) return false;
            user.LockedUntil = null;
            user.FailedLoginAttempts = 0;
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
}
