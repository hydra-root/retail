using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

namespace RetailBillingApp.ViewModels;

public partial class InvoicesViewModel : BaseViewModel
{
    private readonly IInvoiceService _invoiceService;
    private readonly IProductService _productService;
    private readonly ICustomerService _customerService;
    private readonly IStorePricingService _storePricingService;
    private readonly ICriticalChangeGuard _criticalChangeGuard;
    private readonly IOfflineQueueService _offlineQueue;
    private readonly ICreditSaleService _creditSaleService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredInvoices))]
    [NotifyPropertyChangedFor(nameof(RegisterCount))]
    private ObservableCollection<Invoice> _invoices = new();

    [ObservableProperty]
    private Invoice _selectedInvoice = null!;

    /// <summary>PAY-06 / STK-01: product master picker — a line must come from the master so stock moves and party pricing applies.</summary>
    [ObservableProperty]
    private ObservableCollection<Product> _products = new();

    /// <summary>PAY-04: customer master picker.</summary>
    [ObservableProperty]
    private ObservableCollection<Customer> _customers = new();

    [ObservableProperty]
    private Product? _selectedProduct;

    [ObservableProperty]
    private Customer? _selectedCustomer;

    /// <summary>PAY-06: which price list (if any) supplied the rate for the line just added.</summary>
    [ObservableProperty]
    private string _priceSource = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredInvoices))]
    [NotifyPropertyChangedFor(nameof(RegisterCount))]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    private string _searchText = string.Empty;

    // INV-13: register filters
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredInvoices))]
    [NotifyPropertyChangedFor(nameof(RegisterCount))]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    private DateTime? _filterFrom;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredInvoices))]
    [NotifyPropertyChangedFor(nameof(RegisterCount))]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    private DateTime? _filterTo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredInvoices))]
    [NotifyPropertyChangedFor(nameof(RegisterCount))]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    private string _filterStatus = "All";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredInvoices))]
    [NotifyPropertyChangedFor(nameof(RegisterCount))]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    private string _filterType = "All";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredInvoices))]
    [NotifyPropertyChangedFor(nameof(RegisterCount))]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    private string _filterPaymentMode = "All";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredInvoices))]
    [NotifyPropertyChangedFor(nameof(RegisterCount))]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    private decimal? _filterMinAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredInvoices))]
    [NotifyPropertyChangedFor(nameof(RegisterCount))]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    private decimal? _filterMaxAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredInvoices))]
    [NotifyPropertyChangedFor(nameof(RegisterCount))]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    private bool _filterOutstandingOnly;

    /// <summary>INV-13: filter vocabularies for the register toolbar.</summary>
    public List<string> StatusFilters { get; } = new() { "All", "Paid", "Partial", "Pending", "Cancelled" };

    public List<string> TypeFilters { get; } = new() { "All", "GST Invoice", "Bill of Supply", "Estimate" };

    public List<string> PaymentModeFilters { get; } = new() { "All", "Cash", "Card", "UPI", "Credit", "Split" };

    public int RegisterCount => FilteredInvoices.Count();

    public bool HasActiveFilter =>
        !string.IsNullOrWhiteSpace(SearchText) || FilterFrom != null || FilterTo != null ||
        FilterStatus != "All" || FilterType != "All" || FilterPaymentMode != "All" ||
        FilterMinAmount != null || FilterMaxAmount != null || FilterOutstandingOnly;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _newIsBillOfSupply;

    [ObservableProperty]
    private bool _newIsEstimate;

    /// <summary>PAY-05: admin username for the credit-limit override (optional).</summary>
    [ObservableProperty]
    private string _overrideUsername = string.Empty;

    /// <summary>PAY-05: admin password for the credit-limit override (optional).</summary>
    [ObservableProperty]
    private string _overridePassword = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    /// <summary>STK-06/PAY-05: non-blocking warnings surfaced after a successful save.</summary>
    [ObservableProperty]
    private string _warningMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public List<string> PaymentModes { get; } = new() { "Cash", "Card", "UPI", "Credit" };

    // New invoice form - GST fields
    [ObservableProperty]
    private string _newCustomerName = string.Empty;

    [ObservableProperty]
    private string _newCustomerGSTIN = string.Empty;

    [ObservableProperty]
    private string _newPlaceOfSupply = string.Empty;

    [ObservableProperty]
    private string _newPaymentMode = "Cash";

    [ObservableProperty]
    private decimal _newPaidAmount;

    [ObservableProperty]
    private ObservableCollection<InvoiceItem> _newItems = new();

    // INV-04: invoice-level tax mode — inclusive prices include GST in the entered rate
    [ObservableProperty]
    private bool _draftTaxInclusive;

    partial void OnDraftTaxInclusiveChanged(bool value)
    {
        NewProductTaxInclusive = value;
        SuccessMessage = value
            ? "Draft is tax-inclusive: entered rates include GST (invoice-level switch)."
            : "Draft is tax-exclusive: GST is added on top of entered rates.";
    }

    /// <summary>INV-04: flip the whole existing draft between inclusive/exclusive,
    /// preserving each line's gross value (rate recomputed, taxes recalculated).</summary>
    [RelayCommand]
    private void ApplyTaxModeToAll()
    {
        if (NewItems.Count == 0) return;

        var isInterState = TaxCalculationEngine.IsInterState("07", NewPlaceOfSupply);
        foreach (var item in NewItems)
        {
            // Preserve the customer-facing gross across the flip:
            // inclusive: Total = q*R exactly → R = G/q
            // exclusive: Total = q*R*(1+tax%) → R = G/(q*(1+tax%))
            if (item.Quantity > 0)
            {
                var gross = item.Total;
                var taxFactor = 1m + ((item.TaxPercentage + item.CessRate) / 100m);
                item.TaxInclusive = DraftTaxInclusive;
                item.Rate = System.Math.Round(
                    DraftTaxInclusive ? gross / item.Quantity : gross / (item.Quantity * taxFactor), 2);
            }
            item.Amount = item.Rate * item.Quantity;

            var result = TaxCalculationEngine.CalculateItemTax(
                item.Amount, item.TaxPercentage, item.CessRate, isInterState, item.TaxInclusive);
            item.CGST = result.CGST;
            item.SGST = result.SGST;
            item.IGST = result.IGST;
            item.Cess = result.Cess;
            item.TaxAmount = result.TotalTax;
            item.Total = result.TotalAmount;
        }

        RecalculateTotals();
        SuccessMessage = $"All draft lines switched to {(DraftTaxInclusive ? "tax-inclusive" : "tax-exclusive")} pricing.";
        ErrorMessage = string.Empty;
    }

    [ObservableProperty]
    private string _newProductName = string.Empty;

    [ObservableProperty]
    private int _newProductQty = 1;

    [ObservableProperty]
    private decimal _newProductRate;

    [ObservableProperty]
    private decimal _newProductTax;

    [ObservableProperty]
    private decimal _newProductCessRate;

    [ObservableProperty]
    private bool _newProductTaxInclusive;

    [ObservableProperty]
    private decimal _newSubTotal;

    [ObservableProperty]
    private decimal _newCGST;

    [ObservableProperty]
    private decimal _newSGST;

    [ObservableProperty]
    private decimal _newIGST;

    [ObservableProperty]
    private decimal _newCess;

    [ObservableProperty]
    private decimal _newTaxAmount;

    [ObservableProperty]
    private decimal _newTotalAmount;

    public InvoicesViewModel(IInvoiceService invoiceService, IProductService productService, ICustomerService customerService,
        IStorePricingService storePricingService, ICriticalChangeGuard criticalChangeGuard,
        IOfflineQueueService offlineQueue, ICreditSaleService creditSaleService)
    {
        _invoiceService = invoiceService;
        _productService = productService;
        _customerService = customerService;
        _storePricingService = storePricingService;
        _criticalChangeGuard = criticalChangeGuard;
        _offlineQueue = offlineQueue;
        _creditSaleService = creditSaleService;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var invoices = await _invoiceService.GetAllInvoicesAsync();
        Invoices = new ObservableCollection<Invoice>(invoices);

        var products = await _productService.GetAllProductsAsync();
        Products = new ObservableCollection<Product>(products.OrderBy(p => p.Name));

        var customers = await _customerService.GetAllCustomersAsync();
        Customers = new ObservableCollection<Customer>(customers.OrderBy(c => c.Name));

        IsLoading = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    /// <summary>PAY-06 / STK-01: choosing a product pre-fills rate, tax and HSN, re-priced for the selected customer.</summary>
    partial void OnSelectedProductChanged(Product? value)
    {
        if (value == null) return;
        NewProductName = value.Name;
        NewProductRate = value.Rate;
        NewProductTax = value.TaxPercentage;
        _ = ApplyPartyPriceAsync();
    }

    /// <summary>PAY-04 / PAY-06: choosing a customer fills GSTIN and re-prices the pending line.</summary>
    partial void OnSelectedCustomerChanged(Customer? value)
    {
        if (value == null) return;
        NewCustomerName = value.Name;
        if (!string.IsNullOrWhiteSpace(value.GSTIN)) NewCustomerGSTIN = value.GSTIN;
        _ = ApplyPartyPriceAsync();
    }

    /// <summary>
    /// PAY-06: asks the price-list engine what this product costs for the chosen buyer
    /// (customer list, then group list, then the product master rate) and shows the source.
    /// </summary>
    private async System.Threading.Tasks.Task ApplyPartyPriceAsync()
    {
        if (SelectedProduct == null) return;

        // USR-03: store pricing sits in front of the party lists — the resolution walks
        // store overrides first (signed-in user's store), then the PAY-06 party chain.
        var price = await _storePricingService.ResolveAsync(
            SelectedProduct.Id, SelectedCustomer?.Id, SelectedCustomer?.CustomerGroup);

        if (price.Rate.HasValue) NewProductRate = price.Rate.Value;
        PriceSource = price.Rate.HasValue
            ? $"Rate from {price.Source}"
            : price.Source;
    }

    [RelayCommand]
    private async Task AddItemToInvoiceAsync()
    {
        if (SelectedProduct == null)
        {
            ErrorMessage = "Select a product from the catalogue — typed-in lines are not billed, so stock and party pricing would be skipped.";
            return;
        }

        if (NewProductQty <= 0)
        {
            ErrorMessage = "Quantity must be greater than zero.";
            return;
        }

        await ApplyPartyPriceAsync();

        // Determine inter-state based on place of supply (simplified - supplier state = "07")
        var isInterState = TaxCalculationEngine.IsInterState("07", NewPlaceOfSupply);

        var taxableValue = NewProductQty * NewProductRate;

        // Apply discount if any
        var taxableAfterDiscount = taxableValue;

        var itemResult = TaxCalculationEngine.CalculateItemTax(
            taxableAfterDiscount,
            NewProductTax,
            NewProductCessRate,
            isInterState,
            NewProductTaxInclusive);

        var item = new InvoiceItem
        {
            ProductId = SelectedProduct.Id,
            ProductName = SelectedProduct.Name,
            HSNCode = SelectedProduct.HSNCode,
            Quantity = NewProductQty,
            Rate = NewProductRate,
            TaxPercentage = NewProductTax,
            CessRate = NewProductCessRate,
            TaxInclusive = NewProductTaxInclusive,
            Amount = itemResult.TaxableValue,
            CGST = itemResult.CGST,
            SGST = itemResult.SGST,
            IGST = itemResult.IGST,
            Cess = itemResult.Cess,
            TaxAmount = itemResult.TotalTax,
            Total = itemResult.TotalAmount
        };

        NewItems.Add(item);
        RecalculateTotals();
        ErrorMessage = string.Empty;

        SelectedProduct = null;
        NewProductQty = 1;
        NewProductRate = 0;
        NewProductTax = 0;
        NewProductCessRate = 0;
        NewProductTaxInclusive = false;
    }

    [RelayCommand]
    private void RemoveItemFromInvoice(InvoiceItem item)
    {
        NewItems.Remove(item);
        RecalculateTotals();
    }

    /// <summary>
    /// SEC-05: the separate critical-change passcode. Returns true when the action may proceed.
    /// Runs BEFORE the INV-10 admin override, so the two stack instead of collide: this proves
    /// "whoever knows the shop secret is here", INV-10 still proves "an admin authorised it".
    /// </summary>
    private async Task<bool> ConfirmCriticalChangeAsync(CriticalChangeKind kind, string action, string? target = null)
    {
        if (!await _criticalChangeGuard.IsRequiredAsync(kind))
            return true;                       // master switch off, or this gate not armed

        string? refusal = null;
        DateTime? lockedUntilUtc = null;
        int remaining = 0;

        while (true)
        {
            var prompt = Views.CriticalChangeDialog.Show(
                action, CriticalChangeSetting.DescribeGate(kind), refusal, lockedUntilUtc, remaining);
            if (prompt == null) return false;  // operator cancelled

            var outcome = await _criticalChangeGuard.VerifyAsync(kind, prompt.Passcode, prompt.Reason, target);
            if (outcome.Allowed)
            {
                ErrorMessage = string.Empty;
                return true;
            }

            refusal = outcome.Message;
            lockedUntilUtc = outcome.LockedUntilUtc;
            remaining = outcome.RemainingAttempts;
        }
    }

    /// <summary>INV-10: delete with rules - estimates free, live invoices need admin override,
    /// paid or locked-period invoices are refused by the service with a clear reason.</summary>
    [RelayCommand]
    private async Task DeleteInvoiceAsync()
    {
        if (SelectedInvoice == null)
        {
            ErrorMessage = "Select an invoice to delete.";
            return;
        }

        var invoice = SelectedInvoice;
        var confirm = System.Windows.MessageBox.Show(
            $"Delete {invoice.InvoiceNumber}?" + (invoice.IsEstimate
                ? " (estimate - stock never moved)"
                : " Live invoices require an admin override."),
            "Confirm Delete",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        // SEC-05: separate passcode for deletes, asked before the INV-10 override is consulted.
        if (!await ConfirmCriticalChangeAsync(CriticalChangeKind.InvoiceDelete,
                $"Delete {(invoice.IsEstimate ? "estimate" : "invoice")} {invoice.InvoiceNumber} " +
                $"({invoice.TotalAmount:C}, dated {invoice.InvoiceDate:dd-MMM-yyyy})",
                invoice.InvoiceNumber))
            return;

        var ok = await _invoiceService.DeleteInvoiceAsync(invoice.Id, OverrideUsername, OverridePassword);
        if (ok)
        {
            SuccessMessage = $"Invoice {invoice.InvoiceNumber} deleted; stock was restored." +
                (invoice.IsEstimate ? "" : " (admin override used)");
            ErrorMessage = string.Empty;
            OverridePassword = string.Empty;
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = _invoiceService.LastValidationError ?? "Delete failed.";
        }
    }

    [RelayCommand]
    private async Task ConvertEstimateToInvoiceAsync()
    {
        if (SelectedInvoice == null) return;

        if (!SelectedInvoice.IsEstimate)
        {
            ErrorMessage = "Selected document is not an estimate.";
            return;
        }

        // SEC-05: conversion turns a quotation into a real GST document, regenerates the number
        // and moves stock. This gate ships DISARMED (RequireForEstimateConversion = false), so it
        // is a no-op until an admin arms it.
        if (!await ConfirmCriticalChangeAsync(CriticalChangeKind.EstimateConversion,
                $"Convert estimate {SelectedInvoice.InvoiceNumber} into an invoice ({SelectedInvoice.TotalAmount:C})",
                SelectedInvoice.InvoiceNumber))
            return;

        // Keep a name snapshot before load refresh
        var number = SelectedInvoice.InvoiceNumber;
        var ok = await _invoiceService.ConvertEstimateToInvoiceAsync(SelectedInvoice.Id);
        if (ok)
        {
            ErrorMessage = string.Empty;
            SuccessMessage = $"Estimate {number} converted to an invoice (stock moved, new invoice number generated).";
            if (_invoiceService.StockPolicyWarnings.Count > 0)
                WarningMessage = "Warning: " + string.Join(" ", _invoiceService.StockPolicyWarnings);
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = _invoiceService.LastValidationError ?? "Conversion failed.";
        }
    }

    [RelayCommand]
    private async Task WriteOffRemainingAsync()
    {
        if (SelectedInvoice == null) return;

        if (SelectedInvoice.IsEstimate)
        {
            ErrorMessage = "Estimates cannot be written off.";
            return;
        }

        var remaining = SelectedInvoice.TotalAmount - SelectedInvoice.PaidAmount - SelectedInvoice.WriteOffAmount;
        if (remaining <= 0)
        {
            ErrorMessage = "Nothing left to write off on this invoice.";
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Write off {remaining:C} remaining for invoice {SelectedInvoice.InvoiceNumber}?\nThis posts a Bad Debts journal entry and cannot be undone.",
            "Confirm Write-off",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        // SEC-05: a write-off closes a receivable without cash and posts Bad Debts.
        if (!await ConfirmCriticalChangeAsync(CriticalChangeKind.WriteOff,
                $"Write off {remaining:C} remaining on invoice {SelectedInvoice.InvoiceNumber} " +
                $"for {(string.IsNullOrWhiteSpace(SelectedInvoice.CustomerName) ? "walk-in" : SelectedInvoice.CustomerName)}",
                SelectedInvoice.InvoiceNumber))
            return;

        // SEC-05: the LockedPeriodEdit gate. There is no separate invoice-edit UI yet
        // (no UpdateInvoiceAsync caller exists), so this register flow — which mutates
        // the same financial fields an edit would — demands the gate per policy
        // (RequireForLockedPeriodEdit), stacking after the write-off gate above.
        if (!await ConfirmCriticalChangeAsync(CriticalChangeKind.LockedPeriodEdit,
                $"Edit invoice {SelectedInvoice.InvoiceNumber} " +
                $"(write off {remaining:C}, dated {SelectedInvoice.InvoiceDate:dd-MMM-yyyy})",
                SelectedInvoice.InvoiceNumber))
            return;

        var ok = await _invoiceService.WriteOffInvoiceAsync(SelectedInvoice.Id, remaining);
        if (ok)
        {
            ErrorMessage = string.Empty;
            SuccessMessage = $"Write-off of {remaining:C} posted for invoice {SelectedInvoice.InvoiceNumber}.";
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = _invoiceService.LastValidationError ?? "Write-off failed.";
        }
    }

    private void RecalculateTotals()
    {
        NewSubTotal = NewItems.Sum(i => i.Amount);
        NewCGST = NewItems.Sum(i => i.CGST);
        NewSGST = NewItems.Sum(i => i.SGST);
        NewIGST = NewItems.Sum(i => i.IGST);
        NewCess = NewItems.Sum(i => i.Cess);
        NewTaxAmount = NewItems.Sum(i => i.TaxAmount);
        NewTotalAmount = NewItems.Sum(i => i.Total);
    }

    [RelayCommand]
    private async Task CreateInvoiceAsync()
    {
        if (NewItems.Count == 0)
        {
            ErrorMessage = "Please add at least one item.";
            return;
        }

        // Validate GSTIN
        if (!string.IsNullOrWhiteSpace(NewCustomerGSTIN) &&
            !TaxCalculationEngine.IsValidGSTIN(NewCustomerGSTIN))
        {
            ErrorMessage = "Invalid GSTIN format.";
            return;
        }

        // PAY-02: the credit-sale policy can only ADD blocks on top of the PAY-05 hard limit
        // InvoiceService enforces, so a policy block is refused here before a number is issued.
        // A plain hard-limit breach is left to the override panel below the register.
        if (!NewIsEstimate && SelectedCustomer != null && SelectedCustomer.Id > 0 && NewTotalAmount > 0)
        {
            var credit = await _creditSaleService.EvaluateAsync(SelectedCustomer.Id, NewTotalAmount);
            if (credit.BlockedByPolicy)
            {
                ErrorMessage = credit.Reason;
                return;
            }
            if (!credit.Allowed && credit.NeedsAdminOverride)
                WarningMessage = credit.Reason;
        }

        var invoiceNumber = await _invoiceService.GenerateNextInvoiceNumberAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = invoiceNumber,
            InvoiceDate = DateTime.UtcNow,
            CustomerId = SelectedCustomer?.Id,
            CustomerName = NewCustomerName,
            CustomerGSTIN = NewCustomerGSTIN,
            PlaceOfSupply = NewPlaceOfSupply,
            PaymentMode = NewPaymentMode,
            IsBillOfSupply = NewIsBillOfSupply,
            IsEstimate = NewIsEstimate,
            Items = NewItems.ToList(),
            PaidAmount = NewPaidAmount,
            SubTotal = NewSubTotal,
            CGST = NewCGST,
            SGST = NewSGST,
            IGST = NewIGST,
            Cess = NewCess,
            TaxAmount = NewTaxAmount,
            TotalAmount = NewTotalAmount
        };

        var success = await _invoiceService.CreateInvoiceAsync(invoice, OverrideUsername, OverridePassword);
        if (success)
        {
            // STK-11: the till keeps selling while HQ is unreachable - record the document in the outbox.
            await _offlineQueue.EnqueueAsync(PendingChange.EntityInvoice, PendingChange.ActionCreate,
                invoice.InvoiceNumber, invoice, invoice.StoreId);

            NewItems.Clear();
            SelectedCustomer = null;
            SelectedProduct = null;
            PriceSource = string.Empty;
            NewCustomerName = string.Empty;
            NewCustomerGSTIN = string.Empty;
            NewPlaceOfSupply = string.Empty;
            NewPaidAmount = 0;
            NewSubTotal = 0;
            NewCGST = 0;
            NewSGST = 0;
            NewIGST = 0;
            NewCess = 0;
            NewTaxAmount = 0;
            NewTotalAmount = 0;
            OverrideUsername = string.Empty;
            OverridePassword = string.Empty;

            // STK-06 + PAY-05 + STK-03: surface non-blocking expiry, credit-limit and negative-stock warnings
            var policyWarnings = _invoiceService.NearExpiryWarnings
                .Concat(_invoiceService.CreditLimitWarnings)
                .Concat(_invoiceService.StockPolicyWarnings)
                .ToList();
            WarningMessage = policyWarnings.Count > 0
                ? "Warning: " + string.Join(" ", policyWarnings)
                : string.Empty;
            ErrorMessage = string.Empty;
            SuccessMessage = _invoiceService.CreditLimitWarnings.Count > 0
                ? "Invoice saved with a credit-limit override recorded."
                : string.Empty;
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = !string.IsNullOrEmpty(_invoiceService.LastValidationError)
                ? _invoiceService.LastValidationError
                : "Failed to create invoice. Please check GSTIN format.";
            if (!string.IsNullOrEmpty(_invoiceService.LastOverrideError))
                ErrorMessage += " " + _invoiceService.LastOverrideError;
        }
    }

    /// <summary>INV-13: register filtered by text, date window, status, document type, payment mode, value band and dues.</summary>
    public IEnumerable<Invoice> FilteredInvoices
    {
        get
        {
            var query = Invoices.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
                query = query.Where(i => i.InvoiceNumber.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase)
                                      || (i.CustomerName != null && i.CustomerName.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase))
                                      || (i.CustomerGSTIN != null && i.CustomerGSTIN.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase)));

            if (FilterFrom.HasValue)
                query = query.Where(i => i.InvoiceDate.Date >= FilterFrom.Value.Date);
            if (FilterTo.HasValue)
                query = query.Where(i => i.InvoiceDate.Date <= FilterTo.Value.Date);

            if (FilterStatus != "All")
                query = query.Where(i => i.Status == FilterStatus);

            if (FilterType != "All")
                query = query.Where(i => i.TypeLabel == FilterType);

            if (FilterPaymentMode != "All")
                query = query.Where(i => i.PaymentMode == FilterPaymentMode);

            if (FilterMinAmount.HasValue)
                query = query.Where(i => i.TotalAmount >= FilterMinAmount.Value);
            if (FilterMaxAmount.HasValue)
                query = query.Where(i => i.TotalAmount <= FilterMaxAmount.Value);

            if (FilterOutstandingOnly)
                query = query.Where(i => !i.IsEstimate && i.Status != "Cancelled" &&
                                         i.TotalAmount - i.PaidAmount - i.WriteOffAmount > 0.01m);

            return query.ToList();
        }
    }

    [RelayCommand]
    private void ResetFilters()
    {
        SearchText = string.Empty;
        FilterFrom = null;
        FilterTo = null;
        FilterStatus = "All";
        FilterType = "All";
        FilterPaymentMode = "All";
        FilterMinAmount = null;
        FilterMaxAmount = null;
        FilterOutstandingOnly = false;
    }

    /// <summary>INV-06 / INV-08: preview and print the selected invoice (thermal, half sheet or A4).</summary>
    [RelayCommand]
    private void OpenPrintPreview()
    {
        if (SelectedInvoice == null)
        {
            ErrorMessage = "Select an invoice in the register first.";
            return;
        }

        var window = new Views.InvoicePrintPreviewWindow(SelectedInvoice.Id)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }
}
