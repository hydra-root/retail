using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public interface IStoreService
{
    Task<List<Store>> GetAllStoresAsync(bool activeOnly = false);
    ValueTask<Store?> GetStoreByIdAsync(int id);
    Task<bool> AddStoreAsync(Store store);
    Task<bool> UpdateStoreAsync(Store store);
    Task<bool> DeleteStoreAsync(int id);
    Task<bool> AddStoresAsync(IEnumerable<Store> stores);
    Task<int> CountStoresAsync();
    // USR-02 Central dashboard
    Task<List<StoreSalesSummary>> GetStoreSalesSummaryAsync(DateTime? from = null, DateTime? to = null);
    // USR-03 Store-wise stock
    Task<List<StoreStockSummary>> GetStoreStockAsync(int storeId);
    Task<int> SaveChangesAsync();
}
