using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// INV-12: a ticket the cashier parked ("held") on the Quick POS till so the counter
/// frees up while a shopper runs back to the car. The lines and running totals are
/// snapshotted here; recalling a ticket loads it back into the till and deletes it
/// (see QuickPosService.RecallTicketAsync — held tickets leave the list on recall).
/// </summary>
public class HoldTicket
{
    [Key]
    public int Id { get; set; }

    /// <summary>Cashier-supplied label ("Table 3", "Grey bag guy"); auto-timestamped when left blank.</summary>
    [Required]
    [MaxLength(100)]
    public string HoldLabel { get; set; } = string.Empty;

    /// <summary>UTC, per house rule; the grid shows <see cref="HeldAtLocal"/>.</summary>
    public DateTime HeldAt { get; set; } = DateTime.UtcNow;

    public int? CustomerId { get; set; }

    [MaxLength(200)]
    public string? CustomerName { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SubTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    /// <summary>The parked basket. EF convention wires HoldTicketId as the cascade-FK;
    /// recalling deletes the header and (tracked) lines together.</summary>
    public List<HoldTicketItem> Items { get; set; } = new();

    // ---- [NotMapped] display helpers ----

    [NotMapped]
    public string HeldAtLocal => HeldAt.ToLocalTime().ToString("dd-MMM HH:mm");

    [NotMapped]
    public int LineCount => Items.Count;
}
