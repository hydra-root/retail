namespace RetailBillingApp.Models;

/// <summary>
/// STK-04: A single stock alert row (low stock / out of stock / expiring / expired).
/// Not persisted - computed from the Product table on demand.
/// </summary>
public class StockAlert
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string? SKU { get; set; }

    public string? Category { get; set; }

    /// <summary>Out of Stock | Low Stock | Expiring Soon | Expired</summary>
    public string AlertType { get; set; } = string.Empty;

    /// <summary>Critical | Warning | Info</summary>
    public string Severity { get; set; } = string.Empty;

    public int CurrentStock { get; set; }

    public int MinStockQty { get; set; }

    /// <summary>Suggested reorder quantity (2x the minimum threshold, at least 10)</summary>
    public int ReorderQty { get; set; }

    public DateTime? ExpiryDate { get; set; }

    /// <summary>Negative when already expired.</summary>
    public int? DaysToExpiry { get; set; }

    public string Recommendation
    {
        get
        {
            return AlertType switch
            {
                "Out of Stock" => $"Order {ReorderQty} immediately",
                "Low Stock" => $"Reorder {ReorderQty} units",
                "Expiring Soon" => $"Run clearance/discount before {ExpiryDate:dd-MMM-yyyy}",
                "Expired" => "Remove from shelf and adjust stock out",
                _ => string.Empty
            };
        }
    }
}
