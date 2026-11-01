using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// USR-03 store-wise pricing engine. Read path implements the documented chain
/// store+customer → store+group → store → global customer list → global group list → product master;
/// tiers 4-5 are delegated to IPriceListService so a store with no overrides resolves byte-identically
/// to today. Write path is the only place StorePriceOverrides changes, and every mutation is audited
/// through IAuditService exactly like StockPolicyService does.
/// </summary>
public class StorePricingService : IStorePricingService
{
    private readonly AppDbContext _dbContext;
    private readonly IPriceListService _priceListService;
    private readonly IAuthService _authService;
    private readonly IAuditService _auditService;

    public StorePricingService(AppDbContext dbContext, IPriceListService priceListService,
        IAuthService authService, IAuditService auditService)
    {
        _dbContext = dbContext;
        _priceListService = priceListService;
        _authService = authService;
        _auditService = auditService;
    }

    /// <summary>The till/invoice path: whatever store the signed-in user belongs to.</summary>
    public Task<StorePriceResolution> ResolveAsync(int productId, int? customerId, string? customerGroup) =>
        ResolveAsync(_authService.CurrentUser?.StoreId, productId, customerId, customerGroup);

    public async Task<StorePriceResolution> ResolveAsync(int? storeId, int productId, int? customerId, string? customerGroup)
    {
        // ---- Tiers 1-3: store overrides, narrowest scope first ----
        if (storeId is int sid)
        {
            var candidates = await _dbContext.StorePriceOverrides.AsNoTracking()
                .Where(o => o.StoreId == sid && o.ProductId == productId)
                .ToListAsync();

            var winner = PickByScope(candidates, customerId, customerGroup);
            if (winner != null)
            {
                var storeName = await GetStoreNameAsync(sid);
                return new StorePriceResolution
                {
                    Rate = winner.Rate * (1 - winner.DiscountPct / 100m),
                    DiscountPct = winner.DiscountPct,
                    Source = DescribeOverride(winner, storeName),
                    Origin = TierOf(winner),
                    StoreId = sid,
                    StoreName = storeName,
                    OverrideId = winner.Id
                };
            }
        }

        // ---- Tiers 4-5: the existing party-wise engine, untouched ----
        var global = await _priceListService.GetEffectivePriceAsync(productId, customerId, customerGroup);
        if (global.Rate.HasValue)
        {
            return new StorePriceResolution
            {
                Rate = global.Rate,
                DiscountPct = global.DiscountPct,
                Source = global.Source,
                Origin = PriceOrigin.GlobalParty,
                StoreId = storeId
            };
        }

        // ---- Tier 6: product master. Rate stays null on purpose — today's call sites supply
        // Product.Rate themselves (InvoicesViewModel only overwrites when price.Rate.HasValue,
        // QuickPosService uses price.Rate ?? product.Rate), so filling it here would change wording.
        return new StorePriceResolution
        {
            Rate = null,
            Source = global.Source,
            Origin = PriceOrigin.ProductMaster,
            StoreId = storeId
        };
    }

