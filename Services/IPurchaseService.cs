using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface IPurchaseService
{
    Task<List<Purchase>> GetAllPurchasesAsync();
    Task<Purchase?> GetPurchaseByIdAsync(int id);
    Task<string> GenerateNextPurchaseNumberAsync();
    Task<bool> CreatePurchaseAsync(Purchase purchase);
    Task<bool> UpdatePurchaseAsync(Purchase purchase);
    Task<bool> DeletePurchaseAsync(int id);
    Task<int> SaveChangesAsync();
}
