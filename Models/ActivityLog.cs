using System.ComponentModel.DataAnnotations;

namespace RetailBillingApp.Models;

/// <summary>
/// USR-07 Activity log: records logins, failed logins, lockouts, and sensitive actions.
/// </summary>
public class ActivityLog
{
    [Key]
    public int Id { get; set; }

    public int? UserId { get; set; }

    [MaxLength(100)]
    public string? Username { get; set; }

    public int? StoreId { get; set; }

    /// <summary>Action code, e.g. Login, LoginFailed, AccountLocked, Logout, UserCreated, StoreCreated.</summary>
    [Required]
    [MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    /// <summary>Affected entity type, e.g. User, Store, Invoice.</summary>
    [MaxLength(100)]
    public string? EntityType { get; set; }

    /// <summary>Affected entity reference, e.g. user id or invoice number.</summary>
    [MaxLength(100)]
    public string? EntityReference { get; set; }

    [MaxLength(500)]
    public string? Details { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
