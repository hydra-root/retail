using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class BatchService : IBatchService
{
    private readonly AppDbContext _dbContext;

    public BatchService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ProductBatch>> GetAllBatchesAsync()
    {
        return await _dbContext.ProductBatches
            .Include(b => b.Product)
            .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue) // FEFO ordering
            .ThenBy(b => b.BatchNumber)
            .ToListAsync();
    }

    public async Task<List<ProductBatch>> GetBatchesForProductAsync(int productId)
    {
        return await _dbContext.ProductBatches
            .Include(b => b.Product)
            .Where(b => b.ProductId == productId)
            .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue) // FEFO ordering
            .ThenBy(b => b.BatchNumber)
            .ToListAsync();
    }

    public async Task<int> GetAvailableQtyAsync(int batchId)
    {
        var batch = await _dbContext.ProductBatches.FindAsync(batchId);
        return batch?.Quantity ?? 0;
    }

    public async Task<bool> AddBatchAsync(ProductBatch batch)
    {
        try
        {
            _dbContext.ProductBatches.Add(batch);

            // Reflect the batch quantity on the product's total stock
            var product = await _dbContext.Products.FindAsync(batch.ProductId);
            if (product != null)
            {
                product.StockQty += batch.Quantity;
                product.UpdatedAt = DateTime.UtcNow;
            }

            _dbContext.StockTransactions.Add(new StockTransaction
            {
                ProductId = batch.ProductId,
                TransactionType = "Batch In",
                QuantityChange = batch.Quantity,
                Reference = $"Batch {batch.BatchNumber}",
                TransactionDate = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateBatchAsync(ProductBatch batch)
    {
        try
        {
            var existing = await _dbContext.ProductBatches.FindAsync(batch.Id);
            if (existing == null) return false;

            var qtyDelta = batch.Quantity - existing.Quantity;

            existing.BatchNumber = batch.BatchNumber;
            existing.ManufacturingDate = batch.ManufacturingDate;
            existing.ExpiryDate = batch.ExpiryDate;
            existing.MRP = batch.MRP;
            existing.PurchaseRate = batch.PurchaseRate;
            existing.Quantity = batch.Quantity;
            existing.Notes = batch.Notes;

            if (qtyDelta != 0)
            {
                var product = await _dbContext.Products.FindAsync(batch.ProductId);
                if (product != null)
                {
                    product.StockQty += qtyDelta;
                    product.UpdatedAt = DateTime.UtcNow;
                }

                _dbContext.StockTransactions.Add(new StockTransaction
                {
                    ProductId = batch.ProductId,
                    TransactionType = "Batch Adjust",
                    QuantityChange = qtyDelta,
                    Reference = $"Batch {existing.BatchNumber}",
                    TransactionDate = DateTime.UtcNow
                });
            }

            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteBatchAsync(int batchId)
    {
        try
        {
            var batch = await _dbContext.ProductBatches.FindAsync(batchId);
            if (batch == null) return false;

            // Removing a batch removes its qty from total product stock
            var product = await _dbContext.Products.FindAsync(batch.ProductId);
            if (product != null && batch.Quantity != 0)
            {
                product.StockQty -= batch.Quantity;
                product.UpdatedAt = DateTime.UtcNow;

                _dbContext.StockTransactions.Add(new StockTransaction
                {
                    ProductId = batch.ProductId,
                    TransactionType = "Batch Out",
                    QuantityChange = -batch.Quantity,
                    Reference = $"Batch {batch.BatchNumber} removed",
                    TransactionDate = DateTime.UtcNow
                });
            }

            _dbContext.ProductBatches.Remove(batch);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> ConsumeAsync(int batchId, int quantity)
    {
        try
        {
            var batch = await _dbContext.ProductBatches.FindAsync(batchId);
            if (batch == null || batch.Quantity < quantity) return false;

            batch.Quantity -= quantity;

            var product = await _dbContext.Products.FindAsync(batch.ProductId);
            if (product != null)
            {
                product.StockQty -= quantity;
                product.UpdatedAt = DateTime.UtcNow;
            }

            _dbContext.StockTransactions.Add(new StockTransaction
            {
                ProductId = batch.ProductId,
                TransactionType = "Batch Consume",
                QuantityChange = -quantity,
                Reference = $"Batch {batch.BatchNumber}",
                TransactionDate = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }
}
