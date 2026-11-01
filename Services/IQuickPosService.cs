using CommunityToolkit.Mvvm.ComponentModel;
using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

/// <summary>
/// INV-12: Quick POS till support — barcode/SKU resolution against the product master and
/// hold/recall persistence. Settling deliberately lives in the view model and still runs
/// through IInvoiceService.CreateInvoiceAsync, so GST computation, stock movement, invoice
/// numbering and the PAY-05 credit-limit check are never bypassed by the fast path.
/// </summary>
public interface IQuickPosService
{
    /// <summary>Human-readable reason for the most recent validation failure.</summary>
    string? LastValidationError { get; }

    /// <summary>Non-blocking problems from the last scan/hold/recall run (out-of/near-low stock, price fallback).</summary>
    List<string> Warnings { get; }

    /// <summary>
    /// Resolves one scanned code (exact SKU or Barcode match, like IProductService.GetProductBySkuAsync)
    /// to a catalogue product: expired products are refused with the same STK-06 wording InvoiceService
    /// uses, the rate is PAY-06 party-priced for the selected buyer, and out-of/near-low stock comes back
    /// as Success + IsWarning because STK-03 has the final say at settle time. Reads only.
    /// </summary>
    Task<QuickPosScanResult> ScanAsync(string code, int? customerId, string? customerGroup);

    /// <summary>Persists a held ticket with its lines (F4) and audits the mutation.</summary>
    Task<bool> HoldTicketAsync(HoldTicket ticket);

    /// <summary>Open held tickets, newest first, lines included so recall needs no second query.</summary>
    Task<List<HoldTicket>> GetHeldTicketsAsync();

    /// <summary>F5: returns the ticket with its lines and removes it from the held list; audits the mutation.</summary>
    Task<HoldTicket?> RecallTicketAsync(int holdTicketId);
}

/// <summary>
/// INV-12: outcome of one barcode/SKU scan. Plain DTO. Success means the till may add or
/// increment the line; Message carries the failure reason or the non-blocking stock caveat.
/// </summary>
public class QuickPosScanResult
{
    public bool Success { get; set; }

    public Product? Product { get; set; }

    /// <summary>PAY-06 effective rate for the current buyer (falls back to the product master rate).</summary>
    public decimal Rate { get; set; }

    /// <summary>Where the rate came from — shown on the till's status line, same as PAY-06 in the invoice form.</summary>
    public string PriceSource { get; set; } = "Product master rate";

    public string Message { get; set; } = string.Empty;

    /// <summary>True when Success but the cashier must know something (out-of-stock / low stock).</summary>
    public bool IsWarning { get; set; }
}

/// <summary>
/// INV-12: a live till line. Unlike InvoiceItem (plain persistence model) this one is
/// observable because the cashier edits quantities in place with the +/- buttons and a
/// qty box, and the grid cells, the line tax and the running subtotal/tax/total must
/// update without rebuilding the draft. Mapped to InvoiceItem only at settle time.
/// </summary>
public partial class QuickPosLine : ObservableObject
{
    /// <summary>Catalogue id — fixed at scan time, lines always come from the master (STK-01 rule).</summary>
    public int? ProductId { get; set; }

    public string? HSNCode { get; set; }

    [ObservableProperty]
    private string _productName = string.Empty;

    [ObservableProperty]
    private int _quantity = 1;

    [ObservableProperty]
    private decimal _rate;

    [ObservableProperty]
    private decimal _taxPercentage;

    [ObservableProperty]
    private decimal _cessRate;

    /// <summary>INV-04: the entered rate already contains GST (MRP-style till).</summary>
    [ObservableProperty]
    private bool _taxInclusive;

    [ObservableProperty]
    private decimal _amount;

    [ObservableProperty]
    private decimal _taxAmount;

    [ObservableProperty]
    private decimal _total;

    /// <summary>
    /// Re-runs the shared tax engine for this line. Mirrors InvoicesViewModel.AddItemToInvoiceAsync:
    /// gross = qty × rate, then CalculateItemTax; Amount stores the (reverse-computed when inclusive)
    /// taxable value so InvoiceService's recompute at settle sees the same numbers the cashier saw.
    /// The till has no place-of-supply input, so lines are intra-state (CGST/SGST), same as the
    /// invoice form's default when PlaceOfSupply is blank.
    /// </summary>
    public void Recalculate()
    {
        var gross = Rate * Quantity;
        var result = TaxCalculationEngine.CalculateItemTax(gross, TaxPercentage, CessRate, false, TaxInclusive);
        Amount = result.TaxableValue;
        TaxAmount = result.TotalTax;
        Total = result.TotalAmount;
    }

    partial void OnQuantityChanged(int value)
    {
        if (value < 1)
        {
            Quantity = 1;   // re-enters this partial once, then falls through with a legal value
            return;
        }
        Recalculate();
    }

    partial void OnRateChanged(decimal value) => Recalculate();

    partial void OnTaxPercentageChanged(decimal value) => Recalculate();

    partial void OnCessRateChanged(decimal value) => Recalculate();

    partial void OnTaxInclusiveChanged(bool value) => Recalculate();
}
