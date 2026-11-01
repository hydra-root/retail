using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// STK-02: Product category with optional parent category (tree structure).
/// </summary>
public class Category
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int? ParentCategoryId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Category? ParentCategory { get; set; }

    /// <summary>Shows full path when a parent exists, e.g. "Beverages / Cola".</summary>
    [NotMapped]
    public string DisplayName => ParentCategory != null
        ? $"{ParentCategory.Name} / {Name}"
        : Name;
}
