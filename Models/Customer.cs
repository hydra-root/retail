using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class Customer
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Mobile { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(15)]
    public string? GSTIN { get; set; }

    [MaxLength(100)]
    public string? State { get; set; }

    [MaxLength(100)]
    public string? CustomerGroup { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CreditLimit { get; set; }

    public int CreditDays { get; set; } = 30;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public string DisplayName => string.IsNullOrWhiteSpace(Mobile) ? Name : $"{Name} ({Mobile})";

    [NotMapped]
    public decimal OutstandingAmount => _outstandingAmount;

    private decimal _outstandingAmount;

    public void SetOutstandingAmount(decimal amount) => _outstandingAmount = amount;
}
