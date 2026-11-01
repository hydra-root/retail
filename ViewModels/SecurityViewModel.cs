using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// SEC-03: "Security &amp; Encryption" screen. Reports whole-database state honestly, lists which
/// stored secrets are protected vs plaintext (counts and labels only — never values), runs the
/// safe "protect stored secrets now" action, and keeps the dangerous "encrypt database" action
/// disabled unless a prior backup exists and the operator explicitly acknowledges the risk — at
/// which point it still declines to auto-re-key the live file and shows the manual plan instead.
/// </summary>
public partial class SecurityViewModel : BaseViewModel
{
    private readonly ISecurityService _securityService;

    [ObservableProperty]
    private ObservableCollection<SecretStatusRow> _inventory = new();

    [ObservableProperty]
    private string _databaseStateLine = "Checking app.db…";

    [ObservableProperty]
    private string _databaseProbeNote = string.Empty;

    [ObservableProperty]
    private string _masterPasswordState = string.Empty;

    [ObservableProperty]
    private string _backupStateLine = string.Empty;

    [ObservableProperty]
    private bool _backupExists;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAttemptEncrypt))]
    private bool _singleUserConfirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAttemptEncrypt))]
    private bool _riskAcknowledged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlaintextSecrets))]
    private int _protectedSecretCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlaintextSecrets))]
    private int _plaintextSecretCount;

    [ObservableProperty]
    private string _migrationPlanText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public SecurityViewModel(ISecurityService securityService)
    {
        _securityService = securityService;
        // Seed the (long, static) design document once so it is present even before a refresh.
        MigrationPlanText = DatabaseEncryptionPlan.DescribeMigrationPlan();
    }

    /// <summary>True when at least one stored secret is unprotected and can be protected today.</summary>
    public bool HasPlaintextSecrets => PlaintextSecretCount > 0;

    /// <summary>Control gate for the whole-database button: needs a backup AND both acknowledgements.</summary>
    public bool CanAttemptEncrypt => BackupExists && SingleUserConfirmed && RiskAcknowledged;

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            Inventory = new ObservableCollection<SecretStatusRow>(await _securityService.GetSecretInventoryAsync());
            var db = await _securityService.GetDatabaseStatusAsync();

            DatabaseStateLine = db.ProbeSaysEncrypted
                ? "app.db could not be opened without a key — but see the caveat below; this is a hint, not proof."
                : "app.db opens with no key: the database file is plaintext today.";
            DatabaseProbeNote = db.ProbeNote;
            MasterPasswordState = db.MasterPasswordState;
            BackupExists = db.BackupExists;
            BackupStateLine = db.BackupCount > 0
                ? $"{db.BackupCount} prior backup(s) detected — the encrypt-database action is allowed to be considered."
                : "No backup detected. Encrypting the database will stay disabled until you make one.";
            MigrationPlanText = DatabaseEncryptionPlan.DescribeMigrationPlan();

            Recount();
        }
        catch (System.Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Recount()
    {
        ProtectedSecretCount = Inventory.Count(r => r.IsSet && r.StateLabel.StartsWith("Encrypted", System.StringComparison.Ordinal));
        PlaintextSecretCount = Inventory.Count(r => r.IsPlaintext);
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();

    [RelayCommand]
    private async Task ProtectSecretsAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            var report = await _securityService.ProtectStoredSecretsAsync();
            StatusMessage = report.Summary;
            await LoadDataAsync();
        }
        catch (System.Exception ex)
        {
            ErrorMessage = $"Protecting stored secrets failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task EncryptDatabaseAsync()
    {
        ErrorMessage = string.Empty;

        // Loud guard: even if a backup exists, the automatic path is intentionally NOT performed.
        if (!BackupExists)
        {
            ErrorMessage = "Refused: no backup found. Make a verified backup of app.db before encryption " +
                           "is even discussed.";
            return;
        }
        if (!CanAttemptEncrypt)
        {
            ErrorMessage = "Refused: confirm this is the only user of the database and tick the " +
                           "acknowledgement that re-keying is not reversible without your backup.";
            return;
        }

        // The safe outcome: document the manual, backup-first procedure instead of touching app.db.
        await System.Threading.Tasks.Task.CompletedTask;
        StatusMessage =
            "Automatic whole-database encryption is deliberately not shipped, because an incorrect " +
            "re-key of your live app.db is unrecoverable. Follow the export-and-re-key procedure below " +
            "as an explicit, single-user maintenance step (keep the backup until a full session runs " +
            "against the encrypted file). No data was changed.";
    }
}
