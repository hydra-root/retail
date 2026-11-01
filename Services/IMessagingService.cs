using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// INT-03/04/05: messaging center API - SMTP settings, SMS gateway hook
/// (placeholder), WhatsApp share link via wa.me, Email send with mailto
/// fallback, and a persisted message log.
/// </summary>
public interface IMessagingService
{
    /// <summary>
    /// INT-03: pluggable SMS gateway. Null until a real provider (HTTP/REST or
    /// other) is configured - SendSmsAsync then records a "Draft" log row.
    /// </summary>
    ISmsProvider? SmsProvider { get; set; }

    /// <summary>Loads the singleton settings row, creating it with defaults on first use.</summary>
    Task<MessagingSetting> GetSettingsAsync();

    /// <summary>Persists the singleton settings row.</summary>
    Task SaveSettingsAsync(MessagingSetting settings);

    /// <summary>
    /// INT-03: attempts SMS via the configured provider (DLT sender + template);
    /// no provider configured - logs a "Draft" row and returns it instead.
    /// </summary>
    Task<(bool Success, string Status, string Detail)> SendSmsAsync(string phone, string message, string? reference);

    /// <summary>
    /// INT-04: opens a wa.me share link (v1.0) in the default browser with the
    /// message pre-filled; logged as "Opened in browser".
    /// </summary>
    Task<(bool Success, string Status, string Detail)> SendWhatsAppAsync(string phone, string message, string? reference);

    /// <summary>
    /// INT-05: sends via SMTP when configured (10s timeout); otherwise opens a
    /// mailto: link in the default browser and logs "Opened in browser (no SMTP configured)".
    /// </summary>
    Task<(bool Success, string Status, string Detail)> SendEmailAsync(string to, string subject, string body, string? reference);

    /// <summary>Composes the invoice share text (number, date, customer, total, payment status).</summary>
    Task<(string? Message, string? Error)> BuildInvoiceMessageAsync(int invoiceId);

    /// <summary>BuildInvoiceMessageAsync with an invoice Id or invoice number as reference.</summary>
    Task<(string? Message, string? Error, int? InvoiceId)> BuildInvoiceMessageByReferenceAsync(string reference);

    /// <summary>Latest persistent message log entries (default 100, newest first).</summary>
    Task<List<MessageLogRow>> GetMessageLogAsync(int count = 100);
}

/// <summary>
/// INT-03: contract a real SMS gateway implementation must satisfy. Plug an
/// instance into IMessagingService.SmsProvider to activate SMS sending.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Sends one SMS. DLT sender/template are supplied for Indian DLT-registered gateways.</summary>
    Task<(bool Success, string Detail)> SendAsync(string phone, string message, string? dltSender, string? dltTemplateId);
}

/// <summary>Row for the Messaging Center message log grid.</summary>
public class MessageLogRow
{
    public DateTime Timestamp { get; set; }

    /// <summary>"SMS" / "WhatsApp" / "Email".</summary>
    public string Channel { get; set; } = string.Empty;

    public string Recipient { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;

    /// <summary>"Sent" / "Opened in browser" / "Failed" / "Draft".</summary>
    public string Status { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;
}
