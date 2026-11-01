namespace RetailBillingApp.Services;

/// <summary>
/// RPT-12: item-wise profit - per-product sales (excl. tax), COGS via the
/// latest purchase rate and gross profit for a date range, ranked top or bottom.
/// </summary>
public interface IItemProfitService
{
    /// <summary>
    /// Item-wise profit rows for invoices in [start, end] inclusive (estimates excluded).
    /// topMode: "Top" (profit desc) / "Bottom" (profit asc) / "All" (name asc).
    /// Rows are truncated to <paramref name="count"/>.
    /// </summary>
    Task<List<ItemProfitRow>> GetItemProfitAsync(DateTime start, DateTime end, string topMode, int count);
}

/// <summary>RPT-12: one product's sales / COGS / profit for the selected period.</summary>
public class ItemProfitRow
{
    public int? ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string SKU { get; set; } = string.Empty;

    public int QuantitySold { get; set; }

    /// <summary>Sales excluding tax - sum of invoice item amounts.</summary>
    public decimal Sales { get; set; }

    /// <summary>Cost of goods sold at the latest purchase rate per product.</summary>
    public decimal COGS { get; set; }

    /// <summary>Gross profit = Sales - COGS (can be negative).</summary>
    public decimal Profit { get; set; }

    /// <summary>Profit margin as a percentage of sales, 0 when sales are nil.</summary>
    public decimal ProfitMarginPercent => Sales > 0m ? Math.Round(Profit * 100m / Sales, 2) : 0m;

    public int InvoiceCount { get; set; }
}
