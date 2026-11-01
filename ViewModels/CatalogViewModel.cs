using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// STK-02: Manage product categories (tree) and units of measure (with conversion).
/// </summary>
public partial class CatalogViewModel : BaseViewModel
{
    private readonly ICategoryService _categoryService;
    private readonly IUnitService _unitService;

    // ---- Categories ----
    [ObservableProperty]
    private ObservableCollection<Category> _categories = new();

    [ObservableProperty]
    private Category _selectedCategory = null!;

    [ObservableProperty]
    private string _newCategoryName = string.Empty;

    [ObservableProperty]
    private string _newCategoryDescription = string.Empty;

    [ObservableProperty]
    private Category _newParentCategory = null!;

    // ---- Units ----
    [ObservableProperty]
    private ObservableCollection<Unit> _units = new();

    [ObservableProperty]
    private Unit _selectedUnit = null!;

    [ObservableProperty]
    private string _newUnitName = string.Empty;

    [ObservableProperty]
    private string _newUnitSymbol = string.Empty;

    [ObservableProperty]
    private Unit _newBaseUnit = null!;

    [ObservableProperty]
    private decimal _newUnitFactor = 1;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    public CatalogViewModel(ICategoryService categoryService, IUnitService unitService)
    {
        _categoryService = categoryService;
        _unitService = unitService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        var categories = await _categoryService.GetAllCategoriesAsync();
        Categories = new ObservableCollection<Category>(categories);

        var units = await _unitService.GetAllUnitsAsync();
        Units = new ObservableCollection<Unit>(units);
    }

    // ---- Category commands ----

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        ShowMessage(string.Empty, string.Empty);

        if (string.IsNullOrWhiteSpace(NewCategoryName))
        {
            ShowMessage("Category name is required.", null);
            return;
        }

        var category = new Category
        {
            Name = NewCategoryName.Trim(),
            Description = string.IsNullOrWhiteSpace(NewCategoryDescription) ? null : NewCategoryDescription.Trim(),
            ParentCategoryId = NewParentCategory?.Id
        };

        var ok = await _categoryService.AddCategoryAsync(category);
        if (ok)
        {
            ShowMessage(null, $"Category '{category.Name}' added.");
            NewCategoryName = string.Empty;
            NewCategoryDescription = string.Empty;
            NewParentCategory = null!;
            await LoadDataAsync();
        }
        else
        {
            ShowMessage($"Could not add category (name may already exist).", null);
        }
    }

    [RelayCommand]
    private async Task DeleteCategoryAsync()
    {
        ShowMessage(string.Empty, string.Empty);

        if (SelectedCategory == null)
        {
            ShowMessage("Select a category to delete.", null);
            return;
        }

        var ok = await _categoryService.DeleteCategoryAsync(SelectedCategory.Id);
        if (ok)
        {
            ShowMessage(null, $"Category '{SelectedCategory.Name}' deleted.");
            await LoadDataAsync();
        }
        else
        {
            ShowMessage("Cannot delete: category is used by products or has child categories.", null);
        }
    }

    // ---- Unit commands ----

    [RelayCommand]
    private async Task AddUnitAsync()
    {
        ShowMessage(string.Empty, string.Empty);

        if (string.IsNullOrWhiteSpace(NewUnitName) || string.IsNullOrWhiteSpace(NewUnitSymbol))
        {
            ShowMessage("Unit name and symbol are required.", null);
            return;
        }

        var unit = new Unit
        {
            Name = NewUnitName.Trim(),
            Symbol = NewUnitSymbol.Trim(),
            BaseUnitId = NewBaseUnit?.Id,
            Factor = NewBaseUnit == null ? 1 : NewUnitFactor
        };

        var ok = await _unitService.AddUnitAsync(unit);
        if (ok)
        {
            ShowMessage(null, $"Unit '{unit.Name}' added. {unit.ConversionDisplay}");
            NewUnitName = string.Empty;
            NewUnitSymbol = string.Empty;
            NewBaseUnit = null!;
            NewUnitFactor = 1;
            await LoadDataAsync();
        }
        else
        {
            ShowMessage("Could not add unit (name/symbol may already exist).", null);
        }
    }

    [RelayCommand]
    private async Task DeleteUnitAsync()
    {
        ShowMessage(string.Empty, string.Empty);

        if (SelectedUnit == null)
        {
            ShowMessage("Select a unit to delete.", null);
            return;
        }

        var ok = await _unitService.DeleteUnitAsync(SelectedUnit.Id);
        if (ok)
        {
            ShowMessage(null, $"Unit '{SelectedUnit.Name}' deleted.");
            await LoadDataAsync();
        }
        else
        {
            ShowMessage("Cannot delete: unit is used by products or is another unit's base.", null);
        }
    }

    private void ShowMessage(string? error, string? success)
    {
        ErrorMessage = error ?? string.Empty;
        SuccessMessage = success ?? string.Empty;
    }
}
