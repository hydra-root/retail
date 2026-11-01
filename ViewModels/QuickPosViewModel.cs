using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// INV-12: cashier-first single-screen till. Scan drives everything — Enter in the barcode
/// box resolves + adds a line, F-keys reach every action, and after a sale the till stays
/// warm (focus returns to the barcode). Settle assembles the same Invoice shape the invoice
/// form builds and hands it to IInvoiceService.CreateInvoiceAsync, so GST, stock movement,
/// numbering, the PAY-02 credit policy and the PAY-05 hard limit all still run; the sale is
/// then enqueued into the STK-11 outbox like InvoicesViewModel does.
/// NOTE: with [RelayCommand], the generated command names strip the trailing "Async" —
/// ScanAsync → ScanCommand, SettleAsync → SettleCommand, etc. XAML binds those exact names.
/// </summary>
public partial class QuickPosViewModel : BaseViewModel
{
    private readonly IQuickPosService _quickPosService;
    private readonly IInvoiceService _invoiceService;
    private readonly ICustomerService _customerService;
    private readonly ICreditSaleService _creditSaleService;
    private readonly IOfflineQueueService _offlineQueue;

    /// <summary>Raised whenever the till should snap focus back to the barcode box (code-behind listens).</summary>
    public event EventHandler? FocusBarcodeRequested;

    private void RaiseFocusBarcode() => FocusBarcodeRequested?.Invoke(this, EventArgs.Empty);

    [ObservableProperty]
    private string _scanText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<QuickPosLine> _lines = new();

    [ObservableProperty]
    private ObservableCollection<Customer> _customers = new();

    /// <summary>PAY-04: the master customer attached to the ticket — drives party pricing and the credit checks.</summary>
    [ObservableProperty]
    private Customer? _selectedCustomer;

    /// <summary>Free-text name for walk-ins; becomes Invoice.CustomerName (no CustomerId, so PAY-05 cannot run — pick the master for account buyers).</summary>
    [ObservableProperty]
    private string _customerName = string.Empty;

    [ObservableProperty]
    private string _holdLabel = string.Empty;

    /// <summary>INV-04 till default: Indian retail scans MRP, which is GST-inclusive. Toggle to switch live lines too.</summary>
    [ObservableProperty]
    private bool _taxInclusive = true;

    [ObservableProperty]
    private ObservableCollection<HoldTicket> _heldTickets = new();

    [ObservableProperty]
    private HoldTicket? _selectedHeldTicket;

    [ObservableProperty]
    private decimal _subTotal;

