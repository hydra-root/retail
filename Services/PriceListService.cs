using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class PriceListService : IPriceListService
{
    private readonly AppDbContext _dbContext;

    public PriceListService(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<List<PriceList>> GetAllPriceListsAsync() =>
        _dbContext.PriceLists
            .Include(p => p.Items)
            .ThenInclude(i => i.Product)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

    public Task<PriceList?> GetPriceListByIdAsync(int id) =>
        _dbContext.PriceLists
            .Include(p => p.Items)
            .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(p => p.Id == id);

    public async Task<bool> AddPriceListAsync(PriceList priceList)
    {
        try
        {
            priceList.CreatedAt = System.DateTime.UtcNow;
            _dbContext.PriceLists.Add(priceList);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UpdatePriceListAsync(PriceList priceList)
    {
        try
        {
            _dbContext.PriceLists.Update(priceList);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeletePriceListAsync(int id)
    {
        try
        {
            var priceList = await _dbContext.PriceLists.FindAsync(id);
            if (priceList == null) return false;
            _dbContext.PriceLists.Remove(priceList);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// PAY-06: party-wise rate resolution — customer list first, then group list, then no
    /// party price (caller falls back to the product master rate).
    /// </summary>
    public async Task<EffectivePrice> GetEffectivePriceAsync(int productId, int? customerId, string? customerGroup)
    {
        var today = DateTime.Today;

        var query = _dbContext.PriceLists
            .Include(p => p.Items)
            .Where(p => p.IsActive &&
                        p.ValidFrom <= today &&
                        (!p.ValidTo.HasValue || p.ValidTo >= today) &&
                        p.Items.Any(i => i.ProductId == productId));

        PriceList? winner = null;

        if (customerId.HasValue)
            winner = await query.FirstOrDefaultAsync(p => p.CustomerId == customerId);

        if (winner == null && !string.IsNullOrWhiteSpace(customerGroup))
            winner = await _dbContext.PriceLists
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.CustomerGroup == customerGroup &&
                                          p.IsActive &&
                                          p.ValidFrom <= today &&
                                          (!p.ValidTo.HasValue || p.ValidTo >= today) &&
                                          p.Items.Any(i => i.ProductId == productId));

        if (winner == null)
            return new EffectivePrice { Rate = null, Source = "No party list — product master rate" };

        var line = winner.Items.First(i => i.ProductId == productId);
        return new EffectivePrice
        {
            Rate = line.Rate * (1 - line.DiscountPct / 100m),
            Source = $"{winner.Name}{(winner.CustomerGroup != null ? $" ({winner.CustomerGroup})" : string.Empty)}" +
                     (line.DiscountPct > 0 ? $" — list rate {line.Rate:0.##} less {line.DiscountPct:0.##}%" : string.Empty),
            DiscountPct = line.DiscountPct
        };
    }

    public async Task<(bool Success, string? Error)> AddItemAsync(int priceListId, int productId, decimal rate, decimal discountPct)
    {
        if (rate < 0) return (false, "List rate cannot be negative.");
        if (discountPct < 0 || discountPct > 100) return (false, "Discount percentage must be between 0 and 100.");

        var priceList = await _dbContext.PriceLists
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == priceListId);
        if (priceList == null) return (false, "Price list not found.");

        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null) return (false, "Product not found.");

        var existing = priceList.Items.FirstOrDefault(i => i.ProductId == productId);
        if (existing != null)
        {
            existing.Rate = rate;
            existing.DiscountPct = discountPct;
        }
        else
        {
            priceList.Items.Add(new PriceListItem
            {
                PriceListId = priceList.Id,
                ProductId = productId,
                Rate = rate,
                DiscountPct = discountPct
            });
        }

        try
        {
            await _dbContext.SaveChangesAsync();
            return (true, null);
        }
        catch
        {
            return (false, "Failed to save the price list item.");
        }
    }

    public async Task<(bool Success, string? Error)> RemoveItemAsync(int priceListId, int productId)
    {
        var item = await _dbContext.PriceListItems
            .FirstOrDefaultAsync(i => i.PriceListId == priceListId && i.ProductId == productId);
        if (item == null) return (false, "That product is not on the selected list.");

        _dbContext.PriceListItems.Remove(item);
        try
        {
            await _dbContext.SaveChangesAsync();
            return (true, null);
        }
        catch
        {
            return (false, "Failed to remove the price list item.");
        }
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
}
