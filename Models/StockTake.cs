using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// STK-14: physical stock verification (stocktake) header.
/// Snapshot of system quantities at creation time; counts are recorded
/// item-by-item and completing the take posts variances as adjustments.
/// </summary>
public class StockTake
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string StockTakeNumber { get; set; } = string.Empty;

    public DateTime StockTakeDate { get; set; } = DateTime.UtcNow;

    /// <summary>Store this stocktake belongs to (USR-03 store-wise stock). Null = legacy/global.</summary>
    public int? StoreId { get; set; }

    /// <summary>"Open" while counting, "Completed" once variances are posted.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Open";

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<StockTakeItem> Items { get; set; } = new();
}

/// <summary>
/// One product line on a stocktake sheet. SystemQty is the snapshot at
/// creation; CountedQty is filled in during the physical count.
/// </summary>
public class StockTakeItem
{
    [Key]
    public int Id { get; set; }

    public int StockTakeId { get; set; }

    public int ProductId { get; set; }

    /// <summary>Snapshot of the product name at stocktake creation time.</summary>
    [Required]
    [MaxLength(200)]
    public string ProductName { get; set; } = string.Empty;

    /// <summary>System (book) quantity when the stocktake was created.</summary>
    public int SystemQty { get; set; }

    /// <summary>Physically counted quantity. Null until counted.</summary>
    public int? CountedQty { get; set; }

    /// <summary>Counted - System. Positive = found extra, negative = shortage.</summary>
    [NotMapped]
    public int Variance => (CountedQty ?? SystemQty) - SystemQty;

    /// <summary>Variance valued at the product's rate; set by the service on completion.</summary>
    [NotMapped]
    public decimal VarianceValue { get; set; }

    /// <summary>True once the item's count has been recorded.</summary>
    public bool Saved { get; set; }
}
