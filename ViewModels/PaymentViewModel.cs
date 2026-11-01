using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Models;
using RetailBillingApp.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RetailBillingApp.ViewModels;

/// <summary>
/// PAY-03: payment-in / receipts against invoices with FIFO allocation and receipt printing.
/// PAY-01: optional split settle-mode lines (cash/card/UPI/cheque/bank/wallet/credit) that must
/// total the payment amount; a payment with split lines records PaymentMode "Split".
/// </summary>
public partial class PaymentViewModel : BaseViewModel
{
    private readonly IPaymentService _paymentService;
    private readonly ISaleService _saleService;
    private readonly IOfflineQueueService _offlineQueue;

    [ObservableProperty]
    private ObservableCollection<Payment> _payments = new();

    [ObservableProperty]
    private Payment _selectedPayment = null!;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _newPartyName = string.Empty;

    [ObservableProperty]
    private string _newPartyType = "Customer";

    [ObservableProperty]
    private string _newDirection = "In";

    [ObservableProperty]
    private decimal _newAmount;

    [ObservableProperty]
    private string _newPaymentMode = "Cash";

    [ObservableProperty]
    private string _newReference = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    // New payment form - allocations
    [ObservableProperty]
    private ObservableCollection<PaymentAllocation> _newAllocations = new();

    [ObservableProperty]
    private int _newAllocationInvoiceId;

    [ObservableProperty]
    private string _newAllocationInvoiceNumber = string.Empty;

    [ObservableProperty]
    private decimal _newAllocationAmount;

    [ObservableProperty]
    private decimal _newRemainingAmount;

    // PAY-01: split settle-mode entry (Payment.Amount stays the total; these lines must sum to it)
    [ObservableProperty]
    private ObservableCollection<PaymentSplit> _newSplits = new();

    [ObservableProperty]
    private PaymentSplit? _selectedNewSplit;

    [ObservableProperty]
    private string _newSplitMode = "Cash";

    [ObservableProperty]
    private decimal _newSplitAmount;

    [ObservableProperty]
    private string _newSplitReference = string.Empty;

    public List<string> Directions { get; } = new() { "In", "Out" };

    public List<string> PartyTypes { get; } = new() { "Customer", "Supplier" };

    public List<string> PaymentModes { get; } = new() { "Cash", "Card", "UPI", "Credit", "Bank Transfer" };

    /// <summary>
    /// PAY-01: settle-mode vocabulary for split lines. "Credit" means "not settled now" —
    /// that portion stays outstanding and must never be counted as received cash.
    /// </summary>
    public List<string> SplitModes { get; } = new() { "Cash", "Card", "UPI", "Cheque", "Bank Transfer", "Wallet", "Credit" };

    /// <summary>PAY-01: amount still missing from the split lines vs the payment total (₹0.01 tolerance at save).</summary>
    public decimal SplitDifference => NewAmount - NewSplits.Sum(s => s.Amount);

    /// <summary>PAY-01: true while split lines exist (drives the difference hint visibility).</summary>
    public bool HasSplitLines => NewSplits.Count > 0;

    /// <summary>PAY-01: per-settle-mode totals over the loaded payments register.</summary>
    [ObservableProperty]
    private ObservableCollection<PaymentModeSummaryRow> _modeSummaryRows = new();

    /// <summary>PAY-03: the party's open invoices available for allocation (oldest first).</summary>
    [ObservableProperty]
    private ObservableCollection<OutstandingInvoice> _outstandingInvoices = new();

    [ObservableProperty]
    private OutstandingInvoice? _selectedOutstandingInvoice;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    partial void OnNewPartyNameChanged(string value) => OnPropertyChanged(nameof(IsOutstandingVisible));

    partial void OnNewAmountChanged(decimal value) => OnPropertyChanged(nameof(SplitDifference));

    public bool IsOutstandingVisible => !string.IsNullOrWhiteSpace(NewPartyName);

