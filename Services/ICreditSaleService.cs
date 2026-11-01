using RetailBillingApp.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

/// <summary>
/// PAY-02: read-only credit-sale accounting layered over the existing invoices/payments,
/// plus the credit-sale policy (CreditSaleSetting). It never re-implements billing: the
/// actual money movement on settle delegates to IPaymentService (PAY-03 FIFO), and the
/// hard per-customer limit check stays in InvoiceService (PAY-05). This service predicts
/// the verdict and adds the policy-only blocks so a single call site can gate a credit sale.
/// </summary>
public interface ICreditSaleService
{
    /// <summary>Human-readable reason for the most recent validation failure (settle / evaluate).</summary>
    string? LastValidationError { get; }

    /// <summary>Non-blocking warnings from the last call (near-limit, advance, policy notes).</summary>
    List<string> Warnings { get; }

    /// <summary>Singleton credit-sale policy row (created on first read if missing).</summary>
    Task<CreditSaleSetting> GetSettingsAsync();

    /// <summary>
    /// Per-invoice credit rows for non-estimate credit sales dated in [from, to] (inclusive),
    /// each stamped with its due date, ageing bucket, balance and a customer-level blocked flag.
    /// </summary>
    Task<List<CreditSaleRow>> GetCreditSalesAsync(DateTime from, DateTime to);

    /// <summary>Per-customer credit position: limit, outstanding, headroom, oldest due, overdue count, blocked flag.</summary>
    Task<List<CustomerCreditSummary>> GetCustomerCreditSummaryAsync();

    /// <summary>
    /// Headroom / block verdict for extending <paramref name="amount"/> of credit to a customer,
    /// honouring the CreditSaleSetting policy AND consistent with InvoiceService's PAY-05 limit
    /// (Allowed is never true for a sale PAY-05 would reject).
    /// </summary>
    Task<CreditEvaluation> EvaluateAsync(int customerId, decimal amount);

    /// <summary>Plan FIFO allocation of <paramref name="amount"/> across a customer's open credit invoices (no save).</summary>
    Task<List<CreditAllocationPreview>> PreviewOldestFirstAsync(int customerId, decimal amount);

    /// <summary>
    /// Settle a customer oldest-first: creates an inbound "In" Payment and applies it FIFO across the
    /// customer's open credit invoices through IPaymentService (PAY-03). Audited via IAuditService.
    /// </summary>
    Task<CreditSettleResult> SettleOldestFirstAsync(int customerId, decimal amount, string paymentMode = "Cash", string? reference = null);

    /// <summary>All open, past-grace credit invoices across all history (no date range).</summary>
    Task<List<CreditSaleRow>> GetOverdueAsync();

    /// <summary>
    /// Builds the credit-sales CSV body (KPI block + per-invoice rows) as a string; the view writes
    /// it to Desktop with a UTF-8 BOM. Rows material comes straight from the grids.
    /// </summary>
    string BuildCsv(IReadOnlyList<CreditSaleRow> rows, IReadOnlyList<CustomerCreditSummary> summaries, DateTime from, DateTime to);
}

/// <summary>PAY-02: one credit invoice in the credit-sales ledger. Plain DTO.</summary>
public class CreditSaleRow
{
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Gstin { get; set; } = string.Empty;
    public string PaymentMode { get; set; } = string.Empty;

    public int CreditDays { get; set; }
    public DateTime DueDate { get; set; }

    public decimal InvoiceValue { get; set; }
    public decimal Collected { get; set; }
    public decimal WriteOff { get; set; }
    public decimal Balance { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>True while money is still outstanding (Balance &gt; 0) — drives ageing/overdue metrics.</summary>
    public bool IsOpen { get; set; }

    /// <summary>Age of the invoice in days (0 once settled).</summary>
    public int DaysOutstanding { get; set; }

    /// <summary>Days past the due date (0 when not yet due or settled).</summary>
    public int DaysPastDue { get; set; }

    /// <summary>Ageing bucket: "Settled" / "Current (0-30)" / "31-60" / "61-90" / "90+".</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>Outstanding past the configured grace days.</summary>
    public bool IsOverdue { get; set; }

    /// <summary>The owning customer is currently blocked from new credit by the policy / PAY-05 limit.</summary>
    public bool IsBlocked { get; set; }

    public string DueDateText => DueDate.ToString("dd-MMM-yyyy");

    /// <summary>"N day(s) overdue" past the due date, or "N day(s) to due" before it; "Settled" once closed.</summary>
    public string DaysPastDueText
    {
        get
        {
            if (!IsOpen) return "Settled";
            var daysToDue = (DueDate.Date - DateTime.Now.Date).Days;
            return daysToDue < 0 ? $"{-daysToDue} day(s) overdue"
                 : daysToDue == 0 ? "Due today"
                 : $"{daysToDue} day(s) to due";
        }
    }

    public string OverdueLabel => IsOverdue ? "Overdue" : IsOpen ? "On track" : "Settled";
}

/// <summary>PAY-02: a customer's aggregate credit position. Plain DTO.</summary>
public class CustomerCreditSummary
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerGroup { get; set; }

