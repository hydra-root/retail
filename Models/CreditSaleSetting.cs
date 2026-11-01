using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// PAY-02: singleton configuration row (only Id == 1 is used) holding the credit-sale
/// policy that the credit accounting layer consults when it decides whether a customer
/// may be given (more) credit. It complements — never replaces — the hard per-customer
/// credit-limit block that InvoiceService already enforces at billing time (PAY-05):
/// those flags can only ADD restrictions on top of PAY-05, because InvoiceService is
/// unaware of this row. Money-bearing decisions live here as plain ints/bools/strings.
/// </summary>
public class CreditSaleSetting
{
    [Key]
    public int Id { get; set; }

    /// <summary>Master switch: when off, only InvoiceService's PAY-05 hard limit governs; the extra credit-sale controls below are inert.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Due-date credit days used when a customer has no CreditDays of their own (0/unset).</summary>
    public int DefaultCreditDays { get; set; } = 30;

    /// <summary>Days of leeway after the due date before an invoice is treated as overdue / counts toward the ageing block.</summary>
    public int GraceDays { get; set; } = 3;

    /// <summary>Whether exceeding a customer's credit limit should be surfaced as a block. The hard PAY-05 limit is always enforced at billing regardless of this flag.</summary>
    public bool BlockWhenOverLimit { get; set; } = true;

    /// <summary>Whether an over-limit credit sale additionally requires a manager/admin override (mirrors the PAY-05 override path).</summary>
    public bool RequireAdminApprovalOverLimit { get; set; } = true;

    /// <summary>Oldest unpaid-invoice age (days from invoice date) after which further credit is stopped for that customer. 0 disables the ageing block.</summary>
    public int MaxAgeingDaysBeforeBlocked { get; set; }

    /// <summary>When set, the extra ageing/disabled controls apply only to customers in this CustomerGroup; blank means all groups. Customers outside the scope still feel the PAY-05 hard limit.</summary>
    [MaxLength(100)]
    public string? AppliesToCustomerGroup { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? UpdatedBy { get; set; }

    [NotMapped]
    public string PolicySummary => Describe(this);

    /// <summary>Plain-language reading of the policy, shared by the account view header and any future editor.</summary>
    public static string Describe(CreditSaleSetting setting)
    {
        if (!setting.IsEnabled)
            return "Credit-sale controls are OFF: only the per-customer credit limit enforced at billing (PAY-05) applies.";

        var scope = string.IsNullOrWhiteSpace(setting.AppliesToCustomerGroup)
            ? "all customer groups"
            : $"customers in group \"{setting.AppliesToCustomerGroup}\"";
        var ageing = setting.MaxAgeingDaysBeforeBlocked > 0
            ? $"credit is stopped once an unpaid invoice reaches {setting.MaxAgeingDaysBeforeBlocked} day(s) old"
            : "no ageing block is configured";
        var overLimit = setting.BlockWhenOverLimit
            ? (setting.RequireAdminApprovalOverLimit
                ? "over-limit credit needs a manager override"
                : "over-limit credit is blocked")
            : "over-limit relies solely on the billing-time limit check";

        return $"Credit-sale controls are ON for {scope}: due date = invoice date + credit days " +
               $"(default {setting.DefaultCreditDays}, grace {setting.GraceDays} day(s)), {ageing}, {overLimit}.";
    }
}
