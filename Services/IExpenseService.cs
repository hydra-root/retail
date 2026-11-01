using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// ACC-04: expense management - categorised expenses with daily/monthly totals.
/// P&amp;L and Balance Sheet consume the same Expense rows.
/// </summary>
public interface IExpenseService
{
    /// <summary>Expenses in a date range, newest first. Null range = all.</summary>
    Task<List<Expense>> GetAllExpensesAsync(DateTime? start, DateTime? end);

    /// <summary>Add one expense. Returns an error message on failure.</summary>
    Task<(bool Success, string? Error)> AddExpenseAsync(Expense expense);

    Task<bool> DeleteExpenseAsync(int id);

    /// <summary>Distinct expense categories currently in use (for the picker).</summary>
    Task<List<string>> GetCategoriesAsync();

    /// <summary>Daily totals within the range.</summary>
    Task<List<ExpenseDailyRow>> GetDailyTotalsAsync(DateTime start, DateTime end);

    /// <summary>Monthly totals within the range.</summary>
    Task<List<ExpenseMonthlyRow>> GetMonthlyTotalsAsync(DateTime start, DateTime end);

    /// <summary>Category breakdown with share of the range total.</summary>
    Task<List<ExpenseCategoryRow>> GetCategoryBreakdownAsync(DateTime start, DateTime end);
}

/// <summary>ACC-04: one day's expense total.</summary>
public class ExpenseDailyRow
{
    public DateTime Date { get; set; }
    public decimal Total { get; set; }
    public int Count { get; set; }
}

/// <summary>ACC-04: one month's expense total.</summary>
public class ExpenseMonthlyRow
{
    public string Month { get; set; } = string.Empty; // e.g. "Oct 2026"
    public decimal Total { get; set; }
    public int Count { get; set; }
}

/// <summary>ACC-04: category breakdown with share.</summary>
public class ExpenseCategoryRow
{
    public string Category { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int Count { get; set; }

    /// <summary>Share of the range's total expenses, 0-100.</summary>
    public decimal SharePercent { get; set; }
}