    public decimal CreditLimit { get; set; }
    public int CreditDays { get; set; }
    public decimal Outstanding { get; set; }
    public decimal Headroom { get; set; }
    public decimal UtilizationPercent { get; set; }

    public int OpenInvoices { get; set; }
    public int OverdueCount { get; set; }
    public DateTime? OldestDueDate { get; set; }
    public int OldestDaysPastDue { get; set; }

    public bool IsBlocked { get; set; }

    /// <summary>Current outstanding alone already exceeds the credit limit (the PAY-05 hard-limit condition).</summary>
    public bool IsOverLimit { get; set; }

    /// <summary>A non-overridable policy block (credit-sale policy enabled + an unpaid invoice aged past MaxAgeingDaysBeforeBlocked, in scope).</summary>
    public bool AgeingBlocked { get; set; }

    public bool NeedsAdminOverride { get; set; }
    public string BlockedReason { get; set; } = string.Empty;

    public string OldestDueText => OldestDueDate.HasValue ? OldestDueDate.Value.ToString("dd-MMM-yyyy") : "—";
    public string LimitText => CreditLimit > 0 ? CreditLimit.ToString("N2") : "No limit";
    public string HeadroomText => CreditLimit > 0 ? Headroom.ToString("N2") : "n/a";
}

/// <summary>PAY-02: verdict of EvaluateAsync. Plain DTO.</summary>
public class CreditEvaluation
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;

    public decimal CreditLimit { get; set; }
    public decimal Outstanding { get; set; }
    public decimal RequestedAmount { get; set; }
    public decimal ProjectedOutstanding { get; set; }
    public decimal Headroom { get; set; }

    /// <summary>True only when the sale may proceed with no override and no policy block.</summary>
    public bool Allowed { get; set; }

    /// <summary>True when the sole obstacle is the PAY-05 hard limit (an admin override at billing can clear it).</summary>
    public bool NeedsAdminOverride { get; set; }

    /// <summary>True when a non-overridable policy block applies (credit sales off / ageing).</summary>
    public bool BlockedByPolicy { get; set; }

    /// <summary>"Allowed" / "Warning" / "AdminApprovalRequired" / "BlockedByLimit" / "BlockedByAgeing" / "BlockedCreditOff" / "NoLimitSet".</summary>
    public string Verdict { get; set; } = "Allowed";

    public string Reason { get; set; } = string.Empty;
    public List<string> Warnings { get; } = new();
}

/// <summary>PAY-02: one FIFO allocation line in a settle preview / result.</summary>
public class CreditAllocationPreview
{
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal Applied { get; set; }
    public decimal BalanceAfter { get; set; }

    /// <summary>True for the leftover line when the payment exceeds all open invoices.</summary>
    public bool IsAdvance { get; set; }

    public string InvoiceDateText => IsAdvance ? "(Advance)" : InvoiceDate.ToString("dd-MMM-yyyy");
}

/// <summary>PAY-02: outcome of SettleOldestFirstAsync.</summary>
public class CreditSettleResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int PaymentId { get; set; }
    public string? PaymentNumber { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AllocatedToInvoices { get; set; }
    public decimal AdvanceRemainder { get; set; }
    public List<CreditAllocationPreview> Lines { get; set; } = new();

    public string Summary => Success
        ? $"Receipt {PaymentNumber}: {AllocatedToInvoices:N2} applied to {Lines.Count(l => !l.IsAdvance)} invoice(s)"
          + (AdvanceRemainder > 0 ? $", {AdvanceRemainder:N2} held as advance." : ".")
        : Error ?? "Settlement failed.";
}
