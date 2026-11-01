using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class Store
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Location { get; set; }

    [MaxLength(100)]
    public string? Contact { get; set; }

    [MaxLength(20)]
    public string? GSTIN { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    /// <summary>Invoice numbering prefix for this store (e.g. "MS1").</summary>
    [MaxLength(50)]
    public string? InvoicePrefix { get; set; }

    /// <summary>Default printer name for this store (thermal/laser).</summary>
    [MaxLength(100)]
    public string? PrinterName { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
