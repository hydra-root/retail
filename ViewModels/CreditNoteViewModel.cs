using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// INV-11: credit/debit note register + return-against-invoice creation.
/// The note is linked to an open/pending invoice (customer and remaining value
/// come from it) or to a free-standing customer for the manual path. Items pick
/// real products so ProductId/HSN/rate/tax flow through to stock and GSTR-1 CDNR.
/// </summary>
public partial class CreditNoteViewModel : BaseViewModel
{
    private readonly ICreditNoteService _creditNoteService;
    private readonly IInvoiceService _invoiceService;
    private readonly IProductService _productService;
    private readonly ICustomerService _customerService;

    [ObservableProperty]
    private ObservableCollection<CreditNote> _creditNotes = new();

    [ObservableProperty]
    private CreditNote _selectedCreditNote = null!;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _typeFilter = "All";

    [ObservableProperty]
    private string _newCustomerName = string.Empty;

    [ObservableProperty]
    private string _newReason = string.Empty;

    [ObservableProperty]
    private string _newType = "Credit"; // Credit or Debit

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    // Pickers
    [ObservableProperty]
    private ObservableCollection<Invoice> _pendingInvoices = new();

    [ObservableProperty]
    private Invoice? _selectedInvoice = default!;

    [ObservableProperty]
    private ObservableCollection<Customer> _customers = new();

    [ObservableProperty]
    private Customer? _selectedCustomer = default!;

    [ObservableProperty]
    private ObservableCollection<Product> _products = new();

    [ObservableProperty]
    private Product? _selectedProduct = default!;

    // New credit note form
    [ObservableProperty]
    private ObservableCollection<CreditNoteItem> _newItems = new();

    [ObservableProperty]
    private int _newProductQty = 1;

    [ObservableProperty]
    private decimal _newProductRate;

    [ObservableProperty]
    private decimal _newProductTax;

    [ObservableProperty]
    private decimal _newSubTotal;

    [ObservableProperty]
    private decimal _newTaxAmount;

    [ObservableProperty]
    private decimal _newTotalAmount;

    public List<string> NoteTypes { get; } = new() { "Credit", "Debit" };

    public List<string> TypeFilters { get; } = new() { "All", "Credit", "Debit" };

    /// <summary>Guidance line under the invoice picker (INV-11: remaining value cap).</summary>
    public string SelectedInvoiceHint =>
        SelectedInvoice == null
            ? "No invoice linked — pick a customer (or type a walk-in name); the note then has no GSTR-1 original-invoice reference."
            : $"{SelectedInvoice.CustomerName ?? "Customer"} · total {SelectedInvoice.TotalAmount:C} · " +
              $"remaining {SelectedInvoice.TotalAmount - SelectedInvoice.PaidAmount - SelectedInvoice.WriteOffAmount:C}. " +
              "Customer is taken from this invoice.";

    public CreditNoteViewModel(ICreditNoteService creditNoteService, IInvoiceService invoiceService,
        IProductService productService, ICustomerService customerService)
    {
        _creditNoteService = creditNoteService;
        _invoiceService = invoiceService;
        _productService = productService;
        _customerService = customerService;
    }

