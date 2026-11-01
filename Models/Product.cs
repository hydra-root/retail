using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class Product
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? SKU { get; set; }

    [MaxLength(100)]
    public string? Barcode { get; set; }

    [MaxLength(50)]
    public string? Category { get; set; }

    [MaxLength(20)]
    public string? HSNCode { get; set; }

    [MaxLength(100)]
    public string? Brand { get; set; }

    public int? CategoryId { get; set; }

    public int? UnitId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Rate { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal TaxPercentage { get; set; }

    public int StockQty { get; set; }

    public int MinStockQty { get; set; }

    public string? Batch { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public Category? CategoryNav { get; set; }

    public Unit? Unit { get; set; }

    [NotMapped]
    public bool IsLowStock => StockQty <= MinStockQty;

    [NotMapped]
    public bool IsExpired => ExpiryDate.HasValue && ExpiryDate.Value < DateTime.Today;
}
