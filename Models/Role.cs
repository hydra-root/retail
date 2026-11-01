using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace RetailBillingApp.Models;

public class Role
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty; // Admin, Manager, Cashier, Accountant

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool IsSystem { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<RolePermission> RolePermissions { get; set; } = new();
    public List<User> Users { get; set; } = new();
}
