using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class RolePermission
{
    [Key]
    public int Id { get; set; }

    public int RoleId { get; set; }

    public int PermissionId { get; set; }

    [ForeignKey("RoleId")]
    public Role? Role { get; set; }

    [ForeignKey("PermissionId")]
    public Permission? Permission { get; set; }
}
