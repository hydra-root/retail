using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class PaymentAllocation
{
    [Key]
    public int Id { get; set; }

    public int PaymentId { get; set; }

    [MaxLength(50)]
    public string AllocationType { get; set; } = "Invoice"; // Invoice, Advance, Excess

    public int? InvoiceId { get; set; }

    [MaxLength(100)]
    public string? InvoiceNumber { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal AllocatedAmount { get; set; }

    public DateTime AllocationDate { get; set; } = DateTime.UtcNow;

    [ForeignKey("PaymentId")]
    public Payment? Payment { get; set; }
}
