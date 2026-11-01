using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class CreditNote
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string CreditNoteNumber { get; set; } = string.Empty;

    public DateTime CreditNoteDate { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = "Credit"; // Credit or Debit

    public int? OriginalInvoiceId { get; set; }

    [MaxLength(100)]
    public string? OriginalInvoiceNumber { get; set; }

    public int? CustomerId { get; set; }

    [MaxLength(200)]
    public string? CustomerName { get; set; }

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal SubTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    [MaxLength(50)]
    public string Status { get; set; } = "Issued"; // Issued, Applied, Cancelled

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("OriginalInvoiceId")]
    public Invoice? OriginalInvoice { get; set; }

    [ForeignKey("CustomerId")]
    public Customer? Customer { get; set; }

    public List<CreditNoteItem> Items { get; set; } = new();
}
