using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>ACC-04: expense management implementation.</summary>
public class ExpenseService : IExpenseService
{
    private static readonly string[] Defaults =
        ["Rent", "Salaries", "Electricity", "Transport", "Repairs", "Packing", "Other"];

    private readonly AppDbContext _dbContext;

    public ExpenseService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<List<Expense>> GetAllExpensesAsync(DateTime? start, DateTime? end)
    {
        var query = _dbContext.Expenses.AsNoTracking().AsQueryable();
        if (start.HasValue) query = query.Where(e => e.ExpenseDate >= start.Value.Date);
        if (end.HasValue) query = query.Where(e => e.ExpenseDate <= end.Value.Date);
        return await query.OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.Id).ToListAsync();
    }

    public async Task<(bool Success, string? Error)> AddExpenseAsync(Expense expense)
    {
        if (string.IsNullOrWhiteSpace(expense.Category))
            return (false, "Expense category is required.");
        if (expense.Amount <= 0m)
            return (false, "Expense amount must be greater than zero.");

        try
        {
            _dbContext.Expenses.Add(expense);
            await _dbContext.SaveChangesAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<bool> DeleteExpenseAsync(int id)
    {
        var expense = await _dbContext.Expenses.FindAsync(id);
        if (expense == null) return false;
        _dbContext.Expenses.Remove(expense);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<List<string>> GetCategoriesAsync()
    {
        var used = await _dbContext.Expenses.AsNoTracking()
            .Select(e => e.Category)
            .Distinct()
            .ToListAsync();
        var promoted = used.Where(u => !Defaults.Contains(u, StringComparer.OrdinalIgnoreCase)).ToList();
        var merged = used.Concat(Defaults.Where(d => !used.Contains(d, StringComparer.OrdinalIgnoreCase)));
        return merged.OrderBy(c => c).ToList();
    }

    public async Task<List<ExpenseDailyRow>> GetDailyTotalsAsync(DateTime start, DateTime end)
    {
        var rows = await _dbContext.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= start.Date && e.ExpenseDate <= end.Date)
            .ToListAsync();
        return rows.GroupBy(e => e.ExpenseDate.Date)
            .OrderBy(g => g.Key)
            .Select(g => new ExpenseDailyRow { Date = g.Key, Total = g.Sum(e => e.Amount), Count = g.Count() })
            .ToList();
    }

    public async Task<List<ExpenseMonthlyRow>> GetMonthlyTotalsAsync(DateTime start, DateTime end)
    {
        var rows = await _dbContext.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= start.Date && e.ExpenseDate <= end.Date)
            .ToListAsync();
        return rows.GroupBy(e => new { e.ExpenseDate.Year, e.ExpenseDate.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new ExpenseMonthlyRow
            {
                Month = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                Total = g.Sum(e => e.Amount),
                Count = g.Count()
            })
            .ToList();
    }

    public async Task<List<ExpenseCategoryRow>> GetCategoryBreakdownAsync(DateTime start, DateTime end)
    {
        var rows = await _dbContext.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDate >= start.Date && e.ExpenseDate <= end.Date)
            .ToListAsync();
        var total = rows.Sum(e => e.Amount);
        return rows.GroupBy(e => e.Category)
            .OrderByDescending(g => g.Sum(e => e.Amount))
            .Select(g => new ExpenseCategoryRow
            {
                Category = g.Key,
                Total = g.Sum(e => e.Amount),
                Count = g.Count(),
                SharePercent = total > 0m ? System.Math.Round(g.Sum(e => e.Amount) * 100m / total, 1) : 0m
            })
            .ToList();
    }
}
