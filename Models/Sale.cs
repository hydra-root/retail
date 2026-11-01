using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class Sale
{
    [Key]
    public int Id { get; set; }

    [MaxLength(100)]
    public string? InvoiceNumber { get; set; }

    public DateTime SaleDate { get; set; } = DateTime.UtcNow;

    public int? CustomerId { get; set; }

    [MaxLength(200)]
    public string? CustomerName { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PaidAmount { get; set; }

    [MaxLength(50)]
    public string PaymentMode { get; set; } = "Cash";

    [MaxLength(500)]
    public string? Notes { get; set; }

    [ForeignKey("CustomerId")]
    public Customer? Customer { get; set; }

    [NotMapped]
    public decimal OutstandingAmount => Amount - PaidAmount;

    [NotMapped]
    public bool IsFullyPaid => PaidAmount >= Amount;
}
