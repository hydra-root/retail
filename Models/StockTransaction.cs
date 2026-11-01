using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class StockTransaction
{
    [Key]
    public int Id { get; set; }

    public int ProductId { get; set; }

    [Required]
    [MaxLength(50)]
    public string TransactionType { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal QuantityChange { get; set; }

    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Reference { get; set; }

    /// <summary>Store this movement belongs to (USR-03 store-wise stock). Null = legacy/global.</summary>
    public int? StoreId { get; set; }

    [ForeignKey("ProductId")]
    public Product? Product { get; set; }
}
