using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// ACC-06: one line from an imported bank statement CSV.
/// Amount is signed: positive = money in (deposit), negative = money out (withdrawal).
/// </summary>
public class BankStatementLine
{
    [Key]
    public int Id { get; set; }

    public DateTime ValueDate { get; set; }

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ReferenceNo { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    /// <summary>Unmatched / Auto / Manual.</summary>
    [MaxLength(20)]
    public string MatchedType { get; set; } = "Unmatched";

    /// <summary>Human-readable pointer to the matched journal line, e.g. "JE 42: Cash a/c Dr 1,500".</summary>
    [MaxLength(200)]
    public string? MatchedRef { get; set; }

    /// <summary>Unmatched / Reconciled.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Unmatched";

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
