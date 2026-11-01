using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RetailBillingApp.ViewModels;

public partial class RoleViewModel : BaseViewModel
{
    private readonly IRoleService _roleService;
    private readonly IPermissionService _permissionService;
    private readonly IRolePermissionService _rolePermissionService;

    [ObservableProperty]
    private ObservableCollection<Role> _roles = new();

    [ObservableProperty]
    private Role _selectedRole = null!;

    [ObservableProperty]
    private ObservableCollection<Permission> _availablePermissions = new();

    [ObservableProperty]
    private ObservableCollection<Permission> _rolePermissions = new();

    [ObservableProperty]
    private string _newRoleName = string.Empty;

    [ObservableProperty]
    private string _newRoleDescription = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public RoleViewModel(IRoleService roleService, IPermissionService permissionService, IRolePermissionService rolePermissionService)
    {
        _roleService = roleService;
        _permissionService = permissionService;
        _rolePermissionService = rolePermissionService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var roles = await _roleService.GetAllRolesAsync();
        Roles = new ObservableCollection<Role>(roles);
        var permissions = await _permissionService.GetAllPermissionsAsync();
        AvailablePermissions = new ObservableCollection<Permission>(permissions);
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    partial void OnSelectedRoleChanged(Role value)
    {
        if (value != null)
            _ = LoadRolePermissionsAsync(value.Id);
    }

    private async System.Threading.Tasks.Task LoadRolePermissionsAsync(int roleId)
    {
        var rps = await _rolePermissionService.GetRolePermissionsAsync(roleId);
        RolePermissions = new ObservableCollection<Permission>(
            rps.Select(rp => rp.Permission!).Where(p => p != null));
    }

    [RelayCommand]
    private async Task AddRoleAsync()
    {
        if (string.IsNullOrWhiteSpace(NewRoleName))
        {
            ErrorMessage = "Role name is required.";
            return;
        }

        var role = new Role
        {
            Name = NewRoleName,
            Description = NewRoleDescription,
            IsSystem = false
        };

        var success = await _roleService.AddRoleAsync(role);
        if (success)
        {
            NewRoleName = string.Empty;
            NewRoleDescription = string.Empty;
            ErrorMessage = string.Empty;
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to add role.";
        }
    }

    [RelayCommand]
    private async Task DeleteRoleAsync()
    {
        if (SelectedRole == null) return;
        if (SelectedRole.IsSystem)
        {
            ErrorMessage = "System roles cannot be deleted.";
            return;
        }

        var success = await _roleService.DeleteRoleAsync(SelectedRole.Id);
        if (success)
        {
            SelectedRole = null!;
            RolePermissions.Clear();
            await LoadDataAsync();
        }
    }

    [RelayCommand]
    private async Task AssignPermissionAsync()
    {
        if (SelectedRole == null)
        {
            ErrorMessage = "Please select a role.";
            return;
        }

        var availableIds = AvailablePermissions.Select(p => p.Id).ToHashSet();
        var assignedIds = RolePermissions.Select(p => p.Id).ToHashSet();
        var unassigned = AvailablePermissions.Where(p => !assignedIds.Contains(p.Id)).FirstOrDefault();
        
        if (unassigned == null)
        {
            ErrorMessage = "All permissions already assigned.";
            return;
        }

        var success = await _rolePermissionService.AssignPermissionToRoleAsync(SelectedRole.Id, unassigned.Id);
        if (success)
        {
            await LoadRolePermissionsAsync(SelectedRole.Id);
        }
        else
        {
            ErrorMessage = "Failed to assign permission.";
        }
    }

    [RelayCommand]
    private async Task RemovePermissionAsync(Permission permission)
    {
        if (SelectedRole == null) return;

        var success = await _rolePermissionService.RemovePermissionFromRoleAsync(SelectedRole.Id, permission.Id);
        if (success)
        {
            await LoadRolePermissionsAsync(SelectedRole.Id);
        }
    }
}
