using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// INT-01: the unified address book - one row per customer and per supplier, with the
/// receivable / payable the ACC-05 and RPT-09 reports already show, vCard export for a
/// phone address book, and preview-then-commit CSV import through the party services.
/// </summary>
public partial class ContactsViewModel : BaseViewModel
{
    private readonly IContactService _contactService;

    /// <summary>Kept after a preview so Commit re-reads the very same file.</summary>
    private string? _selectedCsvPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredContacts))]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(RowsMessage))]
    private ObservableCollection<ContactRow> _contacts = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredContacts))]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(RowsMessage))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredContacts))]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(RowsMessage))]
    private string _selectedPartyType = ContactParties.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredContacts))]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(RowsMessage))]
    private bool _dataIssuesOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ContactRow? _selectedContact;

    // --- KPI cards ---
    [ObservableProperty] private int _totalContacts;
    [ObservableProperty] private int _customerCount;
    [ObservableProperty] private int _supplierCount;
    [ObservableProperty] private int _missingPhoneCount;
    [ObservableProperty] private int _missingEmailCount;
    [ObservableProperty] private int _duplicatePhoneCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutstandingMessage))]
    private decimal _totalReceivable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutstandingMessage))]
    private decimal _totalPayable;

    // --- CSV import ---
    [ObservableProperty] private ObservableCollection<ContactImportRow> _previewRows = new();
    [ObservableProperty] private int _importReady;
    [ObservableProperty] private int _importSkipped;
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private string _selectedFile = string.Empty;

    [ObservableProperty] private string _successMessage = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _warningMessage = string.Empty;
    [ObservableProperty] private bool _isLoading;

    public ContactsViewModel(IContactService contactService) => _contactService = contactService;

    public List<string> PartyTypes => _contactService.PartyTypes;

    public bool HasSelection => SelectedContact != null;

    public bool HasRows => FilteredContacts.Any();

    public bool IsEmpty => !HasRows;

    public IEnumerable<ContactRow> FilteredContacts =>
        Contacts.Where(Matches);

    public string RowsMessage
    {
        get
        {
            var shown = FilteredContacts.Count();
            if (Contacts.Count == 0)
                return "No contacts yet - customers and suppliers appear here as soon as they exist on their master screens.";
            if (shown == 0)
                return $"No contact matches this search / filter ({Contacts.Count} in the book). Clear the search or pick 'All'.";
            if (shown == Contacts.Count)
                return $"All {Contacts.Count} contact(s) - {CustomerCount} customer(s), {SupplierCount} supplier(s).";
            return $"Showing {shown} of {Contacts.Count} contact(s).";
        }
    }

    public string OutstandingMessage =>
        $"Receivable from customers ₹{TotalReceivable:N2} · Payable to suppliers ₹{TotalPayable:N2} (same figures as Receivables / Payables)";

    private bool Matches(ContactRow row)
    {
        if (DataIssuesOnly && !row.HasIssues) return false;
        if (SelectedPartyType != ContactParties.All && row.PartyType != SelectedPartyType) return false;

        var term = SearchText?.Trim() ?? string.Empty;
        if (term.Length == 0) return true;

        return Contains(row.Name, term)
            || Contains(row.Phone, term)
            || Contains(row.GSTIN, term)
            || Contains(row.Email, term);
    }

    private static bool Contains(string? value, string term) =>
        !string.IsNullOrEmpty(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    /// <summary>Called from the view's Loaded hook (house pattern).</summary>
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var rows = await _contactService.GetContactsAsync();
            Contacts = new ObservableCollection<ContactRow>(rows.Select(ContactRow.Wrap));

            TotalContacts = rows.Count;
            CustomerCount = rows.Count(r => r.PartyType == ContactParties.Customer);
            SupplierCount = rows.Count(r => r.PartyType == ContactParties.Supplier);
            MissingPhoneCount = rows.Count(r => r.MissingPhone);
            MissingEmailCount = rows.Count(r => r.MissingEmail);
            DuplicatePhoneCount = rows.Count(r => r.DuplicatePhone);
            TotalReceivable = rows.Where(r => r.PartyType == ContactParties.Customer).Sum(r => r.Outstanding);
            TotalPayable = rows.Where(r => r.PartyType == ContactParties.Supplier).Sum(r => r.Outstanding);

            ErrorMessage = _contactService.LastValidationError ?? string.Empty;
            WarningMessage = string.Join(Environment.NewLine, _contactService.Warnings);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Loading the address book failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDataAsync();

    /// <summary>"Open" row action: shows the full record in the detail card together with the
    /// master screen that owns it - this screen never edits a party, so the book cannot drift.</summary>
    [RelayCommand]
    private void OpenPartyRecord(ContactRow? row)
    {
        if (row == null)
        {
            ErrorMessage = "Click a contact row first.";
            return;
        }

        SelectedContact = row;
        ErrorMessage = string.Empty;
        SuccessMessage = $"{row.Name} ({row.PartyType} #{row.PartyId}) - {row.EditHint}";
    }

    [RelayCommand]
    private async Task ExportVCardAsync(ContactRow? row)
    {
        if (row == null)
        {
            ErrorMessage = "Click the vCard button on a contact row.";
            return;
        }

        await RunExportAsync(() => _contactService.ExportVCardAsync(row.Contact),
            $"vCard of {row.Name} saved - open it on the phone to add the contact to its address book.");
    }

    [RelayCommand]
    private async Task ExportSelectedAsync()
    {
        var picked = Contacts.Where(c => c.IsSelected).Select(c => c.Contact).ToList();
        if (picked.Count == 0)
        {
            ErrorMessage = "Tick at least one row, or use 'Export all as vCard'.";
            SuccessMessage = string.Empty;
            return;
        }

        await RunExportAsync(() => _contactService.ExportVCardsAsync(picked),
            $"{picked.Count} ticked contact(s) written to one .vcf - import it on the phone to load the whole list at once.");
    }

    [RelayCommand]
    private async Task ExportAllAsync()
    {
        var all = Contacts.Select(c => c.Contact).ToList();
        if (all.Count == 0)
        {
            ErrorMessage = "The address book is empty - add a customer or supplier first.";
            SuccessMessage = string.Empty;
            return;
        }

        await RunExportAsync(() => _contactService.ExportVCardsAsync(all),
            $"All {all.Count} contact(s) written to one .vcf - it imports into an Android or iOS address book.");
    }

    /// <summary>Same SaveFileDialog approach as SEC-06's Tally export destination picker.</summary>
    [RelayCommand]
    private async Task SaveVCardAsAsync()
    {
        var rows = Contacts.Where(c => c.IsSelected).Select(c => c.Contact).ToList();
        if (rows.Count == 0)
            rows = FilteredContacts.Select(c => c.Contact).ToList();
        if (rows.Count == 0)
        {
            ErrorMessage = "There is nothing to save - tick rows or clear the filter first.";
            SuccessMessage = string.Empty;
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save the vCard file",
            FileName = $"AddressBook_{DateTime.Today:yyyyMMdd}.vcf",
            Filter = "vCard address book (*.vcf)|*.vcf|All files (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dialog.ShowDialog() != true) return;

        await RunExportAsync(() => _contactService.SaveVCardsAsync(rows, dialog.FileName),
            $"{rows.Count} contact(s) saved to the chosen file.");
    }

    private async Task RunExportAsync(Func<Task<string>> export, string successText)
    {
        IsLoading = true;
        try
        {
            var path = await export();
            if (string.IsNullOrEmpty(path))
            {
                SuccessMessage = string.Empty;
                ErrorMessage = _contactService.LastValidationError ?? "The export did not run.";
                return;
            }

            ErrorMessage = string.Empty;
            SuccessMessage = $"{successText} File: {path}";
            WarningMessage = string.Join(Environment.NewLine, _contactService.Warnings);
        }
        catch (Exception ex)
        {
            SuccessMessage = string.Empty;
            ErrorMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ------------------------------------------------------------------ CSV import

    /// <summary>OpenFile picker; ShowDialog in the VM is allowed in this project (STK-12 does it).</summary>
    [RelayCommand]
    private async Task PreviewCsvAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a contacts CSV file",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        IsLoading = true;
        try
        {
            _selectedCsvPath = dialog.FileName;
            SelectedFile = dialog.FileName;
            var result = await _contactService.PreviewCsvAsync(dialog.FileName);

            PreviewRows = new ObservableCollection<ContactImportRow>(
                result.SampleRows ?? Enumerable.Empty<ContactImportRow>());
            ImportReady = result.Imported;
            ImportSkipped = result.Skipped;
            HasPreview = true;

            ErrorMessage = string.Join(Environment.NewLine, result.Errors);
            WarningMessage = string.Join(Environment.NewLine, _contactService.Warnings);
            SuccessMessage = result.Skipped == 0
                ? $"{result.Imported} row(s) ready to import. Check the preview, then Commit Import."
                : $"Preview: {result.Imported} ready, {result.Skipped} skipped - the Skipped column says why. Nothing has been written yet.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Preview failed: {ex.Message}";
            SuccessMessage = string.Empty;
            HasPreview = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CommitCsvAsync()
    {
        if (!HasPreview || string.IsNullOrEmpty(_selectedCsvPath))
        {
            ErrorMessage = "Browse & Preview a CSV file first.";
            return;
        }

        IsLoading = true;
        try
        {
            var result = await _contactService.CommitCsvAsync(_selectedCsvPath);

            ImportReady = result.Imported;
            ImportSkipped = result.Skipped;
            ErrorMessage = _contactService.LastValidationError
                ?? string.Join(Environment.NewLine, result.Errors);
            WarningMessage = string.Join(Environment.NewLine, _contactService.Warnings);
            SuccessMessage = result.Imported > 0
                ? $"Imported {result.Imported} contact(s) ({result.Skipped} skipped). Created: " +
                  $"{string.Join(", ", result.Created.Take(8))}{(result.Created.Count > 8 ? " ..." : string.Empty)}"
                : $"Nothing imported - {result.Skipped} row(s) were skipped.";
            HasPreview = false; // a fresh preview is required before another commit
            PreviewRows.Clear();

            if (result.Imported > 0) await LoadDataAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Import failed: {ex.Message}";
            SuccessMessage = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportTemplateAsync()
    {
        try
        {
            var path = await _contactService.ExportTemplateCsvAsync();
            SuccessMessage = $"Import template (with the column notes) saved to {path}";
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not write the template: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetImport()
    {
        _selectedCsvPath = null;
        SelectedFile = string.Empty;
        HasPreview = false;
        ImportReady = ImportSkipped = 0;
        PreviewRows = new ObservableCollection<ContactImportRow>();
        SuccessMessage = "Import cleared.";
        ErrorMessage = string.Empty;
    }

    partial void OnSelectedPartyTypeChanged(string value) => ClearSelectionIfFilteredOut();

    partial void OnDataIssuesOnlyChanged(bool value) => ClearSelectionIfFilteredOut();

    partial void OnSearchTextChanged(string value) => ClearSelectionIfFilteredOut();

    /// <summary>FilteredContacts / HasRows / IsEmpty / RowsMessage are already re-raised by the
    /// NotifyPropertyChangedFor attributes on the backing fields; these hooks only keep the
    /// detail card honest - a row that has just been filtered out cannot stay selected.</summary>
    private void ClearSelectionIfFilteredOut()
    {
        if (SelectedContact != null && !FilteredContacts.Contains(SelectedContact))
            SelectedContact = null;
    }
}

/// <summary>
/// INT-01: selection wrapper around the ContactInfo DTO - the DTO stays plain (one source
/// of truth, no persistence), the grid needs an INotifyPropertyChanged row for its tickbox.
/// </summary>
public partial class ContactRow : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public ContactRow(ContactInfo contact) => Contact = contact;

    public static ContactRow Wrap(ContactInfo contact) => new(contact);

    public ContactInfo Contact { get; }

    public string PartyType => Contact.PartyType;
    public int PartyId => Contact.PartyId;
    public string Key => Contact.Key;
    public string Name => Contact.Name;
    public string? Phone => Contact.Phone;
    public string? Email => Contact.Email;
    public string? GSTIN => Contact.GSTIN;
    public string? State => Contact.State;
    public string? Group => Contact.Group;
    public string? Address => Contact.Address;
    public bool IsActive => Contact.IsActive;
    public bool HasActiveFlag => Contact.HasActiveFlag;
    public decimal Outstanding => Contact.Outstanding;
    public int DocumentCount => Contact.DocumentCount;
    public DateTime? LastDocumentDate => Contact.LastDocumentDate;
    public string WhatsAppNumber => Contact.WhatsAppNumber;
    public bool MissingPhone => Contact.MissingPhone;
    public bool MissingEmail => Contact.MissingEmail;
    public bool DuplicatePhone => Contact.DuplicatePhone;
    public bool HasIssues => Contact.HasIssues;
    public string IssueText => Contact.IssueText;
    public string ActiveLabel => Contact.ActiveLabel;
    public string TermsLabel => Contact.TermsLabel;
    public string OutstandingLabel => Contact.OutstandingLabel;
    public string DocumentsLabel => Contact.DocumentsLabel;
    public string LastDocumentLabel => Contact.LastDocumentLabel;
    public string ChannelText => Contact.ChannelText;
    public string EditHint => Contact.EditHint;
    public string CreatedText => Contact.CreatedAt.ToLocalTime().ToString("dd-MMM-yyyy");
    public string LastDocumentText => Contact.LastDocumentDate == null
        ? "no document yet"
        : Contact.LastDocumentDate.Value.ToLocalTime().ToString("dd-MMM-yyyy");
}
