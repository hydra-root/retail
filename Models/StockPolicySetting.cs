using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// STK-03: singleton configuration row (only Id == 1 is used) holding the
/// negative-stock policy that every stock-moving path consults.
/// </summary>
public class StockPolicySetting
{
    [Key]
    public int Id { get; set; }

    /// <summary>Block / Warn / Allow — what happens when a movement would push stock below zero.</summary>
    [Required]
    [MaxLength(20)]
    public string NegativeStockMode { get; set; } = "Block";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? UpdatedBy { get; set; }

    [NotMapped]
    public string ModeDescription => Describe(NegativeStockMode);

    /// <summary>Plain-language meaning of a mode, shared by the policy editor and its preview.</summary>
    public static string Describe(string mode) => mode switch
    {
        "Allow" => "Negative stock allowed: movements that oversell are posted without a warning, and the ledger records the shortfall.",
        "Warn" => "Negative stock permitted but reported: every oversell posts with a warning naming the product and the shortfall.",
        _ => "Negative stock blocked: overselling is rejected with the on-hand quantity, and the movement must be reduced first."
    };
}
