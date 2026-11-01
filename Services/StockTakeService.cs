using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// STK-14: physical stock verification. Creating a take snapshots every
/// product's system quantity; completing it posts each variance against
/// Product.StockQty and writes a "Stocktake" StockTransaction row.
/// </summary>
public class StockTakeService : IStockTakeService
{
    private readonly AppDbContext _dbContext;
    private readonly IStockPolicyService _stockPolicyService;

    /// <summary>STK-03: why a completion was refused (negative-stock policy in Block mode).</summary>
    public string? LastValidationError { get; private set; }

    public StockTakeService(AppDbContext dbContext, IStockPolicyService stockPolicyService)
    {
        _dbContext = dbContext;
        _stockPolicyService = stockPolicyService;
    }

    public async Task<List<StockTake>> GetAllStockTakesAsync()
    {
        return await _dbContext.StockTakes.AsNoTracking()
            .OrderByDescending(t => t.StockTakeDate)
            .ThenByDescending(t => t.Id)
            .ToListAsync();
    }

    public async Task<StockTake?> GetStockTakeWithItemsAsync(int id)
    {
        return await _dbContext.StockTakes
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<int> CreateStockTakeAsync(string? notes, int? storeId)
    {
        // Snapshot all active products: name + current system quantity
        var products = await _dbContext.Products.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.StockQty })
            .ToListAsync();

        var take = new StockTake
        {
            StockTakeNumber = $"STK-{DateTime.Now:yyyyMMdd-HHmmss}",
            StockTakeDate = DateTime.UtcNow,
            StoreId = storeId,
            Status = "Open",
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedAt = DateTime.UtcNow,
            Items = products.Select(p => new StockTakeItem
            {
                ProductId = p.Id,
                ProductName = p.Name,
                SystemQty = p.StockQty,
                CountedQty = null
            }).ToList()
        };

        _dbContext.StockTakes.Add(take);
        await _dbContext.SaveChangesAsync();

        return take.Id;
    }

    public async Task<bool> RecordCountAsync(int stockTakeId, int productId, int countedQty)
    {
        var item = await _dbContext.StockTakeItems
            .FirstOrDefaultAsync(i => i.StockTakeId == stockTakeId && i.ProductId == productId);

        if (item == null) return false;

        item.CountedQty = countedQty;
        item.Saved = true;
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<(bool Success, int AdjustedCount, decimal TotalVarianceValue)> CompleteStockTakeAsync(
        int stockTakeId, string username)
    {
        var take = await GetStockTakeWithItemsAsync(stockTakeId);
        LastValidationError = null;
        if (take == null) return (false, 0, 0m);
        if (take.Status == "Completed") return (false, 0, 0m);

        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            var adjustedCount = 0;
            var totalVarianceValue = 0m;
            var now = DateTime.UtcNow;

            // STK-03: judge every negative variance against the policy before anything is posted,
            // so a blocked completion leaves the stock and the ledger untouched.
            var blocked = new List<string>();
            foreach (var item in take.Items.Where(i => i.CountedQty.HasValue && i.Variance < 0))
            {
                var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
                if (product == null) continue;

                var (allowed, message) = await _stockPolicyService.EvaluateAsync(
                    product.Name, product.StockQty, item.Variance, "Stocktake variance");
                if (!allowed) blocked.Add(message!);
            }
            if (blocked.Count > 0)
            {
                LastValidationError = "Count would create negative stock: " + string.Join(" ", blocked) +
                    " Correct the counted quantities or relax the policy in Stock Policy.";
                return (false, 0, 0m);
            }

            foreach (var item in take.Items.Where(i => i.CountedQty.HasValue && i.Variance != 0))
            {
                var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
                if (product == null) continue;

                var variance = item.Variance;

                // Adjust the product stock (binding to the tracked entity)
                product.StockQty += variance;
                product.UpdatedAt = now;

                _dbContext.StockTransactions.Add(new StockTransaction
                {
                    ProductId = item.ProductId,
                    TransactionType = "Stocktake",
                    QuantityChange = variance,
                    Reference = $"Physical count by {username}",
                    TransactionDate = now,
                    StoreId = take.StoreId
                });

                item.VarianceValue = variance * product.Rate;
                item.Saved = true;

                adjustedCount++;
                totalVarianceValue += item.VarianceValue;
            }

            take.Status = "Completed";

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return (true, adjustedCount, totalVarianceValue);
        }
        catch
        {
            return (false, 0, 0m);
        }
    }
}
