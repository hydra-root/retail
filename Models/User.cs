using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class User
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    public int? RoleId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Role { get; set; } = "User"; // Legacy, kept for backward compatibility

    public int? StoreId { get; set; }

    [ForeignKey("StoreId")]
    public Store? Store { get; set; }

    [ForeignKey("RoleId")]
    public Role? RoleObj { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;

    /// <summary>Consecutive failed login attempts (USR-04 lockout).</summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>Account locked until this time after too many failed attempts.</summary>
    public DateTime? LockedUntil { get; set; }

    public DateTime? LastLoginAt { get; set; }
}
