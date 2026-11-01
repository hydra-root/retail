using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// USR-03 store-wise pricing. Sits in FRONT of the existing PAY-06 party-wise engine and reuses it:
/// the store tiers are new, the global tiers are delegated to IPriceListService.GetEffectivePriceAsync
/// verbatim, so unwiring this service leaves billing behaving exactly as today.
///
/// Documented precedence chain for (product, store, buyer):
///   1. StoreCustomer  StorePriceOverride  StoreId + ProductId + CustomerId
///   2. StoreGroup     StorePriceOverride  StoreId + ProductId + CustomerGroup
///   3. StoreDefault   StorePriceOverride  StoreId + ProductId, no scope columns
///   4. GlobalParty    an active PriceList for the customer, else for its group  (delegated to IPriceListService)
///   5. ProductMaster  Product.Rate — returned as Rate = null so today's call-site fallback still supplies it
/// </summary>
public interface IStorePricingService
{
    /// <summary>Resolves for the logged-in user's store (IAuthService.CurrentUser?.StoreId), the same
    /// store source InvoiceService stamps onto an invoice. Null store = single-store shop = tiers 1-3 skip.</summary>
    Task<StorePriceResolution> ResolveAsync(int productId, int? customerId, string? customerGroup);

    /// <summary>Resolves for an explicit store (management screen, reports, HQ re-pricing).</summary>
    Task<StorePriceResolution> ResolveAsync(int? storeId, int productId, int? customerId, string? customerGroup);

    /// <summary>One row per catalogue product for the chosen store + scope: global rate, store rate,
    /// delta and which tier a bill would actually pay. Three queries total whatever the catalogue size.</summary>
    Task<List<StorePriceRow>> GetStorePricingGridAsync(int storeId, int? customerId, string? customerGroup);

    /// <summary>Insert-or-update one override (the only write path; audited as Create/Update).</summary>
    Task<(bool Success, string? Error)> SetOverrideAsync(int storeId, int productId, decimal rate, decimal discountPct,
        int? customerId, string? customerGroup, string? notes);

    /// <summary>Delete one override row (audited as Delete with the row as the before-snapshot).</summary>
    Task<(bool Success, string? Error)> ClearOverrideAsync(int overrideId);

    /// <summary>Drop a whole scope's overrides. Pass scopeCustomerId/scopeCustomerGroup to limit the blast
    /// radius; both null clears every override for the store. Audited once with the deleted count.</summary>
    Task<(int ClearedCount, string? Error)> ClearStoreOverridesAsync(int storeId, int? scopeCustomerId, string? scopeCustomerGroup);

    /// <summary>Snapshot the currently-resolvable global rates into the store as store-default overrides,
    /// so the store starts from global and can then diverge product by product. productIds null = whole catalogue.</summary>
    Task<(int CopiedCount, string? Error)> CopyGlobalRatesAsync(int storeId, List<int>? productIds);

    /// <summary>Count of overrides on a store/scope — the management screen's headline number.</summary>
    Task<int> CountOverridesAsync(int storeId, int? scopeCustomerId, string? scopeCustomerGroup);
}

/// <summary>Tier that produced a rate. Kept as an enum so the till and the audit trail can label it
/// without parsing the free-text Source.</summary>
public enum PriceOrigin
{
    StoreCustomer,
    StoreGroup,
    StoreDefault,
    GlobalParty,
    ProductMaster,
}

public static class StorePricingSource
{
    /// <summary>DescribeSource-style tier label. Short enough for a till strip, stable enough to log.</summary>
    public static string Describe(PriceOrigin origin) => origin switch
    {
        PriceOrigin.StoreCustomer => "Store + customer rate",
        PriceOrigin.StoreGroup => "Store + group rate",
        PriceOrigin.StoreDefault => "Store rate",
        PriceOrigin.GlobalParty => "Global party list",
        _ => "Product master rate",
    };

    /// <summary>Tier label plus the detail the winning rule carries (store/party list name).</summary>
    public static string DescribeFull(StorePriceResolution resolution) =>
        $"{Describe(resolution.Origin)} — {resolution.Source}";
}

/// <summary>
/// USR-03 resolution result. Derives from PAY-06's EffectivePrice on purpose: Rate/Source/DiscountPct
/// keep their existing meaning, so a call site that today reads price.Rate / price.Source compiles and
/// behaves the same after swapping IPriceListService for IStorePricingService.
/// </summary>
public class StorePriceResolution : EffectivePrice
{
    /// <summary>Which tier of the chain won.</summary>
    public PriceOrigin Origin { get; set; } = PriceOrigin.ProductMaster;

    /// <summary>Store the resolution ran for (null = no store context, so tiers 1-3 were skipped).</summary>
    public int? StoreId { get; set; }

    /// <summary>Store name when a store tier won — used verbatim in the till's source line.</summary>
    public string? StoreName { get; set; }

    /// <summary>Row id of the winning StorePriceOverride, when one won.</summary>
    public int? OverrideId { get; set; }

    /// <summary>True when a store row beat the global chain.</summary>
    public bool IsStoreSpecific => Origin is PriceOrigin.StoreCustomer or PriceOrigin.StoreGroup or PriceOrigin.StoreDefault;

    /// <summary>Tier label for the UI (see StorePricingSource.Describe).</summary>
    public string OriginLabel => StorePricingSource.Describe(Origin);
}

/// <summary>
/// USR-03: one catalogue product as the store-pricing screen sees it. Plain DTO built by
/// StorePricingService (no EF change-tracking), so the DataGrid stays read-only and the edit
/// panel writes through SetOverrideAsync.
/// </summary>
public class StorePriceRow
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? SKU { get; set; }

    /// <summary>Product.Rate — the last resort of the chain.</summary>
    public decimal MasterRate { get; set; }

    /// <summary>Rate the global party chain (customer list → group list) gives for this scope,
    /// before any store override. Null when no party list covers the product.</summary>
    public decimal? GlobalRate { get; set; }

    /// <summary>GlobalRate ?? MasterRate — what a bill at another store would pay.</summary>
    public decimal GlobalNet => GlobalRate ?? MasterRate;

    /// <summary>Store override for this product + scope, when one exists.</summary>
    public int? OverrideId { get; set; }
    public decimal? StoreRate { get; set; }
    public decimal StoreDiscountPct { get; set; }

    /// <summary>What a bill at THIS store pays: the override net when one exists, else the global net.</summary>
    public decimal BilledRate => StoreRate.HasValue ? StoreRate.Value * (1 - StoreDiscountPct / 100m) : GlobalNet;

    /// <summary>BilledRate - GlobalNet: 0 when the store tracks global.</summary>
    public decimal Delta => BilledRate - GlobalNet;

    public bool HasOverride => StoreRate.HasValue;

    /// <summary>Store genuinely charges something different from global (epsilon per house rule).</summary>
    public bool IsDiverged => HasOverride && System.Math.Abs(Delta) > 0.01m;

    /// <summary>Tier label + winning name, e.g. "Store rate — Main Store".</summary>
    public string Source { get; set; } = "Product master rate";

    /// <summary>Scope the row belongs to, for the screen's header ("Everyone", "Wholesale", "Asha Stores").</summary>
    public string ScopeLabel { get; set; } = "Everyone";
}
