using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// STK-14: physical stock verification.
/// </summary>
public interface IStockTakeService
{
    /// <summary>STK-03: reason the most recent completion was refused.</summary>
    string? LastValidationError { get; }

    Task<List<StockTake>> GetAllStockTakesAsync();

    Task<StockTake?> GetStockTakeWithItemsAsync(int id);

    Task<int> CreateStockTakeAsync(string? notes, int? storeId);

    Task<bool> RecordCountAsync(int stockTakeId, int productId, int countedQty);

    Task<(bool Success, int AdjustedCount, decimal TotalVarianceValue)> CompleteStockTakeAsync(
        int stockTakeId, string username);
}
