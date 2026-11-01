using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class UnitService : IUnitService
{
    private readonly AppDbContext _dbContext;

    public UnitService(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<List<Unit>> GetAllUnitsAsync() =>
        _dbContext.Units
            .Include(u => u.BaseUnit)
            .OrderBy(u => u.Name)
            .ToListAsync();

    public ValueTask<Unit?> GetUnitByIdAsync(int id) => _dbContext.Units.FindAsync(id);

    public async Task<bool> AddUnitAsync(Unit unit)
    {
        try
        {
            if (await _dbContext.Units.AnyAsync(u => u.Name == unit.Name || u.Symbol == unit.Symbol))
                return false; // duplicate name or symbol

            if (!unit.IsBaseUnit && unit.BaseUnitId != null)
            {
                var baseUnit = await _dbContext.Units.FindAsync(unit.BaseUnitId);
                if (baseUnit == null) return false;
                unit.BaseUnit = baseUnit;
            }
            else
            {
                unit.BaseUnitId = null;
                unit.Factor = 1;
            }

            _dbContext.Units.Add(unit);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateUnitAsync(Unit unit)
    {
        try
        {
            var existing = await _dbContext.Units.FindAsync(unit.Id);
            if (existing == null) return false;

            if (await _dbContext.Units.AnyAsync(u => (u.Name == unit.Name || u.Symbol == unit.Symbol) && u.Id != unit.Id))
                return false;

            if (unit.BaseUnitId == unit.Id)
                return false; // cannot be its own base

            existing.Name = unit.Name;
            existing.Symbol = unit.Symbol;
            existing.BaseUnitId = unit.BaseUnitId;
            existing.Factor = unit.BaseUnitId == null ? 1 : unit.Factor;

            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteUnitAsync(int id)
    {
        try
        {
            var unit = await _dbContext.Units.FindAsync(id);
            if (unit == null) return false;

            // Block deletion when in use by products or as another unit's base
            var inUseByProduct = await _dbContext.Products.AnyAsync(p => p.UnitId == id);
            var isBaseOfOthers = await _dbContext.Units.AnyAsync(u => u.BaseUnitId == id);
            if (inUseByProduct || isBaseOfOthers)
                return false;

            _dbContext.Units.Remove(unit);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }
}
