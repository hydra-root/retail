using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// STK-10: stock transfer between stores. Sending stock moves it out of
/// Product.StockQty immediately and parks it as "InTransit"; the receiving
/// store's confirmation (or cancellation) closes the transfer.
/// </summary>
public interface IStockTransferService
{
    /// <summary>STK-03: warnings from a dispatch the policy allowed despite a stock shortfall.</summary>
    List<string> StockPolicyWarnings { get; }

    /// <summary>All transfers, newest first, optionally filtered by status: All/InTransit/Received/Cancelled.</summary>
    Task<List<StockTransfer>> GetTransfersAsync(string statusFilter = "All");

    /// <summary>
    /// Deduct quantity from Product.StockQty, create an InTransit transfer and a
    /// "Transfer" stock ledger row. Returns an error message on failure.
    /// </summary>
    Task<(bool Success, string? Error)> CreateTransferAsync(int fromStoreId, int toStoreId, int productId, int quantity);

    /// <summary>
    /// Receive an in-transit transfer at the destination store (marks Received;
    /// stock was already deducted at dispatch so no quantity change).
    /// Returns an error message on failure.
    /// </summary>
    Task<(bool Success, string? Error)> ReceiveTransferAsync(int transferId);

    /// <summary>
    /// Cancel an in-transit transfer: restore the source product's StockQty,
    /// mark Cancelled and write a compensating ledger row.
    /// Returns an error message on failure.
    /// </summary>
    Task<(bool Success, string? Error)> CancelTransferAsync(int transferId, string username);

    /// <summary>Active stores for the From/To pickers.</summary>
    Task<List<Store>> GetStoreNamesAsync();

    /// <summary>All products (Id/Name) for the product picker.</summary>
    Task<List<Product>> GetActiveProductsAsync();
}
