using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class ProductService : IProductService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly IStockPolicyService _stockPolicyService;

    public ProductService(AppDbContext dbContext, IAuditService auditService, IStockPolicyService stockPolicyService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _stockPolicyService = stockPolicyService;
    }

    public Task<List<Product>> GetAllProductsAsync() =>
        _dbContext.Products
            .Include(p => p.CategoryNav)
            .Include(p => p.Unit)
            .ToListAsync();
    public ValueTask<Product?> GetProductByIdAsync(int id) => _dbContext.Products.FindAsync(id);
    public Task<Product?> GetProductBySkuAsync(string sku) =>
        _dbContext.Products.FirstOrDefaultAsync(p => p.SKU == sku);

    public Task<List<Product>> GetLowStockProductsAsync() =>
        _dbContext.Products.Where(p => p.StockQty <= p.MinStockQty).ToListAsync();

    public Task<List<Product>> GetExpiredProductsAsync() =>
        _dbContext.Products.Where(p => p.ExpiryDate.HasValue && p.ExpiryDate.Value < System.DateTime.Today).ToListAsync();

    public async Task<bool> AddProductAsync(Product product)
    {
        try
        {
            product.CreatedAt = System.DateTime.UtcNow;
            _dbContext.Products.Add(product);
            await _dbContext.SaveChangesAsync();

            // ACC-10: audit the create
            await _auditService.RecordAsync("Create", "Product", product.Id, product.Name, null, product);
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateProductAsync(Product product)
    {
        try
        {
            var before = await _dbContext.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == product.Id);
            product.UpdatedAt = System.DateTime.UtcNow;
            _dbContext.Products.Update(product);
            await _dbContext.SaveChangesAsync();

            // ACC-10: audit the field-level diff
            await _auditService.RecordAsync("Update", "Product", product.Id, product.Name, before, product);
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteProductAsync(int id)
    {
        try
        {
            var product = await _dbContext.Products.FindAsync(id);
            if (product == null) return false;
            _dbContext.Products.Remove(product);
            await _dbContext.SaveChangesAsync();

            // ACC-10: audit the delete with the removed snapshot
            await _auditService.RecordAsync("Delete", "Product", id, product.Name, product, null);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Reason of the most recent stock-adjustment failure (STK-07).</summary>
    public string? LastValidationError { get; private set; }

    /// <summary>STK-03: non-blocking negative-stock warnings from the last adjustment.</summary>
    public List<string> StockPolicyWarnings { get; } = new();

    /// <summary>STK-07: recent stock adjustments (newest first).</summary>
    public async Task<List<AdjustmentHistoryRow>> GetAdjustmentHistoryAsync(int count = 100)
    {
        var rows = await _dbContext.StockTransactions
            .Where(t => t.TransactionType == "Adjustment")
            .OrderByDescending(t => t.TransactionDate)
            .Take(count)
            .Join(_dbContext.Products.AsNoTracking(),
                t => t.ProductId, p => p.Id,
                (t, p) => new AdjustmentHistoryRow
                {
                    Date = t.TransactionDate,
                    ProductName = p.Name,
                    SKU = p.SKU ?? string.Empty,
                    QuantityChange = t.QuantityChange,
                    Reason = t.Reference ?? string.Empty
                })
            .ToListAsync();

        return rows;
    }

    public async Task<bool> AdjustStockAsync(int productId, decimal quantityChange, string reason)
    {
        try
        {
            StockPolicyWarnings.Clear();
            var product = await _dbContext.Products.FindAsync(productId);
            if (product == null)
            {
                LastValidationError = "Product not found.";
                return false;
            }

            // STK-07/STK-03: the configured negative-stock policy governs adjustments too
            var (allowed, policyMessage) = await _stockPolicyService.EvaluateAsync(
                product.Name, product.StockQty, quantityChange, "Stock adjustment");
            if (!allowed)
            {
                LastValidationError = policyMessage;
                return false;
            }
            if (policyMessage != null) StockPolicyWarnings.Add(policyMessage);

            product.StockQty += (int)quantityChange;
            product.UpdatedAt = System.DateTime.UtcNow;

            _dbContext.StockTransactions.Add(new StockTransaction
            {
                ProductId = productId,
                TransactionType = "Adjustment",
                QuantityChange = quantityChange,
                Reference = reason,
                TransactionDate = System.DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync();

            // ACC-10: audit the adjustment
            await _auditService.RecordAsync("Adjust", "Product", productId, product.Name,
                new { StockQty = product.StockQty - quantityChange },
                new { product.StockQty, Reason = reason });

            LastValidationError = null;
            return true;
        }
        catch
        {
            LastValidationError = "Failed to adjust stock.";
            return false;
        }
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
}

/// <summary>STK-07: one stock-adjustment history row.</summary>
public class AdjustmentHistoryRow
{
    public DateTime Date { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public decimal QuantityChange { get; set; }
    public string Reason { get; set; } = string.Empty;
}
