using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface IProductService
{
    Task<List<Product>> GetAllProductsAsync();
    ValueTask<Product?> GetProductByIdAsync(int id);
    Task<Product?> GetProductBySkuAsync(string sku);
    Task<List<Product>> GetLowStockProductsAsync();
    Task<List<Product>> GetExpiredProductsAsync();
    Task<bool> AddProductAsync(Product product);
    Task<bool> UpdateProductAsync(Product product);
    Task<bool> DeleteProductAsync(int id);
    Task<bool> AdjustStockAsync(int productId, decimal quantityChange, string reason);

    /// <summary>STK-07: recent stock adjustments (newest first).</summary>
    Task<List<AdjustmentHistoryRow>> GetAdjustmentHistoryAsync(int count = 100);

    /// <summary>Reason of the most recent stock-adjustment failure (STK-07).</summary>
    string? LastValidationError { get; }

    /// <summary>STK-03: non-blocking negative-stock warnings from the last adjustment.</summary>
    List<string> StockPolicyWarnings { get; }

    Task<int> SaveChangesAsync();
}
