using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.Diagnostics;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace RetailBillingApp.Services;

/// <summary>
/// INT-03/04/05: messaging center implementation. Email goes through SMTP
/// (System.Net.Mail) with a mailto: fallback; WhatsApp opens a wa.me link in
/// the browser; SMS stays a placeholder (Draft) until an ISmsProvider is
/// plugged in. Every attempt is recorded in the MessagingLogs table.
/// </summary>
public class MessagingService : IMessagingService
{
    private readonly AppDbContext _dbContext;
    private readonly IEncryptionService _encryption;

    public MessagingService(AppDbContext dbContext, IEncryptionService encryption)
    {
        _dbContext = dbContext;
        _encryption = encryption;
    }

    /// <summary>INT-03: no default gateway ships - assign one to activate real SMS.</summary>
    public ISmsProvider? SmsProvider { get; set; }

    public async Task<MessagingSetting> GetSettingsAsync()
    {
        var settings = await _dbContext.MessagingSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            settings = new MessagingSetting { Id = 1 };
            _dbContext.MessagingSettings.Add(settings);
            await _dbContext.SaveChangesAsync();
        }
        return settings;
    }

    public Task SaveSettingsAsync(MessagingSetting settings)
    {
        settings.Id = 1;
        // SEC-03: the SMTP secret is DPAPI-encrypted before it touches the row, so app.db no
        // longer holds it in clear text. EncryptString is idempotent (already-protected values
        // pass through), which makes an accidental double-save harmless.
        settings.SmtpPassword = _encryption.EncryptString(settings.SmtpPassword ?? string.Empty);
        _dbContext.MessagingSettings.Update(settings);
        return _dbContext.SaveChangesAsync();
    }

    public async Task<(bool Success, string Status, string Detail)> SendSmsAsync(string phone, string message, string? reference)
    {
        var (status, detail) = string.IsNullOrWhiteSpace(phone)
            ? (StatusDraft, "No recipient phone number entered.")
            : SmsProvider == null
                ? (StatusDraft, "No SMS provider configured — configure provider or use WhatsApp/Email.")
                : (StatusSent, string.Empty);

        if (status == StatusDraft)
        {
            await LogAsync(ChannelsSms, phone, reference, status, detail);
            return (status == StatusSent, status, detail);
        }

        try
        {
            var settings = await GetSettingsAsync();
            var (ok, providerDetail) = await SmsProvider!.SendAsync(phone, message, settings.DltSmsSender, settings.DltTemplateId);
            var sentStatus = ok ? StatusSent : StatusFailed;
            await LogAsync(ChannelsSms, phone, reference, sentStatus, string.IsNullOrWhiteSpace(providerDetail) ? sentStatus : providerDetail);
            return (ok, sentStatus, providerDetail);
        }
        catch (Exception ex)
        {
            await LogAsync(ChannelsSms, phone, reference, StatusFailed, ex.Message);
            return (false, StatusFailed, ex.Message);
        }
    }

    public async Task<(bool Success, string Status, string Detail)> SendWhatsAppAsync(string phone, string message, string? reference)
    {
        var digits = DigitsOnly(phone);
        if (digits.Length == 0)
        {
            await LogAsync(ChannelsWhatsApp, phone, reference, StatusDraft, "No recipient mobile number entered.");
            return (false, StatusDraft, "No recipient mobile number entered.");
        }

        var settings = await GetSettingsAsync();
        // Numbers longer than 10 digits are assumed to already carry the country code.
        var international = digits.Length > 10 ? digits : settings.DefaultWhatsAppCountryCode + digits;
        var url = $"https://wa.me/{international}?text={Uri.EscapeDataString(message)}";

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            var detail = $"WhatsApp share link opened in browser: {url}";
            await LogAsync(ChannelsWhatsApp, phone, reference, StatusOpenedInBrowser, detail);
            return (true, StatusOpenedInBrowser, "WhatsApp share link opened in your browser — press Send there to deliver.");
        }
        catch (Exception ex)
        {
            await LogAsync(ChannelsWhatsApp, phone, reference, StatusFailed, ex.Message);
            return (false, StatusFailed, ex.Message);
        }
    }

    public async Task<(bool Success, string Status, string Detail)> SendEmailAsync(string to, string subject, string body, string? reference)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            await LogAsync(ChannelsEmail, to, reference, StatusDraft, "No recipient email address entered.");
            return (false, StatusDraft, "No recipient email address entered.");
        }

        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            var mailto = $"mailto:{Uri.EscapeDataString(to)}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
            try
            {
                Process.Start(new ProcessStartInfo(mailto) { UseShellExecute = true });
                await LogAsync(ChannelsEmail, to, reference, StatusOpenedInBrowser, "Opened in browser (no SMTP configured): mailto compose window.");
                return (true, StatusOpenedInBrowser, "No SMTP configured — mailto compose window opened in your default email app.");
            }
            catch (Exception ex)
            {
                await LogAsync(ChannelsEmail, to, reference, StatusFailed, $"mailto fallback failed: {ex.Message}");
                return (false, StatusFailed, $"mailto fallback failed: {ex.Message}");
            }
        }

        try
        {
            using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
            {
                EnableSsl = settings.SmtpPort != 25,
                Timeout = 10_000,
            };
            if (!string.IsNullOrWhiteSpace(settings.SmtpUser))
                client.Credentials = new System.Net.NetworkCredential(settings.SmtpUser,
                    _encryption.DecryptString(settings.SmtpPassword));

            using var mail = new MailMessage(
                string.IsNullOrWhiteSpace(settings.FromAddress) ? settings.SmtpUser : settings.FromAddress,
                to, subject, body);
            client.Send(mail);

            await LogAsync(ChannelsEmail, to, reference, StatusSent, $"Sent via {settings.SmtpHost}:{settings.SmtpPort}.");
            return (true, StatusSent, $"Email sent via {settings.SmtpHost}:{settings.SmtpPort}.");
        }
        catch (Exception ex)
        {
            await LogAsync(ChannelsEmail, to, reference, StatusFailed, ex.Message);
            return (false, StatusFailed, $"Email failed: {ex.Message}");
        }
    }

    public async Task<(string? Message, string? Error)> BuildInvoiceMessageAsync(int invoiceId)
    {
        var invoice = await _dbContext.Invoices.AsNoTracking()
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);
        if (invoice == null)
            return (null, $"Invoice with Id {invoiceId} not found.");

        return (ComposeInvoiceText(invoice), null);
    }

    public async Task<(string? Message, string? Error, int? InvoiceId)> BuildInvoiceMessageByReferenceAsync(string reference)
    {
        var trimmed = (reference ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return (null, "Enter an invoice Id or invoice number first.", null);

        if (int.TryParse(trimmed, out var id))
        {
            var (message, error) = await BuildInvoiceMessageAsync(id);
            return (message, error, message == null ? null : id);
        }

        var invoice = await _dbContext.Invoices.AsNoTracking()
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.InvoiceNumber == trimmed);
        if (invoice == null)
            return (null, $"Invoice \"{trimmed}\" not found.", null);

        return (ComposeInvoiceText(invoice), null, invoice.Id);
    }

    public async Task<List<MessageLogRow>> GetMessageLogAsync(int count = 100)
    {
        return await _dbContext.MessagingLogs.AsNoTracking()
            .OrderByDescending(l => l.Timestamp)
            .ThenByDescending(l => l.Id)
            .Take(count)
            .Select(l => new MessageLogRow
            {
                Timestamp = l.Timestamp,
                Channel = l.Channel,
                Recipient = l.Recipient,
                Reference = l.Reference,
                Status = l.Status,
                Detail = l.Detail
            })
            .ToListAsync();
    }

    // --- helpers ---

    private static string ComposeInvoiceText(Invoice invoice)
    {
        var balance = invoice.TotalAmount - invoice.PaidAmount;
        var paymentStatus = invoice.Status;
        if (invoice.PaidAmount >= invoice.TotalAmount && invoice.TotalAmount > 0)
            paymentStatus = "Paid";
        else if (invoice.PaidAmount > 0)
            paymentStatus = $"Unpaid (paid {invoice.PaidAmount:N2}, balance {balance:N2})";
        else if (invoice.TotalAmount > 0)
            paymentStatus = $"Unpaid (balance {balance:N2})";

        var customer = invoice.Customer?.Name ?? invoice.CustomerName ?? string.Empty;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Invoice {invoice.InvoiceNumber}");
        sb.AppendLine($"Date: {invoice.InvoiceDate:dd-MMM-yyyy}");
        if (!string.IsNullOrWhiteSpace(customer))
            sb.AppendLine($"Customer: {customer}");
        sb.AppendLine($"Total Amount: Rs. {invoice.TotalAmount:N2}");
        sb.Append($"Payment Status: {paymentStatus}");
        return sb.ToString();
    }

    private static string DigitsOnly(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? string.Empty : Regex.Replace(phone, @"\D", string.Empty);

    private async Task LogAsync(string channel, string? recipient, string? reference, string status, string detail)
    {
        _dbContext.MessagingLogs.Add(new MessagingLog
        {
            Timestamp = DateTime.UtcNow,
            Channel = channel,
            Recipient = (recipient ?? string.Empty).Trim(),
            Reference = (reference ?? string.Empty).Trim(),
            Status = status,
            Detail = detail.Length > 1000 ? detail[..1000] : detail
        });
        await _dbContext.SaveChangesAsync();
    }

    private const string StatusSent = "Sent";
    private const string StatusDraft = "Draft";
    private const string StatusFailed = "Failed";
    private const string StatusOpenedInBrowser = "Opened in browser";
    private const string ChannelsSms = "SMS";
    private const string ChannelsWhatsApp = "WhatsApp";
    private const string ChannelsEmail = "Email";
}
