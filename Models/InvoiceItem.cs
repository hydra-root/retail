using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class InvoiceItem
{
    [Key]
    public int Id { get; set; }

    public int InvoiceId { get; set; }

    public int? ProductId { get; set; }

    [Required]
    [MaxLength(200)]
    public string ProductName { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? HSNCode { get; set; }

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Rate { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal TaxPercentage { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountPct { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CGST { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SGST { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal IGST { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Cess { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Total { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal CessRate { get; set; }

    public bool TaxInclusive { get; set; }

    [ForeignKey("InvoiceId")]
    public Invoice? Invoice { get; set; }

    [ForeignKey("ProductId")]
    public Product? Product { get; set; }
}
