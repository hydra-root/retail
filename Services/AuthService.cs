using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.Security.Cryptography;

namespace RetailBillingApp.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _dbContext;

    public AuthService(AppDbContext dbContext, IActivityLogService activityLog)
    {
        _dbContext = dbContext;
    }

    public int MaxLoginAttempts { get; } = 5;
    public int LockoutMinutes { get; } = 15;

    public bool IsLoggedIn => CurrentUser != null;
    public User? CurrentUser { get; private set; }
    public string? LastLoginError { get; private set; }
    public bool IsAdmin => CurrentUser?.Role == "Admin" || CurrentUser?.RoleObj?.Name == "Admin";

    public bool Login(string username, string password)
    {
        LastLoginError = null;

        var user = _dbContext.Users
            .Include(u => u.Store)
            .Include(u => u.RoleObj)
            .FirstOrDefault(u => u.Username == username);

        if (user == null || !user.IsActive)
        {
            LastLoginError = "Invalid username or password.";
            WriteLogSync(user?.Id, username, "LoginFailed", "User", username, "Unknown or inactive account");
            return false;
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            LastLoginError = $"Account locked until {user.LockedUntil.Value.ToLocalTime():HH:mm}. Try again later.";
            WriteLogSync(user.Id, username, "LoginFailed", "User", username, "Attempt while locked");
            return false;
        }

        var passwordOk = PasswordHasher.Verify(password, user.PasswordHash);
        if (!passwordOk)
        {
            user.FailedLoginAttempts++;

            if (user.FailedLoginAttempts >= MaxLoginAttempts)
            {
                user.LockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
                _dbContext.SaveChanges();
                LastLoginError = $"Too many failed attempts. Account locked for {LockoutMinutes} minutes.";
                WriteLogSync(user.Id, username, "AccountLocked", "User", username,
                    $"Locked after {MaxLoginAttempts} failed attempts until {user.LockedUntil.Value:u}");
            }
            else
            {
                _dbContext.SaveChanges();
                LastLoginError = $"Invalid username or password. {MaxLoginAttempts - user.FailedLoginAttempts} attempts remaining.";
                WriteLogSync(user.Id, username, "LoginFailed", "User", username,
                    $"Failed attempt {user.FailedLoginAttempts} of {MaxLoginAttempts}");
            }
            return false;
        }

        // SEC-04: transparently upgrade legacy SHA-256 hashes to PBKDF2
        if (PasswordHasher.IsLegacy(user.PasswordHash))
        {
            user.PasswordHash = PasswordHasher.Hash(password);
            WriteLogSync(user.Id, username, "PasswordRehash", "User", username,
                "Legacy SHA-256 hash upgraded to PBKDF2 on successful login");
        }

        // Success: reset counters
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTime.UtcNow;
        _dbContext.SaveChanges();
        CurrentUser = user;
        WriteLogSync(user.Id, username, "Login", "User", username, "Login successful");
        return true;
    }

    public void Logout()
    {
        if (CurrentUser != null)
        {
            WriteLogSync(CurrentUser.Id, CurrentUser.Username, "Logout", "User", CurrentUser.Username, "User logged out");
        }
        CurrentUser = null;
    }

    public bool SeedDefaultAdmin(string username, string password)
    {
        if (_dbContext.Users.Any(u => u.Username == username)) return false;

        _dbContext.Users.Add(new User
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
            Role = "Admin",
            IsActive = true
        });

        _dbContext.SaveChanges();
        return true;
    }

    public bool VerifyPassword(int userId, string password)
    {
        var user = _dbContext.Users.Find(userId);
        if (user == null) return false;
        if (!PasswordHasher.Verify(password, user.PasswordHash)) return false;
        if (PasswordHasher.IsLegacy(user.PasswordHash))
            user.PasswordHash = PasswordHasher.Hash(password); // transparent SEC-04 upgrade
        _dbContext.SaveChanges();
        return true;
    }

    // USR-05: Admin role always has all permissions; otherwise check role permission matrix.
    public bool HasPermission(string permissionName)
    {
        if (CurrentUser == null) return false;
        if (IsAdmin) return true;

        var roleId = CurrentUser.RoleId;
        if (!roleId.HasValue) return false;

        return _dbContext.RolePermissions
            .Include(rp => rp.Permission)
            .Any(rp => rp.RoleId == roleId.Value && rp.Permission != null && rp.Permission.Name == permissionName);
    }

    // USR-07: synchronous write so it is safe from Login()/Logout() on the UI thread.
    private void WriteLogSync(int? userId, string? username, string action,
        string? entityType, string? entityReference, string? details)
    {
        try
        {
            _dbContext.ActivityLogs.Add(new ActivityLog
            {
                UserId = userId,
                Username = username,
                StoreId = CurrentUser?.StoreId,
                Action = action,
                EntityType = entityType,
                EntityReference = entityReference,
                Details = details,
                Timestamp = DateTime.UtcNow
            });
            _dbContext.SaveChanges();
        }
        catch
        {
            // Logging must never break the main flow.
        }
    }

    // SEC-04: hashing now lives in PasswordHasher (PBKDF2 + legacy-upgrade support)
}
