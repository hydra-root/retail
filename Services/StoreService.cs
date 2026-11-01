using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class StoreService : IStoreService
{
    private readonly AppDbContext _dbContext;

    public StoreService(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<List<Store>> GetAllStoresAsync(bool activeOnly = false) =>
        _dbContext.Stores
            .Where(s => !activeOnly || s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync();

    public ValueTask<Store?> GetStoreByIdAsync(int id) => _dbContext.Stores.FindAsync(id);

    public async Task<bool> AddStoreAsync(Store store)
    {
        try
        {
            store.CreatedAt = DateTime.UtcNow;
            _dbContext.Stores.Add(store);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> AddStoresAsync(IEnumerable<Store> stores)
    {
        try
        {
            foreach (var store in stores)
                store.CreatedAt = DateTime.UtcNow;
            _dbContext.Stores.AddRange(stores);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateStoreAsync(Store store)
    {
        try
        {
            _dbContext.Stores.Update(store);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteStoreAsync(int id)
    {
        try
        {
            var store = await _dbContext.Stores.FindAsync(id);
            if (store == null) return false;
            _dbContext.Stores.Remove(store);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public Task<int> CountStoresAsync() => _dbContext.Stores.CountAsync();

    // USR-02: store-wise sales from invoices
    public async Task<List<StoreSalesSummary>> GetStoreSalesSummaryAsync(DateTime? from = null, DateTime? to = null)
    {
        var query = _dbContext.Invoices.AsNoTracking().AsQueryable();
        if (from.HasValue) query = query.Where(i => i.InvoiceDate >= from.Value);
        if (to.HasValue) query = query.Where(i => i.InvoiceDate < to.Value.AddDays(1));

        var rows = await query
            .GroupBy(i => i.StoreId)
            .Select(g => new
            {
                StoreId = g.Key,
                InvoiceCount = g.Count(),
                TotalSales = g.Sum(i => i.TotalAmount),
                TotalTax = g.Sum(i => i.TaxAmount),
                LastInvoiceDate = g.Max(i => i.InvoiceDate)
            })
            .ToListAsync();

        var storeNames = await _dbContext.Stores.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        var summaries = rows.Select(r => new StoreSalesSummary
        {
            StoreId = r.StoreId,
            StoreName = r.StoreId.HasValue && storeNames.ContainsKey(r.StoreId.Value)
                ? storeNames[r.StoreId.Value]
                : "Unassigned",
            InvoiceCount = r.InvoiceCount,
            TotalSales = r.TotalSales,
            TotalTax = r.TotalTax,
            LastInvoiceDate = r.LastInvoiceDate
        })
        .OrderByDescending(s => s.TotalSales)
        .ToList();

        return summaries;
    }

    // USR-03: stock on hand per store, derived from stock transactions
    public async Task<List<StoreStockSummary>> GetStoreStockAsync(int storeId)
    {
        var rows = await _dbContext.StockTransactions.AsNoTracking()
            .Where(t => t.StoreId == storeId)
            .GroupBy(t => t.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                QuantityOnHand = g.Sum(t => t.QuantityChange)
            })
            .ToListAsync();

        var productIds = rows.Select(r => r.ProductId).ToList();
        var products = await _dbContext.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => (p.Name, p.SKU));

        return rows
            .Select(r => new StoreStockSummary
            {
                ProductId = r.ProductId,
                ProductName = products.TryGetValue(r.ProductId, out var p) ? p.Name : "Unknown",
                SKU = products.TryGetValue(r.ProductId, out var pr) ? pr.SKU : null,
                QuantityOnHand = r.QuantityOnHand
            })
            .OrderBy(s => s.ProductName)
            .ToList();
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
}