    [ObservableProperty]
    private decimal _taxAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangeDue))]
    [NotifyPropertyChangedFor(nameof(BalanceDue))]
    private decimal _totalAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangeDue))]
    [NotifyPropertyChangedFor(nameof(BalanceDue))]
    private decimal _paidAmount;

    [ObservableProperty]
    private string _paymentMode = "Cash";

    /// <summary>INV-12 till scope: cash-counter modes only. Credit bills belong to the invoice form.</summary>
    public List<string> PaymentModes { get; } = new() { "Cash", "UPI", "Card" };

    /// <summary>Over-tender change, 0.01m epsilon per house rule.</summary>
    public decimal ChangeDue => PaidAmount - TotalAmount > 0.01m ? Math.Round(PaidAmount - TotalAmount, 2) : 0m;

    /// <summary>Shortfall left on the ticket if the cashier settles now.</summary>
    public decimal BalanceDue => TotalAmount - PaidAmount > 0.01m ? Math.Round(TotalAmount - PaidAmount, 2) : 0m;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _warningMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private string _priceSource = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public QuickPosViewModel(IQuickPosService quickPosService, IInvoiceService invoiceService,
        ICustomerService customerService, ICreditSaleService creditSaleService, IOfflineQueueService offlineQueue)
    {
        _quickPosService = quickPosService;
        _invoiceService = invoiceService;
        _customerService = customerService;
        _creditSaleService = creditSaleService;
        _offlineQueue = offlineQueue;
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;

        var customers = await _customerService.GetAllCustomersAsync();
        Customers = new ObservableCollection<Customer>(customers.OrderBy(c => c.Name));
        await RefreshHeldTicketsAsync();

        IsLoading = false;
        RaiseFocusBarcode();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    private async System.Threading.Tasks.Task RefreshHeldTicketsAsync()
    {
        var held = await _quickPosService.GetHeldTicketsAsync();
        HeldTickets = new ObservableCollection<HoldTicket>(held);
        SelectedHeldTicket = null;
    }

    /// <summary>PAY-04 / PAY-06: choosing the master customer fills the name and re-prices future scans.</summary>
    partial void OnSelectedCustomerChanged(Customer? value)
    {
        if (value == null) return;
        CustomerName = value.Name;
    }

    /// <summary>INV-04: flipping the till-wide switch re-prices every live line, preserving gross like the invoice form.</summary>
    partial void OnTaxInclusiveChanged(bool value)
    {
        foreach (var line in Lines)
            line.TaxInclusive = value;
    }

    /// <summary>
    /// INV-12: the barcode field's Enter key (XAML KeyBinding → ScanCommand). Resolves the code,
    /// adds or increments the line with no dialog, surfaces the reason for unknown/expired codes,
    /// and keeps the focus warm for the next scan.
    /// </summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        var code = ScanText;
        ScanText = string.Empty;
        ErrorMessage = string.Empty;
        WarningMessage = string.Empty;

        var result = await _quickPosService.ScanAsync(code, SelectedCustomer?.Id, SelectedCustomer?.CustomerGroup);

        if (!result.Success || result.Product == null)
        {
            ErrorMessage = _quickPosService.LastValidationError ?? $"Could not resolve '{code}'.";
            RaiseFocusBarcode();
            return;
        }

        var product = result.Product;
        var line = Lines.FirstOrDefault(l => l.ProductId == product.Id);

        if (line == null)
        {
            line = new QuickPosLine
            {
                ProductId = product.Id,
                ProductName = product.Name,
                HSNCode = product.HSNCode,
            };
            line.PropertyChanged += OnLinePropertyChanged;

            // Order matters: tax mode and pricing first, quantity last so the final recompute sees everything.
            line.TaxInclusive = TaxInclusive;
            line.TaxPercentage = product.TaxPercentage;
            line.Rate = result.Rate;
            line.Quantity = 1;

            Lines.Add(line);
        }
        else
        {
            // Re-scan of the same product: increment, and reprice to the buyer currently on the till.
            line.TaxInclusive = TaxInclusive;
            line.Rate = result.Rate;
            line.Quantity += 1;
        }

        PriceSource = result.PriceSource;
        SuccessMessage = $"{product.Name} × {line.Quantity} — {result.PriceSource}";
        if (result.IsWarning)
            WarningMessage = result.Message;
        ErrorMessage = string.Empty;

        RaiseFocusBarcode();
    }

    private void OnLinePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs? e)
    {
        // Any line total/tax tick refreshes the running subtotal/tax/total strip.
        if (e.PropertyName is nameof(QuickPosLine.Amount) or nameof(QuickPosLine.TaxAmount) or nameof(QuickPosLine.Total))
            RecalculateTotals();
    }

    [RelayCommand]
    private void IncreaseQty(QuickPosLine? line)
    {
        if (line == null) return;
        line.Quantity += 1;
    }

    [RelayCommand]
    private void DecreaseQty(QuickPosLine? line)
    {
        if (line == null) return;
        if (line.Quantity > 1)
            line.Quantity -= 1;
        else
            RemoveLine(line);
    }

    [RelayCommand]
    private void RemoveLine(QuickPosLine? line)
    {
        if (line == null) return;
        line.PropertyChanged -= OnLinePropertyChanged;
        Lines.Remove(line);
        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        SubTotal = Lines.Sum(l => l.Amount);
        TaxAmount = Lines.Sum(l => l.TaxAmount);
        TotalAmount = Lines.Sum(l => l.Total);
        RaiseChangeDueNotifications();
    }

    private void RaiseChangeDueNotifications()
    {
        OnPropertyChanged(nameof(ChangeDue));
        OnPropertyChanged(nameof(BalanceDue));
    }

    /// <summary>F4: park the basket (lines, customer link and totals) and free the till.</summary>
    [RelayCommand]
    private async Task HoldAsync()
    {
        ErrorMessage = string.Empty;
        if (Lines.Count == 0)
        {
            ErrorMessage = "Nothing to hold — scan at least one item first.";
            RaiseFocusBarcode();
            return;
        }

        var ticket = new HoldTicket
        {
            HoldLabel = HoldLabel,
            CustomerId = SelectedCustomer?.Id,
            CustomerName = string.IsNullOrWhiteSpace(CustomerName) ? null : CustomerName.Trim(),
            SubTotal = SubTotal,
            TaxAmount = TaxAmount,
            TotalAmount = TotalAmount,
            Items = Lines.Select(l => new HoldTicketItem
            {
                ProductId = l.ProductId,
                ProductName = l.ProductName,
                HSNCode = l.HSNCode,
                Quantity = l.Quantity,
                Rate = l.Rate,
                TaxPercentage = l.TaxPercentage,
                CessRate = l.CessRate,
                TaxInclusive = l.TaxInclusive,
                Amount = l.Amount,
                Total = l.Total,
            }).ToList(),
        };

        if (await _quickPosService.HoldTicketAsync(ticket))
        {
            // Parking frees the whole counter — lines, buyer, tender and label all reset so the
            // next customer starts on a clean till (recall restores the buyer from the snapshot).
            ResetTill();
            await RefreshHeldTicketsAsync();
            SuccessMessage = $"Ticket '{ticket.HoldLabel}' held ({ticket.Items.Count} line(s), {ticket.TotalAmount:C}).";
            WarningMessage = string.Empty;
        }
        else
        {
            ErrorMessage = _quickPosService.LastValidationError ?? "Hold failed.";
        }
        RaiseFocusBarcode();
    }

    /// <summary>F5: recall the selected held ticket — newest held one when nothing is selected.
    /// Refuses to stack on a non-empty till so two baskets can't merge by accident.</summary>
    [RelayCommand]
    private async Task RecallAsync()
    {
        ErrorMessage = string.Empty;
        var target = SelectedHeldTicket ?? HeldTickets.FirstOrDefault();
        if (target == null)
        {
            ErrorMessage = "No held ticket to recall.";
            RaiseFocusBarcode();
            return;
        }
        if (Lines.Count > 0)
        {
            ErrorMessage = "Clear or hold the current ticket before recalling a held one.";
            RaiseFocusBarcode();
            return;
        }

        var ticket = await _quickPosService.RecallTicketAsync(target.Id);
        if (ticket == null)
        {
            ErrorMessage = _quickPosService.LastValidationError ?? "Recall failed.";
            await RefreshHeldTicketsAsync();
            RaiseFocusBarcode();
            return;
        }

        // Rebuild the lines; the tax engine re-derives the splits (inputs are all the snapshot keeps).
        foreach (var item in ticket.Items)
        {
            var line = new QuickPosLine
            {
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                HSNCode = item.HSNCode,
            };
            line.PropertyChanged += OnLinePropertyChanged;
            line.TaxInclusive = item.TaxInclusive;
            line.TaxPercentage = item.TaxPercentage;
            line.CessRate = item.CessRate;
            line.Rate = item.Rate;
            line.Quantity = item.Quantity;
            Lines.Add(line);
        }

        SelectedCustomer = Customers.FirstOrDefault(c => c.Id == ticket.CustomerId);
        if (SelectedCustomer == null)
            CustomerName = ticket.CustomerName ?? string.Empty;

        SuccessMessage = $"Recalled '{ticket.HoldLabel}' ({ticket.Items.Count} line(s), {ticket.TotalAmount:C}).";
        WarningMessage = string.Empty;
        await RefreshHeldTicketsAsync();
        RaiseFocusBarcode();
    }

    /// <summary>
    /// INV-12 settle: builds the same Invoice shape as InvoicesViewModel.CreateInvoiceAsync and hands
    /// it to IInvoiceService — so the STK-06 expiry recheck, STK-03 stock policy, PAY-05 credit limit,
    /// GST computation, stock movement and numbering all run untouched.
    /// </summary>
    [RelayCommand]
    private async Task SettleAsync()
    {
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;
        WarningMessage = string.Empty;

        if (Lines.Count == 0)
        {
            ErrorMessage = "Scan at least one item before settling.";
            RaiseFocusBarcode();
            return;
        }

        // PAY-02: the same credit-sale policy gate the invoice form runs before a number is issued.
        if (SelectedCustomer != null && SelectedCustomer.Id > 0 && TotalAmount > 0)
        {
            var credit = await _creditSaleService.EvaluateAsync(SelectedCustomer.Id, TotalAmount);
            if (credit.BlockedByPolicy)
            {
                ErrorMessage = credit.Reason;
                return;
            }
            if (!credit.Allowed && credit.NeedsAdminOverride)
                WarningMessage = credit.Reason;
        }

        var invoiceNumber = await _invoiceService.GenerateNextInvoiceNumberAsync();

        // Cash tendered is what's in the box; blank/negative falls back to the full total
        // (a cash sale is a paid sale — InvoiceService forces Cash mode to "Paid" anyway).
        var paid = PaidAmount > 0.01m ? PaidAmount : TotalAmount;

        var invoice = new Invoice
        {
            InvoiceNumber = invoiceNumber,
            InvoiceDate = DateTime.UtcNow,
            CustomerId = SelectedCustomer?.Id,
            CustomerName = string.IsNullOrWhiteSpace(CustomerName) ? null : CustomerName.Trim(),
            PaymentMode = PaymentMode,
            PaidAmount = paid,
            SubTotal = SubTotal,
            TaxAmount = TaxAmount,
            TotalAmount = TotalAmount,
            Items = Lines.Select(l => new InvoiceItem
            {
                ProductId = l.ProductId,
                ProductName = l.ProductName,
                HSNCode = l.HSNCode,
                Quantity = l.Quantity,
                Rate = l.Rate,
                TaxPercentage = l.TaxPercentage,
                CessRate = l.CessRate,
                TaxInclusive = l.TaxInclusive,
                Amount = l.Amount,
                TaxAmount = l.TaxAmount,
                Total = l.Total,
            }).ToList(),
        };

        // No manager-override panel on the till (scope): a PAY-05 hard-limit breach is refused with
        // InvoiceService's own message and the buyer is re-billed from the invoice form with override.
        var ok = await _invoiceService.CreateInvoiceAsync(invoice);
        if (ok)
        {
            // STK-11: same outbox enqueue the invoice form performs after a save — the till keeps
            // selling while HQ is unreachable.
            await _offlineQueue.EnqueueAsync(PendingChange.EntityInvoice, PendingChange.ActionCreate,
                invoice.InvoiceNumber, invoice, invoice.StoreId);

            // Amounts come back off the service-computed invoice, not the till-side estimates.
            var change = invoice.PaidAmount - invoice.TotalAmount;
            SuccessMessage = $"Sale {invoice.InvoiceNumber} — total {invoice.TotalAmount:C}, paid {invoice.PaidAmount:C}"
                + (change > 0.01m ? $", change due {change:C}."
                    : change < -0.01m ? $", balance due {(-change):C}."
                    : ", settled to the rupee.");

            var policyWarnings = _invoiceService.NearExpiryWarnings
                .Concat(_invoiceService.CreditLimitWarnings)
                .Concat(_invoiceService.StockPolicyWarnings)
                .Concat(_quickPosService.Warnings)
                .ToList();
            WarningMessage = policyWarnings.Count > 0 ? "Warning: " + string.Join(" ", policyWarnings) : string.Empty;
            ErrorMessage = string.Empty;

            // The sale is banked: reset the till (buyer, tender, label included) but keep the
            // invoice-number confirmation visible, and stay warm on the barcode box.
            ResetTill();
        }
        else
        {
            ErrorMessage = !string.IsNullOrEmpty(_invoiceService.LastValidationError)
                ? _invoiceService.LastValidationError
                : "Settle failed. Check the basket and try again.";
            if (!string.IsNullOrEmpty(_invoiceService.LastOverrideError))
                ErrorMessage += " " + _invoiceService.LastOverrideError;
        }

        // Keep the till warm.
        RaiseFocusBarcode();
    }

    /// <summary>F9: one-tap cash settle. Keeps a larger typed tender so change math still works.</summary>
    [RelayCommand]
    private async Task SettleCashAsync()
    {
        PaymentMode = "Cash";
        if (PaidAmount < TotalAmount - 0.01m)
            PaidAmount = TotalAmount;
        await SettleAsync();
    }

    /// <summary>Esc: clear the whole till — draft, customer link, tender and messages.</summary>
    [RelayCommand]
    private void ClearForm()
    {
        ResetTill();
        ErrorMessage = string.Empty;
        WarningMessage = string.Empty;
        SuccessMessage = string.Empty;
        RaiseFocusBarcode();
    }

    /// <summary>Wipes the live ticket (lines + buyer + tender + hold label) but leaves the
    /// message strip alone, so Hold/Settle can print their own confirmation afterwards.</summary>
    private void ResetTill()
    {
        ClearDraft();
        SelectedCustomer = null;
        CustomerName = string.Empty;
        HoldLabel = string.Empty;
        PaidAmount = 0;
        PaymentMode = "Cash";
        PriceSource = string.Empty;
    }

    private void ClearDraft()
    {
        foreach (var line in Lines)
            line.PropertyChanged -= OnLinePropertyChanged;
        Lines.Clear();
        RecalculateTotals();
    }
}
