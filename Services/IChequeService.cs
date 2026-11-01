using RetailBillingApp.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

/// <summary>
/// PAY-11: the cheque register — post-dated cheques in and out, their bank verdict, and the
/// rules that keep a shopkeeper's cheque book honest.
///
/// Deliberate design choices, all enforced here rather than in the screen:
/// <list type="bullet">
///   <item>Money movement is NOT re-implemented. Clearing an inbound cheque with the opt-in flag
///   set calls <see cref="IPaymentService.CreatePaymentAsync"/> (PAY-03 FIFO) and stores the
///   receipt link on the cheque. This service never touches Invoice.PaidAmount.</item>
///   <item>Cheque numbers are the bank's printed leaf numbers, keyed in off the paper, so they do
///   NOT come from INumberSeriesService (a series would invent a number that is not on the leaf).
///   Uniqueness is checked against the register instead.</item>
///   <item>Every mutation is audited through IAuditService with a before/after snapshot (ACC-10).</item>
/// </list>
///
/// SQLite stores money and dates as TEXT, so every amount/day comparison below happens
/// client-side after the rows are materialised.
/// </summary>
public interface IChequeService
{
    /// <summary>Plain-language reason for the most recent refused add / edit / transition / delete.</summary>
    string? LastValidationError { get; }

    /// <summary>Non-blocking problems from the last call (presentment date already past, early clear, ...).</summary>
    List<string> Warnings { get; }

    /// <summary>Status vocabulary for the screen's filter, "All statuses" first.</summary>
    List<string> StatusFilters { get; }

    /// <summary>Direction vocabulary for the screen's filter, "All directions" first.</summary>
    List<string> DirectionFilters { get; }

    /// <summary>
    /// Register rows ordered by presentment date, soonest payable first (a bank column, not a log).
    /// The date range filters on <c>IssueDate</c> (the day the instrument entered the book);
    /// <paramref name="status"/> / <paramref name="direction"/> accept a vocabulary value or the
    /// "All …" label from the filter lists; <paramref name="search"/> matches cheque number, party,
    /// bank, receipt number, bounce reason and notes; <paramref name="postDatedOnly"/> keeps rows
    /// whose presentment date is still in the future; <paramref name="storeId"/> null means all stores.
    /// </summary>
    Task<List<Cheque>> GetChequesAsync(DateTime? from = null, DateTime? to = null, string? direction = null,
        string? status = null, string? search = null, bool postDatedOnly = false, int? storeId = null);

    Task<Cheque?> GetChequeByIdAsync(int id);

    /// <summary>Validate + insert. Status is forced to Pending; the party ids are resolved by name.</summary>
    Task<bool> AddChequeAsync(Cheque cheque);

    /// <summary>
    /// Edit an OPEN row (Pending / HandedOver) only. Status and the receipt link are never taken
    /// from the form — they move through the transition commands below, so a cleared cheque cannot
    /// have its amount quietly retyped under it.
    /// </summary>
    Task<bool> UpdateChequeAsync(Cheque cheque);

    /// <summary>Pending → HandedOver: the leaf physically moved (handed to the payee / to the bank for collection).</summary>
    Task<bool> MarkHandedOverAsync(int chequeId);

    /// <summary>
    /// Open → Cleared. <paramref name="raiseReceipt"/> null = follow the row's own
    /// <see cref="Cheque.CreateReceiptOnClear"/> flag, true = force the receipt, false = never.
    /// A receipt is only ever raised for an inbound cheque, and only when the financial year
    /// covering the clear date is unlocked; the cheque is not marked cleared if the receipt fails.
    /// </summary>
    Task<bool> MarkClearedAsync(int chequeId, DateTime? clearedDate = null, bool? raiseReceipt = null);

    /// <summary>Open → Bounced. A reason is mandatory; the link to any earlier receipt is left intact for the audit trail.</summary>
    Task<bool> MarkBouncedAsync(int chequeId, string? bounceReason, DateTime? bouncedDate = null);

    /// <summary>Open → Cancelled. Refused once a receipt stands on the cheque — reverse the receipt on the Payments screen first.</summary>
    Task<bool> CancelChequeAsync(int chequeId, string? reason = null);

    /// <summary>Delete, but only a Pending row with no receipt behind it; everything else is history.</summary>
    Task<bool> DeleteChequeAsync(int chequeId);

    /// <summary>
    /// KPI cards: pending value by age bucket, cleared this month, bounced, overdue to present.
    /// Pure computation over the rows handed in, so the cards always describe exactly the grid the
    /// operator is looking at (the register screen passes its filtered rows).
    /// </summary>
    ChequeRegisterKpis BuildKpis(IReadOnlyList<Cheque> rows);

    /// <summary>
    /// CSV body (KPI block + one line per cheque) as a string; the view model writes it to the
    /// Desktop with the UTF-8 BOM convention used by the sales register.
    /// </summary>
    string BuildCsv(IReadOnlyList<Cheque> rows, ChequeRegisterKpis kpis, DateTime from, DateTime to);

    Task<int> SaveChangesAsync();
}

/// <summary>
/// PAY-11: the register's KPI cards, in one read. "Pending" counts both Pending and HandedOver —
/// neither has a bank verdict yet. Buckets are measured from the presentment date, because that is
/// the day the shopkeeper can actually walk to the bank.
/// </summary>
public class ChequeRegisterKpis
{
    public int RowCount { get; set; }

    public int PendingInCount { get; set; }
    public decimal PendingInValue { get; set; }
    public int PendingOutCount { get; set; }
    public decimal PendingOutValue { get; set; }

    public decimal PendingTotalValue => PendingInValue + PendingOutValue;

    public int NotYetDueCount { get; set; }
    public decimal NotYetDueValue { get; set; }
    public decimal Days1To30Value { get; set; }
    public decimal Days31To60Value { get; set; }
    public decimal Days61To90Value { get; set; }
    public decimal Over90Value { get; set; }

    public int ClearedThisMonthCount { get; set; }
    public decimal ClearedThisMonthValue { get; set; }

    public int BouncedCount { get; set; }
    public decimal BouncedValue { get; set; }

    public int OverdueToPresentCount { get; set; }
    public decimal OverdueToPresentValue { get; set; }

    public int CancelledCount { get; set; }

    /// <summary>Money still promised to the shop that has outlived its presentment date.</summary>
    public decimal StalePendingValue => Days31To60Value + Days61To90Value + Over90Value;

    public string PendingAgeProfile =>
        $"not due {NotYetDueValue:N0} · 1-30 {Days1To30Value:N0} · 31-60 {Days31To60Value:N0} · " +
        $"61-90 {Days61To90Value:N0} · 90+ {Over90Value:N0}";
}
