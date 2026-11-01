using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using RetailBillingApp.Views;
using System.Collections.ObjectModel;
using System.Windows;

namespace RetailBillingApp.ViewModels;

/// <summary>USR-04: user management with lockout support and USR-06 passcode-protected actions.</summary>
public partial class UsersViewModel : BaseViewModel
{
    private readonly IUserService _userService;
    private readonly IRoleService _roleService;
    private readonly IStoreService _storeService;
    private readonly IAuthService _authService;
    private readonly IActivityLogService _activityLogService;

    [ObservableProperty]
    private ObservableCollection<User> _users = new();

    [ObservableProperty]
    private User? _selectedUser;

    [ObservableProperty]
    private ObservableCollection<Role> _roles = new();

    [ObservableProperty]
    private ObservableCollection<Store> _stores = new();

    [ObservableProperty]
    private string _newUsername = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private Role? _newUserRole;

    [ObservableProperty]
    private Store? _newUserStore;

    [ObservableProperty]
    private bool _newUserIsActive = true;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public UsersViewModel(IUserService userService, IRoleService roleService, IStoreService storeService,
        IAuthService authService, IActivityLogService activityLogService)
    {
        _userService = userService;
        _roleService = roleService;
        _storeService = storeService;
        _authService = authService;
        _activityLogService = activityLogService;
    }

    public async Task LoadDataAsync()
    {
        IsLoading = true;
        var users = await _userService.GetAllUsersAsync();
        Users = new ObservableCollection<User>(users);
        var roles = await _roleService.GetAllRolesAsync();
        Roles = new ObservableCollection<Role>(roles);
        Stores = new ObservableCollection<Store>(await _storeService.GetAllStoresAsync());
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();

    private bool ConfirmWithPasscode(string action)
    {
        // USR-06: sensitive user actions require re-entering the current user's password.
        var currentUser = _authService.CurrentUser;
        if (currentUser == null) return false;

        var result = PasscodeDialog.Show(
            "Passcode Required",
            $"Re-enter your password to {action} ({currentUser.Username}):",
            passcode => _authService.VerifyPassword(currentUser.Id, passcode)
                ? string.Empty
                : "Incorrect passcode. Please try again.");

        return result != null;
    }

    [RelayCommand]
    private async Task AddUserAsync()
    {
        if (string.IsNullOrWhiteSpace(NewUsername))
        {
            ErrorMessage = "Username is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 6)
        {
            ErrorMessage = "Password must be at least 6 characters.";
            return;
        }

        // Map selected role to legacy Role string column for HasPermission/IsAdmin behaviour
        var roleString = NewUserRole?.Name ?? "User";

        var user = new User
        {
            Username = NewUsername.Trim(),
            Role = roleString,
            RoleId = NewUserRole?.Id,
            StoreId = NewUserStore?.Id,
            IsActive = NewUserIsActive
        };

        var success = await _userService.AddUserAsync(user, NewPassword);
        if (success)
        {
            ErrorMessage = string.Empty;
            SuccessMessage = $"User '{user.Username}' created.";
            NewUsername = string.Empty;
            NewPassword = string.Empty;
            NewUserRole = null;
            NewUserStore = null;

            await _activityLogService.LogAsync(_authService.CurrentUser?.Id, _authService.CurrentUser?.Username,
                user.StoreId, "UserCreated", "User", user.Username, $"Created user '{user.Username}' with role '{roleString}'");
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = $"Failed to add user '{user.Username}' (may already exist).";
            SuccessMessage = string.Empty;
        }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync()
    {
        if (SelectedUser == null) return;

        if (string.Equals(SelectedUser.Username, "admin", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "The default admin account cannot be deactivated.";
            return;
        }

        if (!ConfirmWithPasscode(SelectedUser.IsActive ? "deactivate a user" : "activate a user"))
        {
            ErrorMessage = "Action cancelled: passcode required.";
            return;
        }

        var success = await _userService.SetActiveAsync(SelectedUser.Id, !SelectedUser.IsActive);
        if (success)
        {
            ErrorMessage = string.Empty;
            SuccessMessage = $"User '{SelectedUser.Username}' {(SelectedUser.IsActive ? "deactivated" : "activated")}.";
            await _activityLogService.LogAsync(_authService.CurrentUser?.Id, _authService.CurrentUser?.Username,
                SelectedUser.StoreId, "UserActiveToggled", "User", SelectedUser.Username,
                $"Set active={(!SelectedUser.IsActive)}");
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to change user state.";
        }
    }

    [RelayCommand]
    private async Task UnlockUserAsync()
    {
        if (SelectedUser == null) return;

        if (SelectedUser.LockedUntil == null)
        {
            ErrorMessage = "Selected user is not locked.";
            return;
        }

        if (!ConfirmWithPasscode("unlock a user"))
        {
            ErrorMessage = "Action cancelled: passcode required.";
            return;
        }

        var success = await _userService.UnlockUserAsync(SelectedUser.Id);
        if (success)
        {
            ErrorMessage = string.Empty;
            SuccessMessage = $"User '{SelectedUser.Username}' unlocked.";
            await _activityLogService.LogAsync(_authService.CurrentUser?.Id, _authService.CurrentUser?.Username,
                SelectedUser.StoreId, "UserUnlocked", "User", SelectedUser.Username,
                $"Unlocked after {SelectedUser.FailedLoginAttempts} failed attempts");
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to unlock user.";
        }
    }

    [RelayCommand]
    private async Task ResetPasswordAsync()
    {
        if (SelectedUser == null)
        {
            ErrorMessage = "Select a user to reset password.";
            return;
        }

        if (!ConfirmWithPasscode("reset a password"))
        {
            ErrorMessage = "Action cancelled: passcode required.";
            return;
        }

        // Now prompt for the new password (validated for length)
        var newPassword = PasscodeDialog.Show(
            "New Password",
            $"Enter the new password for '{SelectedUser.Username}':",
            passcode => passcode.Length < 6 ? "Password must be at least 6 characters." : string.Empty);

        if (newPassword == null) return;

        var success = await _userService.ResetPasswordAsync(SelectedUser.Id, newPassword);
        if (success)
        {
            ErrorMessage = string.Empty;
            SuccessMessage = $"Password reset for '{SelectedUser.Username}'.";
            await _activityLogService.LogAsync(_authService.CurrentUser?.Id, _authService.CurrentUser?.Username,
                SelectedUser.StoreId, "PasswordReset", "User", SelectedUser.Username,
                $"Password reset by {_authService.CurrentUser?.Username}");
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to reset password.";
        }
    }

    [RelayCommand]
    private async Task DeleteUserAsync()
    {
        if (SelectedUser == null) return;

        if (string.Equals(SelectedUser.Username, "admin", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "The default admin account cannot be deleted.";
            return;
        }

        if (MessageBox.Show($"Delete user '{SelectedUser.Username}'? This cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (!ConfirmWithPasscode("delete a user"))
        {
            ErrorMessage = "Action cancelled: passcode required.";
            return;
        }

        var success = await _userService.DeleteUserAsync(SelectedUser.Id);
        if (success)
        {
            ErrorMessage = string.Empty;
            SuccessMessage = $"User '{SelectedUser.Username}' deleted.";
            await _activityLogService.LogAsync(_authService.CurrentUser?.Id, _authService.CurrentUser?.Username,
                SelectedUser.StoreId, "UserDeleted", "User", SelectedUser.Username,
                $"Deleted user '{SelectedUser.Username}'");
            SelectedUser = null;
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to delete user.";
        }
    }
}
