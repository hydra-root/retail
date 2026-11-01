using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public interface IUserService
{
    Task<List<User>> GetAllUsersAsync();
    ValueTask<User?> GetUserByIdAsync(int id);
    Task<bool> AddUserAsync(User user, string password);
    Task<bool> UpdateUserAsync(User user);
    Task<bool> DeleteUserAsync(int id);
    Task<bool> ResetPasswordAsync(int userId, string newPassword);
    Task<bool> SetActiveAsync(int userId, bool isActive);
    Task<bool> UnlockUserAsync(int userId);
    Task<int> SaveChangesAsync();
}
