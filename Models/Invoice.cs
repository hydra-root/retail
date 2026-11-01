using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class Invoice
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string InvoiceNumber { get; set; } = string.Empty;

    public DateTime InvoiceDate { get; set; } = DateTime.UtcNow;

    public int? CustomerId { get; set; }

    [MaxLength(200)]
    public string? CustomerName { get; set; }

    [MaxLength(15)]
    public string? CustomerGSTIN { get; set; }

    [MaxLength(50)]
    public string? PlaceOfSupply { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SubTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Discount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CGST { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SGST { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal IGST { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Cess { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal RoundOff { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PaidAmount { get; set; }

    [MaxLength(50)]
    public string PaymentMode { get; set; } = "Cash";

    [MaxLength(50)]
    public string Status { get; set; } = "Paid";

    /// <summary>INV-02: Bill of Supply (non-GST) - no tax computed.</summary>
    public bool IsBillOfSupply { get; set; }

    /// <summary>INV-07: created as an estimate/quotation; benefits no stock impact until converted.</summary>
    public bool IsEstimate { get; set; }

    /// <summary>PAY-12: settled remaining amount written off (bad debts/settlement discount).</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal WriteOffAmount { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public int? StoreId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("CustomerId")]
    [NotMapped]
    public string TypeLabel => IsEstimate ? "Estimate" : IsBillOfSupply ? "Bill of Supply" : "GST Invoice";

    public Customer? Customer { get; set; }

    public List<InvoiceItem> Items { get; set; } = new();
}
