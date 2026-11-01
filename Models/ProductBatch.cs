using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// STK-05: Batch-wise stock for a product (batch number, mfg/expiry dates, MRP, qty).
/// Multiple batches can exist per product; issued using FEFO (First Expired, First Out).
/// </summary>
public class ProductBatch
{
    [Key]
    public int Id { get; set; }

    public int ProductId { get; set; }

    [Required]
    [MaxLength(100)]
    public string BatchNumber { get; set; } = string.Empty;

    public DateTime? ManufacturingDate { get; set; }

    public DateTime? ExpiryDate { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal MRP { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PurchaseRate { get; set; }

    public int Quantity { get; set; }

    public int? StoreId { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Product? Product { get; set; }

    [NotMapped]
    public string ProductName => Product?.Name ?? string.Empty;

    [NotMapped]
    public bool IsExpired => ExpiryDate.HasValue && ExpiryDate.Value < DateTime.Today;
}
