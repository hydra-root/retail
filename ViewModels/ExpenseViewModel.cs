using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// ACC-04: expense management - add/delete expenses, daily/monthly totals,
/// category breakdown with shares.
/// </summary>
public partial class ExpenseViewModel : BaseViewModel
{
    private readonly IExpenseService _expenseService;

    [ObservableProperty]
    private ObservableCollection<Expense> _expenses = new();

    [ObservableProperty]
    private ObservableCollection<ExpenseCategoryRow> _categoryRows = new();

    [ObservableProperty]
    private ObservableCollection<ExpenseDailyRow> _dailyRows = new();

    [ObservableProperty]
    private ObservableCollection<ExpenseMonthlyRow> _monthlyRows = new();

    [ObservableProperty]
    private Expense? _selectedExpense;

    // New expense form
    [ObservableProperty]
    private string _newCategory = "Other";

    [ObservableProperty]
    private string _newAmountText = string.Empty;

    [ObservableProperty]
    private DateTime _newExpenseDate = DateTime.Today;

    [ObservableProperty]
    private string _newPaymentMode = "Cash";

    [ObservableProperty]
    private string _newNotes = string.Empty;

    [ObservableProperty]
    private DateTime _startDate = DateTime.Today.AddDays(-30);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private decimal _rangeTotal;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public List<string> PaymentModes { get; } = ["Cash", "UPI", "Card", "Bank Transfer", "Cheque"];

    public ExpenseViewModel(IExpenseService expenseService)
    {
        _expenseService = expenseService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync() => await ApplyFilterAsync();

    private async System.Threading.Tasks.Task ApplyFilterAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _expenseService.GetAllExpensesAsync(StartDate, EndDate);
            Expenses = new ObservableCollection<Expense>(list);
            RangeTotal = list.Sum(e => e.Amount);
            CategoryRows = new ObservableCollection<ExpenseCategoryRow>(
                await _expenseService.GetCategoryBreakdownAsync(StartDate, EndDate));
            DailyRows = new ObservableCollection<ExpenseDailyRow>(
                await _expenseService.GetDailyTotalsAsync(StartDate, EndDate));
            MonthlyRows = new ObservableCollection<ExpenseMonthlyRow>(
                await _expenseService.GetMonthlyTotalsAsync(StartDate, EndDate));
            NewCategories = new List<string>(await _expenseService.GetCategoriesAsync());
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [ObservableProperty]
    private List<string> _newCategories = ["Rent", "Salaries", "Electricity", "Transport", "Repairs", "Packing", "Other"];

    [RelayCommand]
    private async Task FilterAsync() => await ApplyFilterAsync();

    /// <summary>ACC-04: record a new expense.</summary>
    [RelayCommand]
    private async Task AddExpenseAsync()
    {
        if (!decimal.TryParse(NewAmountText, System.Globalization.CultureInfo.InvariantCulture, out var amount))
        {
            ErrorMessage = "Enter a valid amount.";
            return;
        }

        var (ok, error) = await _expenseService.AddExpenseAsync(new Expense
        {
            Category = NewCategory.Trim(),
            Amount = amount,
            ExpenseDate = NewExpenseDate,
            PaymentMode = NewPaymentMode,
            Notes = string.IsNullOrWhiteSpace(NewNotes) ? null : NewNotes.Trim()
        });

        if (ok)
        {
            SuccessMessage = $"Expense of {amount:C} recorded under \"{NewCategory}\".";
            ErrorMessage = string.Empty;
            NewAmountText = string.Empty;
            NewNotes = string.Empty;
            await ApplyFilterAsync();
        }
        else
        {
            ErrorMessage = error ?? "Failed to record the expense.";
        }
    }

    /// <summary>ACC-04: delete the selected expense.</summary>
    [RelayCommand]
    private async Task DeleteExpenseAsync()
    {
        if (SelectedExpense == null)
        {
            ErrorMessage = "Select an expense to delete.";
            return;
        }

        if (await _expenseService.DeleteExpenseAsync(SelectedExpense.Id))
        {
            SuccessMessage = "Expense deleted.";
            ErrorMessage = string.Empty;
            await ApplyFilterAsync();
        }
    }

    /// <summary>ACC-04: export the filtered expenses + summaries to CSV on Desktop.</summary>
    [RelayCommand]
    private void ExportCsv()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"Expenses_{StartDate:yyyyMMdd}_{EndDate:yyyyMMdd}.csv");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Expenses,{StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}");
            sb.AppendLine($"Range Total,{RangeTotal:F2}");
            sb.AppendLine();
            sb.AppendLine("Date,Category,Payment Mode,Amount,Notes");
            foreach (var e in Expenses)
                sb.AppendLine($"{e.ExpenseDate:yyyy-MM-dd},{e.Category},{e.PaymentMode},{e.Amount:F2},\"{(e.Notes ?? string.Empty).Replace("\"", "\"\"")}\"");
            sb.AppendLine();
            sb.AppendLine("Category Breakdown");
            sb.AppendLine("Category,Total,Count,Share %");
            foreach (var c in CategoryRows)
                sb.AppendLine($"{c.Category},{c.Total:F2},{c.Count},{c.SharePercent}");
            sb.AppendLine();
            sb.AppendLine("Monthly Totals");
            sb.AppendLine("Month,Total,Count");
            foreach (var m in MonthlyRows)
                sb.AppendLine($"{m.Month},{m.Total:F2},{m.Count}");

            File.WriteAllText(path, sb.ToString());
            SuccessMessage = $"Exported to {path}";
        }
        catch (Exception ex)
        {
            SuccessMessage = $"Export failed: {ex.Message}";
        }
    }
}
