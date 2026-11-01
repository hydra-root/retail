using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// USR-03 store-wise pricing: one product's rate at one store, optionally narrowed to a
/// customer group or a single customer. Deliberately a NEW table — PriceList / PriceListItem
/// are untouched, so every existing party-wise list keeps working unchanged and a single-store
/// shop with no rows here bills exactly as it does today.
///
/// Scope rules (mirrored by StorePriceOverride precedence in StorePricingService):
///   CustomerId set, CustomerGroup null -> store + customer rate (tier 1)
///   CustomerId null, CustomerGroup set -> store + group rate    (tier 2)
///   both null                          -> store default rate    (tier 3)
/// Rate/DiscountPct follow PriceListItem's semantics: the billed rate is Rate * (1 - DiscountPct/100).
/// </summary>
public class StorePriceOverride
{
    [Key]
    public int Id { get; set; }

    /// <summary>The store this rate belongs to. Required — there is no "all stores" row here;
    /// "all stores" is simply the absence of an override (the global party list / master rate wins).</summary>
    public int StoreId { get; set; }

    public int ProductId { get; set; }

    /// <summary>Narrowest scope: this rate applies to this buyer at this store only.</summary>
    public int? CustomerId { get; set; }

    /// <summary>Mid scope: this rate applies to every buyer in this group at this store.
    /// Ignored (set to null by the service) when CustomerId is set.</summary>
    [MaxLength(100)]
    public string? CustomerGroup { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Rate { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal DiscountPct { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Username that last wrote this row (UTC write time is UpdatedAt).</summary>
    [MaxLength(100)]
    public string? UpdatedBy { get; set; }

    [ForeignKey("StoreId")]
    public Store? Store { get; set; }

    [ForeignKey("ProductId")]
    public Product? Product { get; set; }

    /// <summary>What a bill actually pays for this line — same formula as PriceListItem.EffectiveRate.</summary>
    [NotMapped]
    public decimal EffectiveRate => Rate * (1 - DiscountPct / 100m);
}
