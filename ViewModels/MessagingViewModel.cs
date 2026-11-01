using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// INT-03/04/05: Messaging Center - SMTP/DLT/WhatsApp settings, compose &amp;
/// send (SMS draft / WhatsApp wa.me link / Email with mailto fallback), and
/// the persistent message log.
/// </summary>
public partial class MessagingViewModel : BaseViewModel
{
    private readonly IMessagingService _messagingService;

    [ObservableProperty]
    private ObservableCollection<MessageLogRow> _logRows = new();

    // --- Settings form (SMTP / DLT / WhatsApp) ---
    [ObservableProperty]
    private string _smtpHost = string.Empty;

    [ObservableProperty]
    private string _smtpPort = "587";

    [ObservableProperty]
    private string _smtpUser = string.Empty;

    [ObservableProperty]
    private string _smtpPassword = string.Empty;

    [ObservableProperty]
    private string _fromAddress = string.Empty;

    [ObservableProperty]
    private string _dltSmsSender = string.Empty;

    [ObservableProperty]
    private string _dltTemplateId = string.Empty;

    [ObservableProperty]
    private string _defaultWhatsAppCountryCode = "91";

    // --- Compose & Send form ---
    [ObservableProperty]
    private string _selectedChannel = "WhatsApp";

    [ObservableProperty]
    private string _recipient = string.Empty;

    [ObservableProperty]
    private string _referenceInvoice = string.Empty;

    [ObservableProperty]
    private string _messageText = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public List<string> Channels { get; } = new() { "SMS", "WhatsApp", "Email" };

    private readonly IEncryptionService _encryption;

    public MessagingViewModel(IMessagingService messagingService, IEncryptionService encryption)
    {
        _messagingService = messagingService;
        _encryption = encryption;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var settings = await _messagingService.GetSettingsAsync();
            SmtpHost = settings.SmtpHost;
            SmtpPort = settings.SmtpPort.ToString();
            SmtpUser = settings.SmtpUser;
            // SEC-03: the stored value is DPAPI-protected; show the operator the secret, not the blob.
            SmtpPassword = _encryption.DecryptString(settings.SmtpPassword);
            FromAddress = settings.FromAddress;
            DltSmsSender = settings.DltSmsSender;
            DltTemplateId = settings.DltTemplateId;
            DefaultWhatsAppCountryCode = settings.DefaultWhatsAppCountryCode;

            var log = await _messagingService.GetMessageLogAsync(100);
            LogRows = new ObservableCollection<MessageLogRow>(log);
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            if (!int.TryParse(SmtpPort, out var port) || port is < 1 or > 65535)
            {
                ErrorMessage = "SMTP Port must be a number between 1 and 65535.";
                return;
            }

            await _messagingService.SaveSettingsAsync(new MessagingSetting
            {
                Id = 1,
                SmtpHost = SmtpHost.Trim(),
                SmtpPort = port,
                SmtpUser = SmtpUser.Trim(),
                SmtpPassword = SmtpPassword,
                FromAddress = FromAddress.Trim(),
                DltSmsSender = DltSmsSender.Trim(),
                DltTemplateId = DltTemplateId.Trim(),
                DefaultWhatsAppCountryCode = string.IsNullOrWhiteSpace(DefaultWhatsAppCountryCode)
                    ? "91" : DigitsOnly(DefaultWhatsAppCountryCode)
            });

            SuccessMessage = "Messaging settings saved.";
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Saving settings failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadInvoiceMessageAsync()
    {
        SuccessMessage = string.Empty;
        try
        {
            var (message, error, _) = await _messagingService.BuildInvoiceMessageByReferenceAsync(ReferenceInvoice);
            if (error != null)
            {
                ErrorMessage = error;
                return;
            }
            MessageText = message ?? string.Empty;
            ErrorMessage = string.Empty;
            SuccessMessage = "Invoice message loaded — edit it before sending if needed.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SendOpenAsync()
    {
        SuccessMessage = string.Empty;
        try
        {
            var reference = string.IsNullOrWhiteSpace(ReferenceInvoice) ? null : ReferenceInvoice.Trim();
            var recipient = Recipient.Trim();
            var (ok, status, detail) = SelectedChannel switch
            {
                "SMS" => await _messagingService.SendSmsAsync(recipient, MessageText, reference),
                "WhatsApp" => await _messagingService.SendWhatsAppAsync(recipient, MessageText, reference),
                _ => await _messagingService.SendEmailAsync(recipient, SubjectFromReference(reference), MessageText, reference)
            };

            if (ok)
            {
                SuccessMessage = $"{SelectedChannel}: {status} — {detail}";
                ErrorMessage = string.Empty;
            }
            else
            {
                ErrorMessage = $"{SelectedChannel}: {status} — {detail}";
            }

            var log = await _messagingService.GetMessageLogAsync(100);
            LogRows = new ObservableCollection<MessageLogRow>(log);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private static string SubjectFromReference(string? reference) =>
        string.IsNullOrWhiteSpace(reference) ? "Message from your store" : $"{reference} — message from your store";

    private static string DigitsOnly(string? text) =>
        text == null ? string.Empty : new string(text.Where(char.IsDigit).ToArray());
}
