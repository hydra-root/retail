using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class NumberSeries
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty; // e.g., "Sales Invoice", "Purchase Invoice", "Credit Note"

    [Required]
    [MaxLength(50)]
    public string Prefix { get; set; } = string.Empty; // e.g., "INV", "PUR", "CN"

    [Required]
    [MaxLength(50)]
    public string Suffix { get; set; } = string.Empty;

    [Required]
    public int StartNumber { get; set; } = 1;

    public int CurrentNumber { get; set; } = 1;

    [Required]
    [MaxLength(20)]
    public string FinancialYear { get; set; } = string.Empty; // e.g., "2026-27"

    [Required]
    [MaxLength(50)]
    public string DocType { get; set; } = string.Empty; // Invoice, Purchase, CreditNote, Estimate

    public int? StoreId { get; set; }

    public bool ResetEachYear { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public string NextNumber => $"{Prefix}{CurrentNumber:D4}{Suffix}";
}
