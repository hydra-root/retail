using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;

namespace RetailBillingApp.ViewModels;

public partial class PermissionViewModel : BaseViewModel
{
    private readonly IPermissionService _permissionService;

    [ObservableProperty]
    private ObservableCollection<Permission> _permissions = new();

    [ObservableProperty]
    private Permission _selectedPermission = null!;

    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    private string _newCategory = string.Empty;

    [ObservableProperty]
    private string _newDescription = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public List<string> Categories { get; } = new()
    {
        "Invoicing", "Inventory", "Sales", "Accounting", "Reports", "Users", "Settings"
    };

    public PermissionViewModel(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var permissions = await _permissionService.GetAllPermissionsAsync();
        Permissions = new ObservableCollection<Permission>(permissions);
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task AddPermissionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName) || string.IsNullOrWhiteSpace(NewCategory))
        {
            ErrorMessage = "Name and category are required.";
            return;
        }

        var permission = new Permission
        {
            Name = NewName,
            Category = NewCategory,
            Description = NewDescription
        };

        var success = await _permissionService.AddPermissionAsync(permission);
        if (success)
        {
            NewName = string.Empty;
            NewCategory = string.Empty;
            NewDescription = string.Empty;
            ErrorMessage = string.Empty;
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to add permission.";
        }
    }

    [RelayCommand]
    private async Task DeletePermissionAsync()
    {
        if (SelectedPermission == null) return;

        var success = await _permissionService.DeletePermissionAsync(SelectedPermission.Id);
        if (success)
        {
            SelectedPermission = null!;
            await LoadDataAsync();
        }
    }
}
