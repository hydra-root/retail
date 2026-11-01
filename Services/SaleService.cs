using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class SaleService : ISaleService
{
    private readonly AppDbContext _dbContext;
    private readonly IAutoPostingService _autoPosting;

    public SaleService(AppDbContext dbContext, IAutoPostingService autoPostingService)
    {
        _dbContext = dbContext;
        _autoPosting = autoPostingService;
    }

    public Task<List<Sale>> GetAllSalesAsync() =>
        _dbContext.Sales.Include(s => s.Customer)
            .OrderByDescending(s => s.SaleDate).ToListAsync();

    public Task<Sale?> GetSaleByIdAsync(int id) =>
        _dbContext.Sales.Include(s => s.Customer)
            .FirstOrDefaultAsync(s => s.Id == id);

    public async Task<bool> RecordSaleAsync(Sale sale)
    {
        try
        {
            sale.SaleDate = System.DateTime.UtcNow;
            _dbContext.Sales.Add(sale);
            await _dbContext.SaveChangesAsync();

            // ACC-02: post the POS sale to the books; a posting failure never fails the save.
            try
            {
                await _autoPosting.PostSaleAsync(sale.Id);
            }
            catch { /* posting must never fail a sale save */ }

            return true;
        }
        catch { return false; }
    }

    public async Task<bool> RecordPaymentAsync(int saleId, decimal amount)
    {
        try
        {
            var sale = await _dbContext.Sales.FindAsync(saleId);
            if (sale == null) return false;
            sale.PaidAmount += amount;
            await _dbContext.SaveChangesAsync();

            // ACC-02: post the POS receipt (part payment) to the books; a posting
            // failure never fails the receipt.
            try
            {
                await _autoPosting.PostSaleReceiptAsync(saleId, amount);
            }
            catch { /* posting must never fail a receipt save */ }

            return true;
        }
        catch { return false; }
    }

    public Task<List<Sale>> GetOutstandingSalesAsync() =>
        _dbContext.Sales.Include(s => s.Customer)
            .Where(s => s.PaidAmount < s.Amount)
            .OrderByDescending(s => s.SaleDate).ToListAsync();

    public async Task<decimal> GetTotalOutstandingAsync()
    {
        var sales = await _dbContext.Sales
            .Where(s => s.PaidAmount < s.Amount)
            .Select(s => new { s.Amount, s.PaidAmount })
            .ToListAsync();
        return sales.Sum(s => s.Amount - s.PaidAmount);
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
}
