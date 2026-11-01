using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;

namespace RetailBillingApp.Services;

/// <summary>
/// RPT-12: item-wise profit implementation. Aggregates invoice items per
/// product with COGS at the latest purchase rate (same convention as the P&L)
/// and ranks products by profit - top performers or low margin items.
/// </summary>
public class ItemProfitService : IItemProfitService
{
    private readonly AppDbContext _dbContext;

    public ItemProfitService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<List<ItemProfitRow>> GetItemProfitAsync(DateTime start, DateTime end, string topMode, int count)
    {
        var invoices = await _dbContext.Invoices
            .AsNoTracking()
            .Include(i => i.Items)
            .Where(i => !i.IsEstimate && i.InvoiceDate >= start.Date && i.InvoiceDate <= end.Date)
            .ToListAsync();

        // Product SKU lookup for display
        var skus = await _dbContext.Products
            .AsNoTracking()
            .Select(p => new { p.Id, p.SKU })
            .ToListAsync();
        var skuMap = skus.ToDictionary(p => p.Id, p => p.SKU ?? string.Empty);

        // COGS basis: latest purchase rate per product, ordered by purchase date
        // (and id as tie-breaker) - the last entry wins, same convention as P&L.
        var purchaseRates = await _dbContext.PurchaseItems
            .AsNoTracking()
            .Where(pi => pi.ProductId != null)
            .Select(pi => new { pi.Id, pi.ProductId, pi.Rate, PurchaseDate = pi.Purchase!.PurchaseDate })
            .OrderBy(x => x.PurchaseDate)
            .ThenBy(x => x.Id)
            .ToListAsync();
        var costMap = purchaseRates
            .GroupBy(x => x.ProductId!.Value)
            .ToDictionary(g => g.Key, g => g.Last().Rate);

        // Group items per product; ad-hoc items without a product keep their own row.
        var rows = invoices
            .SelectMany(i => i.Items.Select(it => new { It = it, i.Id }))
            .Where(x => x.It.Quantity > 0 || x.It.Amount != 0m)
            .GroupBy(x => x.It.ProductId.HasValue
                ? $"P{x.It.ProductId.Value}"
                : $"N{x.It.ProductName}")
            .Select(g =>
            {
                var productId = g.First().It.ProductId;
                var sales = g.Sum(x => x.It.Amount);
                var qty = g.Sum(x => x.It.Quantity);
                var cogs = productId.HasValue && costMap.TryGetValue(productId.Value, out var rate)
                    ? rate * qty
                    : 0m;
                return new ItemProfitRow
                {
                    ProductId = productId,
                    ProductName = g.First().It.ProductName,
                    SKU = productId.HasValue && skuMap.TryGetValue(productId.Value, out var sku) ? sku : string.Empty,
                    QuantitySold = qty,
                    Sales = Math.Round(sales, 2),
                    COGS = Math.Round(cogs, 2),
                    Profit = Math.Round(sales - cogs, 2),
                    InvoiceCount = g.Select(x => x.Id).Distinct().Count()
                };
            })
            .ToList();

        var effectiveCount = count > 0 ? count : rows.Count;
        rows = (string.Equals(topMode, "Bottom", StringComparison.OrdinalIgnoreCase)
            ? rows.OrderBy(r => r.Profit)
            : string.Equals(topMode, "Top", StringComparison.OrdinalIgnoreCase)
                ? rows.OrderByDescending(r => r.Profit)
                : rows.OrderBy(r => r.ProductName))
            .Take(effectiveCount)
            .ToList();

        return rows;
    }
}
