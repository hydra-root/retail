using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class SupplierService : ISupplierService
{
    private readonly AppDbContext _dbContext;

    public SupplierService(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<List<Supplier>> GetAllSuppliersAsync() => _dbContext.Suppliers.ToListAsync();

    public ValueTask<Supplier?> GetSupplierByIdAsync(int id) => _dbContext.Suppliers.FindAsync(id);

    public async Task<bool> AddSupplierAsync(Supplier supplier)
    {
        try
        {
            supplier.CreatedAt = System.DateTime.UtcNow;
            _dbContext.Suppliers.Add(supplier);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateSupplierAsync(Supplier supplier)
    {
        try
        {
            _dbContext.Suppliers.Update(supplier);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteSupplierAsync(int id)
    {
        try
        {
            var supplier = await _dbContext.Suppliers.FindAsync(id);
            if (supplier == null) return false;
            _dbContext.Suppliers.Remove(supplier);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
}
