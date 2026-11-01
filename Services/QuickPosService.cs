using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// INV-12: scan-resolution and hold/recall engine for the Quick POS till. Deliberately small:
/// the money path (tax, stock movement, numbering, PAY-05 credit limit) stays in InvoiceService,
/// which the view model calls directly — nothing here duplicates or bypasses it.
/// </summary>
public class QuickPosService : IQuickPosService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuthService _authService;
    private readonly IAuditService _auditService;
    private readonly IStorePricingService _storePricingService;

    public string? LastValidationError { get; private set; }

    public List<string> Warnings { get; } = new();

    public QuickPosService(AppDbContext dbContext, IAuthService authService,
        IAuditService auditService, IStorePricingService storePricingService)
    {
        _dbContext = dbContext;
        _authService = authService;
        _auditService = auditService;
        _storePricingService = storePricingService;
    }

    public async Task<QuickPosScanResult> ScanAsync(string code, int? customerId, string? customerGroup)
    {
        LastValidationError = null;
        Warnings.Clear();

        var trimmed = (code ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return Fail("Scan a barcode or type a SKU first.");

        // SKU or Barcode, exact match after trimming — same lookup vocabulary the invoice form's
        // product master uses (IProductService.GetProductBySkuAsync covers SKU only, so the till
        // queries both unique-indexed columns here rather than editing the shared service).
        var product = await _dbContext.Products
            .FirstOrDefaultAsync(p => p.SKU == trimmed || p.Barcode == trimmed);

        if (product == null)
            return Fail($"No catalogue product for '{trimmed}' — add it in Products first, " +
                        "so stock and party pricing can apply.");

        // STK-06: reuse InvoiceService's exact expired-product wording so the till refuses at the
        // scan what the invoice would refuse at the save (no surprise block after the basket is built).
        if (product.IsExpired)
            return Fail($"Product '{product.Name}' is EXPIRED (expired on {product.ExpiryDate:dd-MMM-yyyy}) and cannot be billed.");

        var result = new QuickPosScanResult
        {
            Success = true,
            Product = product,
        };

        // PAY-06 + USR-03: the full resolution chain the invoice form runs (store overrides →
        // customer list → group list → product master rate), priced for the buyer at the till.
        var price = await _storePricingService.ResolveAsync(product.Id, customerId, customerGroup);
        result.Rate = price.Rate ?? product.Rate;
        result.PriceSource = price.Rate.HasValue ? $"Rate from {price.Source}" : price.Source;

        // STK-03: out-of/near-low stock is a till notice, not a scan block — the negative-stock
        // policy inside InvoiceService has the final say at settle, exactly as it does today.
        if (product.StockQty <= 0)
        {
            result.IsWarning = true;
            var warn = $"'{product.Name}' is OUT OF STOCK (on hand {product.StockQty}) — " +
                       "settling will follow the Stock Policy (block/warn).";
            Warnings.Add(warn);
            result.Message = warn;
        }
        else if (product.IsLowStock)
        {
            result.IsWarning = true;
            var warn = $"'{product.Name}' is at low stock ({product.StockQty} on hand).";
            Warnings.Add(warn);
            result.Message = warn;
        }

        return result;
    }

    public async Task<bool> HoldTicketAsync(HoldTicket ticket)
    {
        try
        {
            LastValidationError = null;

            if (ticket.Items.Count == 0)
            {
                LastValidationError = "Nothing to hold — scan at least one item first.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ticket.HoldLabel))
                ticket.HoldLabel = $"Hold {DateTime.Now:HH:mm:ss}";
            else if (ticket.HoldLabel.Length > 100)
                ticket.HoldLabel = ticket.HoldLabel[..100];

            ticket.HeldAt = DateTime.UtcNow;

            _dbContext.HoldTickets.Add(ticket);
            await _dbContext.SaveChangesAsync();

            // USR-01 convention: the audit row names the till's store so multi-store trails stay readable.
            var storeSuffix = _authService.CurrentUser?.StoreId is int storeId ? $" (store {storeId})" : string.Empty;
            await _auditService.RecordAsync("Hold", "HoldTicket", ticket.Id, ticket.HoldLabel + storeSuffix, null, ticket);
            return true;
        }
        catch (Exception ex)
        {
            LastValidationError = ex.Message;
            return false;
        }
    }

    public Task<List<HoldTicket>> GetHeldTicketsAsync() =>
        _dbContext.HoldTickets.Include(t => t.Items)
            .OrderByDescending(t => t.HeldAt).ToListAsync();

    public async Task<HoldTicket?> RecallTicketAsync(int holdTicketId)
    {
        try
        {
            LastValidationError = null;

            var ticket = await _dbContext.HoldTickets
                .Include(t => t.Items)
                .FirstOrDefaultAsync(t => t.Id == holdTicketId);

            if (ticket == null)
            {
                LastValidationError = "That held ticket is no longer on the list — it was recalled in another session.";
                return null;
            }

            // A recalled ticket leaves the held list (INV-12 scope). Lines are tracked through the
            // Include above, so Remove cascades the header + basket in one SaveChanges; afterwards
            // the entity is an in-memory (detached) copy whose Items the till rebuilds from.
            _dbContext.HoldTickets.Remove(ticket);
            await _dbContext.SaveChangesAsync();

            await _auditService.RecordAsync("Recall", "HoldTicket", ticket.Id, ticket.HoldLabel, ticket, null);
            return ticket;
        }
        catch (Exception ex)
        {
            LastValidationError = ex.Message;
            return null;
        }
    }

    private QuickPosScanResult Fail(string message)
    {
        LastValidationError = message;
        return new QuickPosScanResult { Success = false, Message = message };
    }
}