    public async Task<List<StorePriceRow>> GetStorePricingGridAsync(int storeId, int? customerId, string? customerGroup)
    {
        var storeName = await GetStoreNameAsync(storeId) ?? $"store {storeId}";

        var products = await _dbContext.Products.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.SKU, p.Rate })
            .ToListAsync();

        var overrides = await _dbContext.StorePriceOverrides.AsNoTracking()
            .Where(o => o.StoreId == storeId)
            .ToListAsync();

        // One round-trip for the whole party chain, then matched in memory per product:
        // the screen stays O(3 queries) for a large catalogue instead of O(2N).
        var party = await BuildPartyLinesAsync(customerId, customerGroup);

        var scopeLabel = ScopeLabel(customerId, customerGroup);

        return products.Select(p =>
        {
            var winner = PickByScope(
                overrides.Where(o => o.ProductId == p.Id).ToList(), customerId, customerGroup);

            var global = party.GlobalFor(p.Id);

            var row = new StorePriceRow
            {
                ProductId = p.Id,
                ProductName = p.Name,
                SKU = p.SKU,
                MasterRate = p.Rate,
                GlobalRate = global.Rate,
                ScopeLabel = scopeLabel,
            };

            if (winner != null)
            {
                row.OverrideId = winner.Id;
                row.StoreRate = winner.Rate;
                row.StoreDiscountPct = winner.DiscountPct;
                row.Source = $"{StorePricingSource.Describe(TierOf(winner))} — {storeName}";
            }
            else
            {
                row.Source = global.Rate.HasValue
                    ? $"{StorePricingSource.Describe(PriceOrigin.GlobalParty)} — {global.ListName}"
                    : $"{StorePricingSource.Describe(PriceOrigin.ProductMaster)} — {p.Name}";
            }

            return row;
        }).ToList();
    }

    public async Task<(bool Success, string? Error)> SetOverrideAsync(int storeId, int productId, decimal rate,
        decimal discountPct, int? customerId, string? customerGroup, string? notes)
    {
        if (rate < 0) return (false, "Store rate cannot be negative.");
        if (discountPct < 0 || discountPct > 100) return (false, "Discount percentage must be between 0 and 100.");
        if (string.IsNullOrWhiteSpace(customerGroup) && discountPct == 0 && rate == 0 && false)
            return (false, "unreachable");

        var store = await _dbContext.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == storeId);
        if (store == null) return (false, "Store not found.");

        var product = await _dbContext.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null) return (false, "Product not found.");

        // Same normalisation as resolution: a customer-scoped row carries no group, so the two
        // scopes can never hold a row each and disagree about which one wins.
        var group = customerId.HasValue ? null : NullIfBlank(customerGroup);

        var existing = await _dbContext.StorePriceOverrides.FirstOrDefaultAsync(o =>
            o.StoreId == storeId && o.ProductId == productId
            && o.CustomerId == customerId
            && (o.CustomerGroup == null ? group == null : o.CustomerGroup == group));

        var before = existing == null ? null : Snapshot(existing, store.Name, product.Name);
        var origin = existing == null ? "Create" : "Update";

        if (existing == null)
        {
            existing = new StorePriceOverride
            {
                StoreId = storeId,
                ProductId = productId,
                CustomerId = customerId,
                CustomerGroup = group,
                CreatedAt = DateTime.UtcNow,
            };
            _dbContext.StorePriceOverrides.Add(existing);
        }

        existing.Rate = rate;
        existing.DiscountPct = discountPct;
        existing.Notes = NullIfBlank(notes);
        existing.UpdatedAt = DateTime.UtcNow;
        existing.UpdatedBy = _authService.CurrentUser?.Username;

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            return (false, "Failed to save the store price.");
        }

        await _auditService.RecordAsync(origin, "StorePriceOverride", existing.Id,
            $"{store.Name} · {product.Name} · {ScopeLabel(customerId, group)}",
            before, Snapshot(existing, store.Name, product.Name));

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ClearOverrideAsync(int overrideId)
    {
        var row = await _dbContext.StorePriceOverrides.FirstOrDefaultAsync(o => o.Id == overrideId);
        if (row == null) return (false, "That store override is no longer on the list.");

        var storeName = await GetStoreNameAsync(row.StoreId);
        var productName = await _dbContext.Products.AsNoTracking()
            .Where(p => p.Id == row.ProductId).Select(p => p.Name).FirstOrDefaultAsync() ?? $"product {row.ProductId}";

        var before = Snapshot(row, storeName ?? $"store {row.StoreId}", productName);

        _dbContext.StorePriceOverrides.Remove(row);
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            return (false, "Failed to clear the store price.");
        }

        await _auditService.RecordAsync("Delete", "StorePriceOverride", row.Id,
            $"{storeName} · {productName} · {ScopeLabel(row.CustomerId, row.CustomerGroup)}", before, null);

        return (true, null);
    }

    public async Task<(int ClearedCount, string? Error)> ClearStoreOverridesAsync(int storeId,
        int? scopeCustomerId, string? scopeCustomerGroup)
    {
        var query = _dbContext.StorePriceOverrides.Where(o => o.StoreId == storeId);

        // Scope filters mirror resolution: a blank scope means "this exact scope row", i.e. the
        // store-default rows, only when the caller asked for a scope; null/null means the whole store.
        if (scopeCustomerId != null || !string.IsNullOrWhiteSpace(scopeCustomerGroup))
        {
            var group = scopeCustomerId != null ? null : NullIfBlank(scopeCustomerGroup);
            query = query.Where(o => o.CustomerId == scopeCustomerId
                && (o.CustomerGroup == null ? group == null : o.CustomerGroup == group));
        }

        var rows = await query.ToListAsync();
        if (rows.Count == 0) return (0, null);

        var storeName = await GetStoreNameAsync(storeId) ?? $"store {storeId}";

        _dbContext.StorePriceOverrides.RemoveRange(rows);
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            return (0, "Failed to clear the store overrides.");
        }

        // One audit row for a bulk clear: IAuditService.RecordAsync saves per call, so writing one
        // row per product would turn a 500-product clear into 500 transactions.
        await _auditService.RecordAsync("Delete", "StorePriceOverride", storeId,
            $"{storeName} · {ScopeLabel(scopeCustomerId, scopeCustomerGroup)} · bulk clear",
            null, new { storeId, storeName, clearedCount = rows.Count, clearedBy = _authService.CurrentUser?.Username });

        return (rows.Count, null);
    }

    public async Task<(int CopiedCount, string? Error)> CopyGlobalRatesAsync(int storeId, List<int>? productIds)
    {
        var store = await _dbContext.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == storeId);
        if (store == null) return (0, "Store not found.");

        var products = await _dbContext.Products.AsNoTracking()
            .Where(p => productIds == null || productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Rate })
            .ToListAsync();
        if (products.Count == 0) return (0, "No products to copy.");

        var party = await BuildPartyLinesAsync(null, null);

        var existing = await _dbContext.StorePriceOverrides
            .Where(o => o.StoreId == storeId && o.CustomerId == null && o.CustomerGroup == null)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var user = _authService.CurrentUser?.Username;
        var touched = 0;

        foreach (var product in products)
        {
            var globalRate = party.GlobalFor(product.Id).Rate;
            if (!globalRate.HasValue) continue; // nothing to snapshot: global already = master rate

            var rate = Math.Round(globalRate.Value, 2, MidpointRounding.AwayFromZero);

            var row = existing.FirstOrDefault(o => o.ProductId == product.Id);
            if (row == null)
            {
                row = new StorePriceOverride
                {
                    StoreId = storeId,
                    ProductId = product.Id,
                    Rate = rate,
                    DiscountPct = 0,
                    Notes = "Copied from global",
                    CreatedAt = now,
                };
                _dbContext.StorePriceOverrides.Add(row);
            }
            else
            {
                row.Rate = rate;
                row.DiscountPct = 0;
                row.Notes = "Copied from global";
            }

            row.UpdatedAt = now;
            row.UpdatedBy = user;
            touched++;
        }

        if (touched == 0)
            return (0, "No party list prices any of these products, so there is nothing to copy — " +
                       "this store already falls back to the product master rates.");

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            return (0, "Failed to copy the global rates into the store.");
        }

        await _auditService.RecordAsync("Create", "StorePriceOverride", storeId,
            $"{store.Name} · copy global rates", null,
            new { storeId, storeName = store.Name, copiedCount = touched, copiedBy = user });

        return (touched, null);
    }

    public async Task<int> CountOverridesAsync(int storeId, int? scopeCustomerId, string? scopeCustomerGroup)
    {
        var group = scopeCustomerId != null ? null : NullIfBlank(scopeCustomerGroup);
        return await _dbContext.StorePriceOverrides.AsNoTracking().CountAsync(o =>
            o.StoreId == storeId && o.CustomerId == scopeCustomerId
            && (o.CustomerGroup == null ? group == null : o.CustomerGroup == group));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Tier 1 → 2 → 3, in one pass over the store's rows for one product.</summary>
    private static StorePriceOverride? PickByScope(List<StorePriceOverride> rows, int? customerId, string? customerGroup)
    {
        if (rows.Count == 0) return null;

        if (customerId.HasValue)
        {
            var forCustomer = rows.FirstOrDefault(o => o.CustomerId == customerId.Value);
            if (forCustomer != null) return forCustomer;
        }

        var group = NullIfBlank(customerGroup);
        if (group != null)
        {
            var forGroup = rows.FirstOrDefault(o => o.CustomerId == null && o.CustomerGroup == group);
            if (forGroup != null) return forGroup;
        }

        return rows.FirstOrDefault(o => o.CustomerId == null && string.IsNullOrWhiteSpace(o.CustomerGroup));
    }

    private static PriceOrigin TierOf(StorePriceOverride row) =>
        row.CustomerId.HasValue ? PriceOrigin.StoreCustomer
        : !string.IsNullOrWhiteSpace(row.CustomerGroup) ? PriceOrigin.StoreGroup
        : PriceOrigin.StoreDefault;

    /// <summary>What the till shows when a store row won: the tier, the store and the scope it matched.</summary>
    private static string DescribeOverride(StorePriceOverride row, string? storeName)
    {
        var scope = ScopeLabel(row.CustomerId, row.CustomerGroup);
        var discount = row.DiscountPct > 0 ? $" — rate {row.Rate:0.##} less {row.DiscountPct:0.##}%" : string.Empty;
        return $"{storeName ?? $"store {row.StoreId}"} · {scope}{discount}";
    }

    private static string ScopeLabel(int? customerId, string? customerGroup) =>
        customerId.HasValue ? $"customer #{customerId.Value}"
        : !string.IsNullOrWhiteSpace(customerGroup) ? $"group {customerGroup}"
        : "everyone";

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private Task<string?> GetStoreNameAsync(int storeId) =>
        _dbContext.Stores.AsNoTracking().Where(s => s.Id == storeId).Select(s => s.Name).FirstOrDefaultAsync();

    private static object Snapshot(StorePriceOverride row, string storeName, string productName) => new
    {
        row.Id,
        Store = storeName,
        Product = productName,
        row.CustomerId,
        row.CustomerGroup,
        row.Rate,
        row.DiscountPct,
        Net = row.EffectiveRate,
        row.Notes,
        row.UpdatedBy,
        row.UpdatedAt,
    };

    /// <summary>
    /// Loads the active party lists once and answers "what would global charge for this product".
    /// Mirrors PriceListService.GetEffectivePriceAsync's rule (customer list first, then group list,
    /// both restricted to the active/valid window) so the divergence screen cannot disagree with the till.
    /// </summary>
    private async Task<PartyLineIndex> BuildPartyLinesAsync(int? customerId, string? customerGroup)
    {
        var today = DateTime.Today;

        var lists = await _dbContext.PriceLists.AsNoTracking()
            .Include(p => p.Items)
            .Where(p => p.IsActive && p.ValidFrom <= today && (!p.ValidTo.HasValue || p.ValidTo >= today))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var customerLines = customerId.HasValue
            ? lists.Where(p => p.CustomerId == customerId.Value).SelectMany(p => p.Items.Select(i => (p.Name, i))).ToList()
            : new List<(string Name, PriceListItem Item)>();

        var group = NullIfBlank(customerGroup);
        var groupLines = group != null
            ? lists.Where(p => p.CustomerGroup == group).SelectMany(p => p.Items.Select(i => (p.Name, i))).ToList()
            : new List<(string Name, PriceListItem Item)>();

        return new PartyLineIndex(customerLines, groupLines);
    }

    /// <summary>In-memory twin of the two global tiers. Returned rate is already net of the list discount.</summary>
    private sealed class PartyLineIndex
    {
        private readonly List<(string Name, PriceListItem Item)> _customerLines;
        private readonly List<(string Name, PriceListItem Item)> _groupLines;

        public PartyLineIndex(List<(string Name, PriceListItem Item)> customerLines,
            List<(string Name, PriceListItem Item)> groupLines)
        {
            _customerLines = customerLines;
            _groupLines = groupLines;
        }

        public (decimal? Rate, string? ListName) GlobalFor(int productId)
        {
            var hit = _customerLines.FirstOrDefault(l => l.Item.ProductId == productId);
            if (hit.Item == null)
                hit = _groupLines.FirstOrDefault(l => l.Item.ProductId == productId);

            return hit.Item == null
                ? (null, null)
                : (hit.Item.Rate * (1 - hit.Item.DiscountPct / 100m), hit.Name);
        }
    }
}
