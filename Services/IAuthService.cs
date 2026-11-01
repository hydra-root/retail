using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public interface IAuthService
{
    bool IsLoggedIn { get; }
    User? CurrentUser { get; }
    bool Login(string username, string password);
    void Logout();
    bool IsAdmin { get; }
    bool SeedDefaultAdmin(string username, string password);

    /// <summary>Reason of the most recent failed Login() call (USR-04).</summary>
    string? LastLoginError { get; }

    /// <summary>USR-04: lock account for a while after repeated failed attempts.</summary>
    int MaxLoginAttempts { get; }
    int LockoutMinutes { get; }

    /// <summary>True when the given password matches the given user's password.</summary>
    bool VerifyPassword(int userId, string password);

    /// <summary>USR-05: permission check via role permissions; Admin always allowed.</summary>
    bool HasPermission(string permissionName);
}
