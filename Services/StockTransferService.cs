using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// STK-10: stock transfer implementation. Product-level (system-wide) stock is
/// what the app tracks, so dispatching a transfer deducts Product.StockQty
/// immediately; receiving at the destination is a confirmation step that does
/// not touch the quantity (the goods were already counted once), while
/// cancellation restores it.
/// </summary>
public class StockTransferService : IStockTransferService
{
    private readonly AppDbContext _dbContext;
    private readonly IStockPolicyService _stockPolicyService;

    /// <summary>STK-03: warnings raised when the policy allows a negative-stock dispatch.</summary>
    public List<string> StockPolicyWarnings { get; } = new();

    public StockTransferService(AppDbContext dbContext, IStockPolicyService stockPolicyService)
    {
        _dbContext = dbContext;
        _stockPolicyService = stockPolicyService;
    }

    public async Task<List<StockTransfer>> GetTransfersAsync(string statusFilter = "All")
    {
        var query = _dbContext.StockTransfers.AsNoTracking();

        if (statusFilter == "InTransit")
            query = query.Where(t => t.Status == "InTransit");
        else if (statusFilter == "Received")
            query = query.Where(t => t.Status == "Received");
        else if (statusFilter == "Cancelled")
            query = query.Where(t => t.Status == "Cancelled");

        var transfers = await query
            .OrderByDescending(t => t.TransferDate)
            .ThenByDescending(t => t.Id)
            .ToListAsync();

        // Resolve store names for grid display ("From"/"To" columns).
        var storeNames = await _dbContext.Stores.AsNoTracking()
            .Where(s => transfers.Select(t => t.FromStoreId).Contains(s.Id)
                     || transfers.Select(t => t.ToStoreId).Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        foreach (var t in transfers)
        {
            t.FromStoreName = storeNames.GetValueOrDefault(t.FromStoreId, $"Store #{t.FromStoreId}");
            t.ToStoreName = storeNames.GetValueOrDefault(t.ToStoreId, $"Store #{t.ToStoreId}");
        }

        return transfers;
    }

    public async Task<(bool Success, string? Error)> CreateTransferAsync(
        int fromStoreId, int toStoreId, int productId, int quantity)
    {
        if (quantity <= 0) return (false, "Transfer quantity must be greater than zero.");
        if (fromStoreId == toStoreId) return (false, "Source and destination stores must be different.");

        StockPolicyWarnings.Clear();

        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null) return (false, "The selected product does not exist.");

        var storeIds = await _dbContext.Stores.AsNoTracking()
            .Where(s => s.Id == fromStoreId || s.Id == toStoreId)
            .ToDictionaryAsync(s => s.Id, s => s.Name);
        if (!storeIds.ContainsKey(fromStoreId) || !storeIds.ContainsKey(toStoreId))
            return (false, "Both stores must exist.");
        // STK-03: dispatching more than on hand is judged by the shared negative-stock policy
        var (allowed, policyMessage) = await _stockPolicyService.EvaluateAsync(
            product.Name, product.StockQty, -quantity, "Stock transfer");
        if (!allowed) return (false, policyMessage);
        if (policyMessage != null) StockPolicyWarnings.Add(policyMessage);

        var transfer = new StockTransfer
        {
            FromStoreId = fromStoreId,
            ToStoreId = toStoreId,
            ProductId = product.Id,
            ProductName = product.Name,
            Quantity = quantity,
            Rate = product.Rate,
            TransferValue = product.Rate * quantity,
            Status = "InTransit",
            TransferDate = DateTime.UtcNow
        };

        // Dispatch: total (system-wide) stock leaves the source immediately.
        product.StockQty -= quantity;
        product.UpdatedAt = DateTime.UtcNow;

        _dbContext.StockTransfers.Add(transfer);
        _dbContext.StockTransactions.Add(new StockTransaction
        {
            ProductId = product.Id,
            TransactionType = "Transfer",
            QuantityChange = -quantity,
            Reference = $"Stock transfer out {transfer.TransferNumber} to {storeIds[toStoreId]}",
            StoreId = fromStoreId,
            TransactionDate = DateTime.UtcNow
        });

        try
        {
            await _dbContext.SaveChangesAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string? Error)> ReceiveTransferAsync(int transferId)
    {
        var transfer = await _dbContext.StockTransfers.FirstOrDefaultAsync(t => t.Id == transferId);
        if (transfer == null) return (false, "Transfer not found.");
        if (transfer.Status != "InTransit")
            return (false, $"Only in-transit transfers can be received (current status: {transfer.Status}).");

        // NOTE on quantity: stock was deducted from Product.StockQty at dispatch
        // (CreateTransferAsync), so receipt changes nothing - the quantity is
        // already counted in the system-wide total. We still record a zero-effect
        // stock ledger row so the audit trail shows the goods arrived at the
        // destination store (ChangeAmount 0, "Transfer In").
        transfer.Status = "Received";
        transfer.ReceivedDate = DateTime.UtcNow;

        _dbContext.StockTransactions.Add(new StockTransaction
        {
            ProductId = transfer.ProductId,
            TransactionType = "Transfer In",
            QuantityChange = 0, // already deducted at dispatch; arrival is confirmation only
            Reference = $"Stock transfer in {transfer.TransferNumber} received",
            StoreId = transfer.ToStoreId,
            TransactionDate = DateTime.UtcNow
        });

        try
        {
            await _dbContext.SaveChangesAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string? Error)> CancelTransferAsync(int transferId, string username)
    {
        var transfer = await _dbContext.StockTransfers.FirstOrDefaultAsync(t => t.Id == transferId);
        if (transfer == null) return (false, "Transfer not found.");
        if (transfer.Status != "InTransit")
            return (false, $"Only in-transit transfers can be cancelled (current status: {transfer.Status}).");

        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == transfer.ProductId);
        if (product != null)
        {
            // Goods never reached (or were rejected at) the destination - give
            // the quantity back to the source product's total stock.
            product.StockQty += transfer.Quantity;
            product.UpdatedAt = DateTime.UtcNow;
        }

        transfer.Status = "Cancelled";

        _dbContext.StockTransactions.Add(new StockTransaction
        {
            ProductId = transfer.ProductId,
            TransactionType = "Transfer Cancelled",
            QuantityChange = +transfer.Quantity, // compensating row: nets out the dispatch deduction
            Reference = $"Stock transfer cancelled {transfer.TransferNumber} by {username}",
            StoreId = transfer.FromStoreId,
            TransactionDate = DateTime.UtcNow
        });

        try
        {
            await _dbContext.SaveChangesAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<List<Store>> GetStoreNamesAsync()
    {
        return await _dbContext.Stores.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<List<Product>> GetActiveProductsAsync()
    {
        return await _dbContext.Products.AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync();
    }
}
