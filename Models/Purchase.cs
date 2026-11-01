using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class Purchase
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string PurchaseNumber { get; set; } = string.Empty;

    public DateTime PurchaseDate { get; set; } = DateTime.UtcNow;

    public int? SupplierId { get; set; }

    [MaxLength(200)]
    public string? SupplierName { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SubTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PaidAmount { get; set; }

    [MaxLength(50)]
    public string PaymentMode { get; set; } = "Cash";

    [MaxLength(50)]
    public string Status { get; set; } = "Pending"; // Pending, Paid, Partial

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("SupplierId")]
    public Supplier? Supplier { get; set; }

    public List<PurchaseItem> Items { get; set; } = new();
}
