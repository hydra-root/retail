using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// STK-02: Unit of measure with conversion. A base unit (e.g. Piece) has Factor 1;
/// a derived unit (e.g. Box) stores how many base units it contains (Box = 12 Pcs → Factor 12).
/// </summary>
public class Unit
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Base unit this unit converts to (null for base units).</summary>
    public int? BaseUnitId { get; set; }

    /// <summary>How many base units one of this unit contains (Box of 12 Pcs = 12). Base units = 1.</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal Factor { get; set; } = 1;

    public Unit? BaseUnit { get; set; }

    [NotMapped]
    public bool IsBaseUnit => BaseUnitId == null;

    /// <summary>Show conversion, e.g. "1 Box = 12 Pcs".</summary>
    [NotMapped]
    public string ConversionDisplay => BaseUnit == null
        ? "Base unit"
        : $"1 {Name} = {Factor} {BaseUnit.Symbol}";
}
