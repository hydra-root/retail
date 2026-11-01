using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace RetailBillingApp.Models;

public class Permission
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty; // CreateInvoice, EditInvoice, DeleteInvoice, etc.

    [Required]
    [MaxLength(100)]
    public string Category { get; set; } = string.Empty; // Invoicing, Inventory, Sales, Accounting, Reports, Users, Settings

    [MaxLength(500)]
    public string? Description { get; set; }

    public List<RolePermission> RolePermissions { get; set; } = new();
}
