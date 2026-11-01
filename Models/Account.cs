using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class Account
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public int? GroupId { get; set; }

    [Required]
    [MaxLength(50)]
    public string AccountType { get; set; } = string.Empty; // Asset, Liability, Income, Expense, Equity

    [Column(TypeName = "decimal(18,2)")]
    public decimal OpeningBalance { get; set; }

    public bool IsSystem { get; set; } = false;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("GroupId")]
    public AccountGroup? Group { get; set; }

    [NotMapped]
    public decimal CurrentBalance => OpeningBalance; // Simplified - computed from journal lines in real app
}
