using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public interface IUnitService
{
    Task<List<Unit>> GetAllUnitsAsync();
    ValueTask<Unit?> GetUnitByIdAsync(int id);
    Task<bool> AddUnitAsync(Unit unit);
    Task<bool> UpdateUnitAsync(Unit unit);
    Task<bool> DeleteUnitAsync(int id);
}
