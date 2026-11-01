using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace RetailBillingApp.Models;

public class PriceList
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? CustomerGroup { get; set; }

    public int? CustomerId { get; set; }

    [Column(TypeName = "date")]
    public DateTime ValidFrom { get; set; } = DateTime.Today;

    [Column(TypeName = "date")]
    public DateTime? ValidTo { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<PriceListItem> Items { get; set; } = new();

    [ForeignKey("CustomerId")]
    public Customer? Customer { get; set; }
}
