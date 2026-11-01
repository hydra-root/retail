using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface ISupplierService
{
    Task<List<Supplier>> GetAllSuppliersAsync();
    ValueTask<Supplier?> GetSupplierByIdAsync(int id);
    Task<bool> AddSupplierAsync(Supplier supplier);
    Task<bool> UpdateSupplierAsync(Supplier supplier);
    Task<bool> DeleteSupplierAsync(int id);
    Task<int> SaveChangesAsync();
}
