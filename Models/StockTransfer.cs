using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// STK-10: stock transfer between stores. Stock is deducted from the product
/// immediately (system-wide stock is tracked at Product level) and moves
/// through an "InTransit" window until the destination store receives it.
/// </summary>
public class StockTransfer
{
    [Key]
    public int Id { get; set; }

    /// <summary>Human-readable transfer number, e.g. "TR-20261031-142530".</summary>
    [Required]
    [MaxLength(50)]
    public string TransferNumber { get; set; } = $"TR-{DateTime.Now:yyyyMMdd-HHmmss}";

    public DateTime TransferDate { get; set; } = DateTime.UtcNow;

    public int FromStoreId { get; set; }

    public int ToStoreId { get; set; }

    public int ProductId { get; set; }

    /// <summary>Product name snapshot at transfer time (still meaningful even if the product is later renamed).</summary>
    [Required]
    [MaxLength(200)]
    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    /// <summary>Unit rate snapshot (copied from Product.Rate at transfer time) for value visibility.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Rate { get; set; }

    /// <summary>Rate * Quantity, stored so the grid can show in-transit value without joins.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal TransferValue { get; set; }

    /// <summary>InTransit / Received / Cancelled.</summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "InTransit";

    public DateTime? ReceivedDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("ProductId")]
    public Product? Product { get; set; }

    // Not persisted; filled in by StockTransferService for grid display.
    [NotMapped]
    public string? FromStoreName { get; set; }

    [NotMapped]
    public string? ToStoreName { get; set; }
}
