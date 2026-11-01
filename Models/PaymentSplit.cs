using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// PAY-01: one settle-mode line of a split payment (cash/card/UPI/cheque/bank/wallet/credit).
/// A payment with zero split lines stays in the legacy single-PaymentMode form.
/// "Credit" means "not settled now" — that portion stays outstanding, it is not received cash.
/// </summary>
public class PaymentSplit
{
    [Key]
    public int Id { get; set; }

    public int PaymentId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Mode { get; set; } = "Cash"; // Cash, Card, UPI, Cheque, Bank Transfer, Wallet, Credit

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    /// <summary>Optional line reference, e.g. cheque number or UPI transaction id.</summary>
    [MaxLength(100)]
    public string? Reference { get; set; }

    [ForeignKey("PaymentId")]
    public Payment? Payment { get; set; }
}
