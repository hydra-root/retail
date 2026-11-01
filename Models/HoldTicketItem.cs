using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// INV-12: one line inside a <see cref="HoldTicket"/>. Deliberately a value snapshot
/// (inputs only — no tax splits): the tax engine re-derives CGST/SGST/IGST when the
/// ticket is recalled, exactly like a fresh scan. There is intentionally no Product
/// navigation property — that would add an FK constraint the schema upgrader's
/// CREATE TABLE does not declare, and held baskets must survive a product rename/edit.
/// </summary>
public class HoldTicketItem
{
    [Key]
    public int Id { get; set; }

    public int HoldTicketId { get; set; }

    /// <summary>Catalogue id when the line was scanned from a real product; kept nullable for legacy rows.</summary>
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

    [Column(TypeName = "decimal(5,2)")]
    public decimal CessRate { get; set; }

    /// <summary>INV-04 pricing mode the line was entered under; restored on recall.</summary>
    public bool TaxInclusive { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Total { get; set; }

    [ForeignKey("HoldTicketId")]
    public HoldTicket? HoldTicket { get; set; }
}
