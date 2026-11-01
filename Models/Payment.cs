using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class Payment
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string PaymentNumber { get; set; } = string.Empty;

    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(50)]
    public string Direction { get; set; } = "In"; // In or Out

    [Required]
    [MaxLength(50)]
    public string PartyType { get; set; } = "Customer"; // Customer or Supplier

    public int? PartyId { get; set; }

    [MaxLength(200)]
    public string? PartyName { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [MaxLength(50)]
    public string PaymentMode { get; set; } = "Cash";

    [MaxLength(500)]
    public string? Reference { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public int? StoreId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<PaymentAllocation> Allocations { get; set; } = new();

    /// <summary>
    /// PAY-01: split settle-mode lines. When non-empty the payment's PaymentMode reads "Split"
    /// and the line amounts must total exactly to Amount; when empty the single PaymentMode applies.
    /// </summary>
    public List<PaymentSplit> Splits { get; set; } = new();
}