    partial void OnSelectedInvoiceChanged(Invoice? value) => OnPropertyChanged(nameof(SelectedInvoiceHint));

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredCreditNotes));

    partial void OnTypeFilterChanged(string value) => OnPropertyChanged(nameof(FilteredCreditNotes));

    /// <summary>Picking a product pre-fills HSN/rate/tax; the rate stays editable (negotiated return).</summary>
    partial void OnSelectedProductChanged(Product? value)
    {
        if (value == null) return;
        NewProductRate = value.Rate;
        NewProductTax = value.TaxPercentage;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var creditNotes = await _creditNoteService.GetAllCreditNotesAsync();
            CreditNotes = new ObservableCollection<CreditNote>(creditNotes);

            Products = new ObservableCollection<Product>(await _productService.GetAllProductsAsync());
            Customers = new ObservableCollection<Customer>(await _customerService.GetAllCustomersAsync());

            // INV-11: only real bills take CDNR notes — estimates are filtered out of the picker.
            var pending = await _invoiceService.GetPendingInvoicesAsync();
            PendingInvoices = new ObservableCollection<Invoice>(pending.Where(i => !i.IsEstimate));

            // Reset pickers if a selected entity was removed/deleted.
            if (SelectedProduct != null && Products.All(p => p.Id != SelectedProduct.Id)) SelectedProduct = null;
            if (SelectedCustomer != null && Customers.All(c => c.Id != SelectedCustomer.Id)) SelectedCustomer = null;
            if (SelectedInvoice != null && PendingInvoices.All(i => i.Id != SelectedInvoice.Id)) SelectedInvoice = null;

            ErrorMessage = string.Empty;
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

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private void AddItemToCreditNote()
    {
        if (SelectedProduct == null)
        {
            ErrorMessage = "Please pick a product to add.";
            return;
        }
        if (NewProductQty <= 0)
        {
            ErrorMessage = "Quantity must be greater than zero.";
            return;
        }
        if (NewProductRate < 0)
        {
            ErrorMessage = "Rate cannot be negative.";
            return;
        }

        var item = new CreditNoteItem
        {
            ProductId = SelectedProduct.Id,
            ProductName = SelectedProduct.Name,
            HSNCode = SelectedProduct.HSNCode,
            Quantity = NewProductQty,
            Rate = NewProductRate,
            TaxPercentage = NewProductTax,
            Amount = NewProductQty * NewProductRate,
            TaxAmount = (NewProductQty * NewProductRate) * (NewProductTax / 100m)
        };

        NewItems.Add(item);
        RecalculateTotals();

        NewProductQty = 1;
        // Re-offer the product's own rate/tax for the next line.
        NewProductRate = SelectedProduct.Rate;
        NewProductTax = SelectedProduct.TaxPercentage;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void RemoveItemFromCreditNote(CreditNoteItem item)
    {
        NewItems.Remove(item);
        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        NewSubTotal = NewItems.Sum(i => i.Amount);
        NewTaxAmount = NewItems.Sum(i => i.TaxAmount);
        NewTotalAmount = NewSubTotal + NewTaxAmount;
    }

    [RelayCommand]
    private async Task CreateCreditNoteAsync()
    {
        SuccessMessage = string.Empty;

        if (NewItems.Count == 0)
        {
            ErrorMessage = "Please add at least one item.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewReason))
        {
            ErrorMessage = "Reason is required.";
            return;
        }

        if (SelectedInvoice == null && SelectedCustomer == null && string.IsNullOrWhiteSpace(NewCustomerName))
        {
            ErrorMessage = "Pick an original invoice or a customer (or type a walk-in customer name).";
            return;
        }

        // INV-11: linking an invoice carries CustomerId/CustomerName through to the
        // customer ledger (RPT-10/PAY-07 match on CustomerId) and GSTR-1 CDNR.
        var creditNote = new CreditNote
        {
            CreditNoteDate = System.DateTime.UtcNow,
            Type = NewType,
            OriginalInvoiceId = SelectedInvoice?.Id,
            OriginalInvoiceNumber = SelectedInvoice?.InvoiceNumber,
            CustomerId = SelectedInvoice?.CustomerId ?? SelectedCustomer?.Id,
            CustomerName = SelectedInvoice?.CustomerName ?? SelectedCustomer?.Name ?? NewCustomerName,
            Reason = NewReason,
            Status = "Issued",
            Items = NewItems.ToList()
        };

        var success = await _creditNoteService.CreateCreditNoteAsync(creditNote);
        if (success)
        {
            NewItems.Clear();
            NewCustomerName = string.Empty;
            NewReason = string.Empty;
            NewType = "Credit";
            SelectedInvoice = null;
            SelectedCustomer = null;
            SelectedProduct = null;
            NewProductQty = 1;
            NewProductRate = 0;
            NewProductTax = 0;
            NewSubTotal = 0;
            NewTaxAmount = 0;
            NewTotalAmount = 0;
            ErrorMessage = string.Empty;
            SuccessMessage = $"{creditNote.Type} note {creditNote.CreditNoteNumber} issued for {creditNote.TotalAmount:C}.";
            if (_creditNoteService.StockPolicyWarnings.Count > 0)
                SuccessMessage += " Warning: " + string.Join(" ", _creditNoteService.StockPolicyWarnings);
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = _creditNoteService.LastValidationError ?? "Failed to create the credit/debit note.";
        }
    }

    [RelayCommand]
    private async Task CancelSelectedNoteAsync()
    {
        SuccessMessage = string.Empty;
        if (SelectedCreditNote == null)
        {
            ErrorMessage = "Select a note in the register first.";
            return;
        }
        if (SelectedCreditNote.Status == "Cancelled")
        {
            ErrorMessage = $"Note {SelectedCreditNote.CreditNoteNumber} is already cancelled.";
            return;
        }

        var number = SelectedCreditNote.CreditNoteNumber;
        var success = await _creditNoteService.CancelNoteAsync(SelectedCreditNote.Id);
        if (success)
        {
            ErrorMessage = string.Empty;
            SuccessMessage = $"Note {number} cancelled; its stock effect was reversed (still visible to GSTR-1 CDNR as cancelled).";
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = _creditNoteService.LastValidationError ?? "Failed to cancel the note.";
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedNoteAsync()
    {
        SuccessMessage = string.Empty;
        if (SelectedCreditNote == null)
        {
            ErrorMessage = "Select a note in the register first.";
            return;
        }

        var number = SelectedCreditNote.CreditNoteNumber;
        var success = await _creditNoteService.DeleteCreditNoteAsync(SelectedCreditNote.Id);
        if (success)
        {
            ErrorMessage = string.Empty;
            SuccessMessage = $"Note {number} deleted; its stock effect was reversed.";
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = _creditNoteService.LastValidationError ?? "Failed to delete the note.";
        }
    }

    public IEnumerable<CreditNote> FilteredCreditNotes =>
        CreditNotes.Where(c =>
            (TypeFilter == "All" || c.Type == TypeFilter) &&
            (string.IsNullOrWhiteSpace(SearchText)
                || c.CreditNoteNumber.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase)
                || (c.CustomerName != null && c.CustomerName.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase))
                || (c.Reason != null && c.Reason.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase))
                || (c.OriginalInvoiceNumber != null && c.OriginalInvoiceNumber.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase))));
}
