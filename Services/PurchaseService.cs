using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class PurchaseService : IPurchaseService
{
    private readonly AppDbContext _dbContext;
    private readonly IAutoPostingService _autoPosting;

    public PurchaseService(AppDbContext dbContext, IAutoPostingService autoPostingService)
    {
        _dbContext = dbContext;
        _autoPosting = autoPostingService;
    }

    public Task<List<Purchase>> GetAllPurchasesAsync() =>
        _dbContext.Purchases
            .Include(p => p.Items)
            .Include(p => p.Supplier)
            .OrderByDescending(p => p.PurchaseDate)
            .ToListAsync();

    public Task<Purchase?> GetPurchaseByIdAsync(int id) =>
        _dbContext.Purchases
            .Include(p => p.Items)
            .Include(p => p.Supplier)
            .FirstOrDefaultAsync(p => p.Id == id);

    public async Task<string> GenerateNextPurchaseNumberAsync()
    {
        var count = await _dbContext.Purchases.CountAsync();
        var date = System.DateTime.Today.ToString("yyyyMMdd");
        return $"PUR-{date}-{count + 1:D4}";
    }

    public async Task<bool> CreatePurchaseAsync(Purchase purchase)
    {
        try
        {
            using var txn = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                purchase.CreatedAt = System.DateTime.UtcNow;
                purchase.SubTotal = purchase.Items.Sum(i => i.Amount);
                purchase.TaxAmount = purchase.Items.Sum(i => i.TaxAmount);
                purchase.TotalAmount = purchase.SubTotal + purchase.TaxAmount;

                purchase.Status = purchase.PaymentMode == "Cash" || purchase.PaidAmount >= purchase.TotalAmount
                    ? "Paid" : purchase.PaidAmount > 0 ? "Partial" : "Pending";

                _dbContext.Purchases.Add(purchase);
                await _dbContext.SaveChangesAsync();

                // Add stock for each item
                foreach (var item in purchase.Items)
                {
                    if (item.ProductId.HasValue)
                    {
                        var product = await _dbContext.Products.FindAsync(item.ProductId.Value);
                        if (product != null)
                        {
                            product.StockQty += item.Quantity;
                            product.Batch = item.Batch;
                            product.ExpiryDate = item.ExpiryDate;
                            product.UpdatedAt = System.DateTime.UtcNow;

                            _dbContext.StockTransactions.Add(new StockTransaction
                            {
                                ProductId = item.ProductId.Value,
                                TransactionType = "Purchase",
                                QuantityChange = item.Quantity,
                                Reference = $"Purchase {purchase.PurchaseNumber}",
                                TransactionDate = System.DateTime.UtcNow
                            });
                        }
                    }
                }

                await _dbContext.SaveChangesAsync();
                await txn.CommitAsync();

                // ACC-02: post the purchase to the books; a posting failure never fails the save.
                try
                {
                    await _autoPosting.PostPurchaseAsync(purchase.Id);
                }
                catch { /* posting must never fail a purchase save */ }

                return true;
            }
            catch
            {
                await txn.RollbackAsync();
                throw;
            }
        }
        catch { return false; }
    }

    public async Task<bool> UpdatePurchaseAsync(Purchase purchase)
    {
        try
        {
            _dbContext.Purchases.Update(purchase);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeletePurchaseAsync(int id)
    {
        try
        {
            var purchase = await _dbContext.Purchases.FindAsync(id);
            if (purchase == null) return false;
            _dbContext.Purchases.Remove(purchase);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
}
