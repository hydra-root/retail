using System.ComponentModel.DataAnnotations;

namespace RetailBillingApp.Models;

/// <summary>
/// INT-03/04/05: singleton-style configuration row for the Messaging Center
/// (SMTP for Email, DLT registration for SMS, WhatsApp defaults).
/// Only the row with Id == 1 is used; the service creates it on first load.
/// </summary>
public class MessagingSetting
{
    [Key]
    public int Id { get; set; }

    // --- INT-05: Email (SMTP) ---
    [MaxLength(200)]
    public string SmtpHost { get; set; } = string.Empty;

    public int SmtpPort { get; set; } = 587;

    [MaxLength(200)]
    public string SmtpUser { get; set; } = string.Empty;

    [MaxLength(200)]
    public string SmtpPassword { get; set; } = string.Empty;

    [MaxLength(200)]
    public string FromAddress { get; set; } = string.Empty;

    // --- INT-03: SMS / DLT (placeholder until a real gateway is configured) ---
    [MaxLength(20)]
    public string DltSmsSender { get; set; } = string.Empty;

    [MaxLength(100)]
    public string DltTemplateId { get; set; } = string.Empty;

    // --- INT-04: WhatsApp ---
    /// <summary>Default dialing prefix prepended to local mobile numbers (India = 91).</summary>
    [MaxLength(5)]
    public string DefaultWhatsAppCountryCode { get; set; } = "91";
}
