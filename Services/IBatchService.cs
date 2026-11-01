using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public interface IBatchService
{
    Task<List<ProductBatch>> GetAllBatchesAsync();
    Task<List<ProductBatch>> GetBatchesForProductAsync(int productId);
    Task<int> GetAvailableQtyAsync(int batchId);
    Task<bool> AddBatchAsync(ProductBatch batch);
    Task<bool> UpdateBatchAsync(ProductBatch batch);
    Task<bool> DeleteBatchAsync(int batchId);
    Task<bool> ConsumeAsync(int batchId, int quantity);
}
