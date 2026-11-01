using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface IFinancialYearService
{
    Task<List<FinancialYear>> GetAllFinancialYearsAsync();
    Task<FinancialYear?> GetFinancialYearByIdAsync(int id);
    Task<FinancialYear?> GetCurrentFinancialYearAsync();
    Task<bool> AddFinancialYearAsync(FinancialYear fy);
    Task<bool> UpdateFinancialYearAsync(FinancialYear fy);
    Task<bool> DeleteFinancialYearAsync(int id);
    Task<bool> SetActiveYearAsync(int id);
    Task<bool> LockYearAsync(int id, bool lockStatus);
    Task<bool> IsPeriodLockedAsync(DateTime date);

    /// <summary>ACC-07: create the next FY, make it active, and roll the number series over. Returns an error message on failure.</summary>
    Task<(bool Success, string? Error, string? Message)> CarryForwardAsync(int currentYearId);
    Task<int> SaveChangesAsync();
}
