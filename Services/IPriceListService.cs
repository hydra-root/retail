using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface IPriceListService
{
    Task<List<PriceList>> GetAllPriceListsAsync();
    Task<PriceList?> GetPriceListByIdAsync(int id);
    Task<bool> AddPriceListAsync(PriceList priceList);
    Task<bool> UpdatePriceListAsync(PriceList priceList);
    Task<bool> DeletePriceListAsync(int id);
    /// <summary>PAY-06: the rate that applies for a buyer, and the list it came from.</summary>
    Task<EffectivePrice> GetEffectivePriceAsync(int productId, int? customerId, string? customerGroup);

    /// <summary>PAY-06: attach a product rate to a list (replaces any existing entry for that product).</summary>
    Task<(bool Success, string? Error)> AddItemAsync(int priceListId, int productId, decimal rate, decimal discountPct);

    Task<(bool Success, string? Error)> RemoveItemAsync(int priceListId, int productId);

    Task<int> SaveChangesAsync();
}

/// <summary>
/// PAY-06: resolution result for one product + buyer. Rate is null when no party list
/// covers the product and billing should fall back to the product master rate.
/// </summary>
public class EffectivePrice
{
    public decimal? Rate { get; set; }

    /// <summary>Which list won (or why none did) — shown next to the billed rate.</summary>
    public string Source { get; set; } = "Product master rate";

    /// <summary>Discount percentage carried by the winning list item, when a list applied.</summary>
    public decimal DiscountPct { get; set; }
}
