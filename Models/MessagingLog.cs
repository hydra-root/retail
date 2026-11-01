using System.ComponentModel.DataAnnotations;

namespace RetailBillingApp.Models;

/// <summary>
/// INT-03/04/05: persistent log of every messaging attempt (SMS / WhatsApp /
/// Email) shown on the Message Log tab of the Messaging Center.
/// </summary>
public class MessagingLog
{
    [Key]
    public int Id { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>"SMS" / "WhatsApp" / "Email".</summary>
    [MaxLength(20)]
    public string Channel { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Recipient { get; set; } = string.Empty;

    /// <summary>Invoice number or other reference the message was about.</summary>
    [MaxLength(100)]
    public string Reference { get; set; } = string.Empty;

    /// <summary>"Sent" / "Opened in browser" / "Failed" / "Draft".</summary>
    [MaxLength(50)]
    public string Status { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Detail { get; set; } = string.Empty;
}