    public PaymentViewModel(IPaymentService paymentService, ISaleService saleService, IOfflineQueueService offlineQueue)
    {
        _paymentService = paymentService;
        _saleService = saleService;
        _offlineQueue = offlineQueue;

        // PAY-01: keep the split-difference hint and visibility in sync with the lines grid.
        NewSplits.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(SplitDifference));
            OnPropertyChanged(nameof(HasSplitLines));
        };
    }

    public async System.Threading.Tasks.Task LoadDataAsync()
    {
        IsLoading = true;
        var payments = await _paymentService.GetAllPaymentsAsync();
        Payments = new ObservableCollection<Payment>(payments);
        RebuildModeSummary();
        IsLoading = false;
    }

    /// <summary>
    /// PAY-01: totals per settle mode over the loaded register. Split payments contribute their
    /// lines; payments without split lines contribute their full amount under the single mode.
    /// "Credit" rows are flagged outstanding — a promise, not received cash.
    /// </summary>
    private void RebuildModeSummary()
    {
        var totals = new Dictionary<string, PaymentModeSummaryRow>(System.StringComparer.OrdinalIgnoreCase);

        void Accumulate(string? mode, string direction, decimal amount)
        {
            var key = string.IsNullOrWhiteSpace(mode) ? "Unknown" : mode.Trim();
            if (!totals.TryGetValue(key, out var row))
            {
                row = new PaymentModeSummaryRow { Mode = key, SettlesNow = key != "Credit" };
                totals[key] = row;
            }
            row.LineCount++;
            if (direction == "In") row.Received += amount;
            else row.Paid += amount;
        }

        foreach (var payment in Payments)
        {
            if (payment.Splits.Count > 0)
            {
                foreach (var split in payment.Splits)
                    Accumulate(split.Mode, payment.Direction, split.Amount);
            }
            else
            {
                Accumulate(payment.PaymentMode, payment.Direction, payment.Amount);
            }
        }

        ModeSummaryRows = new ObservableCollection<PaymentModeSummaryRow>(
            totals.Values.OrderByDescending(r => r.Received).ThenBy(r => r.Mode));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    partial void OnNewDirectionChanged(string value)
    {
        if (value == "In")
            NewPartyType = "Customer";
        else
            NewPartyType = "Supplier";
    }

    [RelayCommand]
    private async Task AddAllocationAsync()
    {
        if (_newAllocationInvoiceId <= 0 || _newAllocationAmount <= 0)
        {
            ErrorMessage = "Please select a valid invoice and amount.";
            return;
        }

        if (_newAllocationAmount > _newRemainingAmount)
        {
            ErrorMessage = "Allocation amount exceeds remaining amount.";
            return;
        }

        var alloc = new PaymentAllocation
        {
            AllocationType = "Invoice",
            InvoiceId = _newAllocationInvoiceId,
            InvoiceNumber = _newAllocationInvoiceNumber,
            AllocatedAmount = _newAllocationAmount
        };

        NewAllocations.Add(alloc);
        _newRemainingAmount -= _newAllocationAmount;

        _newAllocationInvoiceId = 0;
        _newAllocationInvoiceNumber = string.Empty;
        _newAllocationAmount = 0;
    }

    [RelayCommand]
    private void RemoveAllocation(PaymentAllocation alloc)
    {
        _newRemainingAmount += alloc.AllocatedAmount;
        NewAllocations.Remove(alloc);
    }

    /// <summary>PAY-01: add a settle-mode split line (mode + amount + optional cheque/UPI reference).</summary>
    [RelayCommand]
    private void AddSplit()
    {
        if (string.IsNullOrWhiteSpace(NewSplitMode))
        {
            ErrorMessage = "Select a settle mode for the split line.";
            return;
        }

        if (NewSplitAmount <= 0)
        {
            ErrorMessage = "Split amount must be greater than zero.";
            return;
        }

        var reference = (NewSplitReference ?? string.Empty).Trim();

        // The same cheque number may not back two split lines of one payment.
        if (NewSplitMode == "Cheque" && reference.Length > 0 &&
            NewSplits.Any(s => s.Mode == "Cheque" &&
                string.Equals((s.Reference ?? string.Empty).Trim(), reference, System.StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = $"Cheque reference \"{reference}\" is already used in this payment.";
            return;
        }

        NewSplits.Add(new PaymentSplit
        {
            Mode = NewSplitMode,
            Amount = NewSplitAmount,
            Reference = reference.Length > 0 ? reference : null
        });

        ErrorMessage = string.Empty;
        NewSplitAmount = 0;
        NewSplitReference = string.Empty;
    }

    /// <summary>PAY-01: remove a settle-mode split line from the form.</summary>
    [RelayCommand]
    private void RemoveSplit(PaymentSplit split)
    {
        if (split == null) return;
        NewSplits.Remove(split);
    }

    /// <summary>PAY-03: load the party's open invoices (oldest first).</summary>
    [RelayCommand]
    private async Task LoadOutstandingAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPartyName))
        {
            ErrorMessage = "Enter a party name first.";
            return;
        }

        var list = await _paymentService.GetOutstandingInvoicesAsync(NewPartyName, inbound: NewDirection == "In");
        OutstandingInvoices = new ObservableCollection<OutstandingInvoice>(list);
        if (list.Count == 0)
            SuccessMessage = $"No open invoices for {NewPartyName}.";
    }

    /// <summary>PAY-03: allocate the full receipt amount FIFO (oldest invoice first, leftover kept as advance).</summary>
    [RelayCommand]
    private async Task AllocateFifoAsync()
    {
        if (_newAmount <= 0 || string.IsNullOrWhiteSpace(NewPartyName))
        {
            ErrorMessage = "Enter party name and amount first.";
            return;
        }

        var allocations = await _paymentService.BuildFifoAllocationsAsync(NewPartyName, NewAmount);
        NewAllocations.Clear();
        foreach (var alloc in allocations)
            NewAllocations.Add(alloc);

        NewRemainingAmount = _newAmount - allocations.Where(a => a.AllocationType == "Invoice").Sum(a => a.AllocatedAmount);
        ErrorMessage = string.Empty;
        SuccessMessage = $"FIFO plan: {allocations.Count(a => a.AllocationType == "Invoice")} invoice(s), {NewRemainingAmount:C} kept as advance.";
    }

    /// <summary>PAY-03: apply an outstanding invoice selected in the grid (FIFO amount = whole remaining).</summary>
    [RelayCommand]
    private void ApplySelectedOutstanding()
    {
        if (SelectedOutstandingInvoice == null)
        {
            ErrorMessage = "Select an open invoice first.";
            return;
        }

        var overflow = NewAllocations.Where(a => a.AllocationType == "Invoice").Sum(a => a.AllocatedAmount) + SelectedOutstandingInvoice.Remaining - _newAmount;
        if (_newAmount > 0 && overflow > 0)
        {
            ErrorMessage = "That invoice would exceed the receipt amount.";
            return;
        }

        NewAllocations.Add(new PaymentAllocation
        {
            AllocationType = "Invoice",
            InvoiceId = SelectedOutstandingInvoice.Id,
            InvoiceNumber = SelectedOutstandingInvoice.InvoiceNumber,
            AllocatedAmount = SelectedOutstandingInvoice.Remaining
        });

        NewRemainingAmount = _newAmount - NewAllocations.Where(a => a.AllocationType == "Invoice").Sum(a => a.AllocatedAmount);
        ErrorMessage = string.Empty;
    }

    /// <summary>PAY-03: print (save as text) the receipt of the selected payment.</summary>
    [RelayCommand]
    private async Task PrintReceiptAsync()
    {
        if (SelectedPayment == null)
        {
            ErrorMessage = "Select a receipt from the list first.";
            return;
        }

        var path = await _paymentService.PrintReceiptAsync(SelectedPayment.Id);
        if (path != null)
        {
            SuccessMessage = $"Receipt saved to {path}";
            ErrorMessage = string.Empty;
        }
        else
        {
            ErrorMessage = "Failed to build the receipt.";
        }
    }

    [RelayCommand]
    private async Task CreatePaymentAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPartyName))
        {
            ErrorMessage = "Party name is required.";
            return;
        }

        if (NewAmount <= 0)
        {
            ErrorMessage = "Amount must be greater than zero.";
            return;
        }

        if (NewAllocations.Count == 0 && NewDirection == "In")
        {
            ErrorMessage = "Please add at least one allocation for receipts.";
            return;
        }

        var totalAllocated = NewAllocations.Sum(a => a.AllocatedAmount);
        if (NewDirection == "In" && totalAllocated > NewAmount)
        {
            ErrorMessage = "Total allocated exceeds payment amount.";
            return;
        }

        // PAY-01: split lines (when used) must be complete and total the amount exactly —
        // ₹0.01 tolerance, same pattern as the ACC-03 journal validator. Zero lines keeps the
        // legacy single-mode behaviour untouched.
        if (NewSplits.Count > 0)
        {
            foreach (var split in NewSplits)
            {
                if (string.IsNullOrWhiteSpace(split.Mode))
                {
                    ErrorMessage = "Every split line needs a settle mode.";
                    return;
                }
                if (split.Amount <= 0)
                {
                    ErrorMessage = $"Split line {split.Mode}: amount must be greater than zero.";
                    return;
                }
            }

            var duplicateCheque = NewSplits
                .Where(s => s.Mode == "Cheque" && !string.IsNullOrWhiteSpace(s.Reference))
                .GroupBy(s => s.Reference!.Trim(), System.StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicateCheque != null)
            {
                ErrorMessage = $"Cheque reference \"{duplicateCheque.Key}\" is used by more than one split line.";
                return;
            }

            var splitTotal = NewSplits.Sum(s => s.Amount);
            if (System.Math.Abs(splitTotal - NewAmount) > 0.01m)
            {
                ErrorMessage = $"Split lines total {splitTotal:C} but the payment amount is {NewAmount:C} — adjust the lines so they match exactly.";
                return;
            }
        }

        var splitCount = NewSplits.Count;
        var paymentNumber = await _paymentService.GenerateNextPaymentNumberAsync(NewDirection);

        var payment = new Payment
        {
            PaymentNumber = paymentNumber,
            PaymentDate = System.DateTime.UtcNow,
            Direction = NewDirection,
            PartyType = NewPartyType,
            PartyName = NewPartyName,
            Amount = NewAmount,
            // PAY-01: a split payment reads "Split" in registers/reports; single-mode stays as picked.
            PaymentMode = splitCount > 0 ? "Split" : NewPaymentMode,
            Reference = NewReference,
            Allocations = NewAllocations.ToList(),
            Splits = NewSplits.ToList()
        };

        var success = await _paymentService.CreatePaymentAsync(payment);
        if (success)
        {
            // STK-11: receipts queue for HQ like every other money document.
            await _offlineQueue.EnqueueAsync(PendingChange.EntityPayment, PendingChange.ActionCreate,
                payment.PaymentNumber, payment, payment.StoreId);

            NewAllocations.Clear();
            NewSplits.Clear();
            NewPartyName = string.Empty;
            NewDirection = "In"; // also normalizes NewPartyType back to Customer
            NewAmount = 0;
            NewPaymentMode = "Cash";
            NewReference = string.Empty;
            NewRemainingAmount = 0;
            NewAllocationInvoiceId = 0;
            NewAllocationInvoiceNumber = string.Empty;
            NewAllocationAmount = 0;
            NewSplitMode = "Cash";
            NewSplitAmount = 0;
            NewSplitReference = string.Empty;
            OutstandingInvoices.Clear();
            ErrorMessage = string.Empty;
            SuccessMessage = splitCount > 0
                ? $"Receipt {payment.PaymentNumber} recorded and applied ({splitCount} split line(s), PaymentMode \"Split\")."
                : $"Receipt {payment.PaymentNumber} recorded and applied.";
            await LoadDataAsync();
        }
        else
        {
            ErrorMessage = "Failed to record payment.";
        }
    }

    public IEnumerable<Payment> FilteredPayments =>
        string.IsNullOrWhiteSpace(_searchText)
            ? Payments
            : Payments.Where(p => p.PaymentNumber.Contains(_searchText, System.StringComparison.OrdinalIgnoreCase)
                               || (p.PartyName != null && p.PartyName.Contains(_searchText, System.StringComparison.OrdinalIgnoreCase)));
}
