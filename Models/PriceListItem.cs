using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class PriceListItem
{
    [Key]
    public int Id { get; set; }

    public int PriceListId { get; set; }

    public int ProductId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Rate { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal DiscountPct { get; set; }

    [ForeignKey("PriceListId")]
    public PriceList? PriceList { get; set; }

    [ForeignKey("ProductId")]
    public Product? Product { get; set; }

    /// <summary>PAY-06: rate a bill actually gets from this line.</summary>
    [NotMapped]
    public decimal EffectiveRate => Rate * (1 - DiscountPct / 100m);
}
