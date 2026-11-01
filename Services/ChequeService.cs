using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// PAY-11: the cheque register service. Owns the rules a shopkeeper's bank column depends on —
/// what may still move, what is history, when a cheque is absurd, and the single place where
/// clearing an instrument is allowed to touch money (through IPaymentService, never directly).
///
/// Reads are materialised first and compared client-side: SQLite keeps money and dates as TEXT,
/// so server-side decimal/date arithmetic is the bug this repo has already been bitten by.
/// </summary>
public class ChequeService : IChequeService
{
    private const decimal Epsilon = 0.01m;

    /// <summary>A leaf older than this on entry is a typo, not a cheque.</summary>
    private const int MaxBackdatedYears = 5;

    private readonly AppDbContext _dbContext;
    private readonly IAuthService _authService;
    private readonly IAuditService _auditService;
    private readonly IFinancialYearService _financialYearService;
    private readonly IPaymentService _paymentService;

    public string? LastValidationError { get; private set; }
    public List<string> Warnings { get; } = new();

    public List<string> StatusFilters { get; } = new() { AllStatuses };
    public List<string> DirectionFilters { get; } = new() { AllDirections };

    private const string AllStatuses = "All statuses";
    private const string AllDirections = "All directions";

    public ChequeService(AppDbContext dbContext, IAuthService authService, IAuditService auditService,
        IFinancialYearService financialYearService, IPaymentService paymentService)
    {
        _dbContext = dbContext;
        _authService = authService;
        _auditService = auditService;
        _financialYearService = financialYearService;
        _paymentService = paymentService;

        StatusFilters.AddRange(Cheque.StatusOptions);
        DirectionFilters.AddRange(Cheque.DirectionOptions);
    }

    // ---------------------------------------------------------------- queries

    public async Task<List<Cheque>> GetChequesAsync(DateTime? from = null, DateTime? to = null,
        string? direction = null, string? status = null, string? search = null,
        bool postDatedOnly = false, int? storeId = null)
    {
        // Server-side predicates are string equalities only (safe on TEXT columns); the date
        // window, the post-dated test and the free-text search run client-side.
        var query = _dbContext.Cheques.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(direction) && direction != AllDirections)
            query = query.Where(c => c.Direction == direction);
        if (!string.IsNullOrWhiteSpace(status) && status != AllStatuses)
            query = query.Where(c => c.Status == status);

        var rows = await query.OrderBy(c => c.PresentmentDate).ToListAsync();

        var fromDay = from?.Date;
        var toDay = to?.Date;
        var needle = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        return rows.Where(c =>
            (fromDay == null || c.IssueDate.Date >= fromDay) &&
            (toDay == null || c.IssueDate.Date <= toDay) &&
            (storeId == null || c.StoreId == storeId) &&
            (!postDatedOnly || c.PresentmentDate.Date > DateTime.Today) &&
            (needle == null ||
             c.ChequeNumber.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
             c.PartyName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
             (c.BankName != null && c.BankName.Contains(needle, StringComparison.OrdinalIgnoreCase)) ||
             (c.PaymentNumber != null && c.PaymentNumber.Contains(needle, StringComparison.OrdinalIgnoreCase)) ||
             (c.Notes != null && c.Notes.Contains(needle, StringComparison.OrdinalIgnoreCase)) ||
             (c.BounceReason != null && c.BounceReason.Contains(needle, StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    public Task<Cheque?> GetChequeByIdAsync(int id) =>
        _dbContext.Cheques.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);

    // ---------------------------------------------------------------- add / edit

    public async Task<bool> AddChequeAsync(Cheque cheque)
    {
        Warnings.Clear();
        LastValidationError = null;

        if (cheque == null)
        {
            LastValidationError = "Nothing to save — the cheque form is empty.";
            return false;
        }

        if (!await ValidateAsync(cheque, isNew: true)) return false;

        cheque.Status = Cheque.StatusPending;
        cheque.ClearedDate = null;
        cheque.BouncedDate = null;
        cheque.BounceReason = null;
        cheque.PaymentId = null;
        cheque.PaymentNumber = null;
        cheque.CreatedAt = DateTime.UtcNow;
        cheque.UpdatedAt = DateTime.UtcNow;
        cheque.StoreId ??= _authService.CurrentUser?.StoreId;

        try
        {
            _dbContext.Cheques.Add(cheque);
            await _dbContext.SaveChangesAsync();

            await _auditService.RecordAsync("AddCheque", "Cheque", cheque.Id, cheque.RowLabel, null,
                Snapshot(cheque));
            return true;
        }
        catch (Exception ex)
        {
            LastValidationError = $"The cheque could not be saved: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Edits are allowed while the row is open only. Status, the receipt link and the outcome dates
    /// are re-read from the database rather than trusted from the form, so a cleared instrument
    /// cannot quietly change value under the ledger.
    /// </summary>
    public async Task<bool> UpdateChequeAsync(Cheque cheque)
    {
        Warnings.Clear();
        LastValidationError = null;

        if (cheque == null)
        {
            LastValidationError = "Nothing to save — the cheque form is empty.";
            return false;
        }

        var stored = await _dbContext.Cheques.FirstOrDefaultAsync(c => c.Id == cheque.Id);
        if (stored == null)
        {
            LastValidationError = "That cheque is no longer in the register — refresh the list.";
            return false;
        }
        if (!stored.IsOpen)
        {
            LastValidationError = $"{stored.ChequeNumber} is {stored.Status} and cannot be edited. "
                                + "Cancelled and bounced are final, and a cleared cheque is already money — "
                                + "correct that on the Payments screen instead.";
            return false;
        }

        var candidate = CloneForValidation(stored, cheque);
        if (!await ValidateAsync(candidate, isNew: false)) return false;

        stored.Direction = candidate.Direction;
        stored.PartyType = candidate.PartyType;
        stored.CustomerId = candidate.CustomerId;
        stored.SupplierId = candidate.SupplierId;
        stored.PartyName = candidate.PartyName;
        stored.ChequeNumber = candidate.ChequeNumber;
        stored.BankName = candidate.BankName;
        stored.IssueDate = candidate.IssueDate;
        stored.PresentmentDate = candidate.PresentmentDate;
        stored.Amount = candidate.Amount;
        stored.CreateReceiptOnClear = candidate.CreateReceiptOnClear;
        stored.Notes = candidate.Notes;
        stored.StoreId = candidate.StoreId ?? _authService.CurrentUser?.StoreId;
        stored.UpdatedAt = DateTime.UtcNow;

        var before = Snapshot(stored);
        try
        {
            await _dbContext.SaveChangesAsync();
            await _auditService.RecordAsync("UpdateCheque", "Cheque", stored.Id, stored.RowLabel,
                before, Snapshot(stored));
            return true;
        }
        catch (Exception ex)
        {
            LastValidationError = $"The cheque could not be updated: {ex.Message}";
            return false;
        }
    }

    // ---------------------------------------------------------------- transitions

    /// <summary>Pending → HandedOver: the paper moved, the bank has not spoken yet.</summary>
    public async Task<bool> MarkHandedOverAsync(int chequeId)
    {
        Warnings.Clear();
        LastValidationError = null;

        var cheque = await _dbContext.Cheques.FirstOrDefaultAsync(c => c.Id == chequeId);
        if (cheque == null) return Fail("That cheque is no longer in the register — refresh the list.");
        if (cheque.Status != Cheque.StatusPending)
        {
            return Fail(cheque.Status == Cheque.StatusHandedOver
                ? $"{cheque.ChequeNumber} is already marked handed over."
                : ExplainTerminalRefusal(cheque, "mark it handed over"));
        }

        var before = Snapshot(cheque);
        cheque.Status = Cheque.StatusHandedOver;
        cheque.UpdatedAt = DateTime.UtcNow;

        if (!await SaveTransitionAsync(cheque)) return false;

        await _auditService.RecordAsync("HandOverCheque", "Cheque", cheque.Id, cheque.RowLabel,
            before, Snapshot(cheque));
        return true;
    }

    /// <summary>
    /// Open → Cleared. Order matters: the receipt (when asked for) is raised FIRST, because a cheque
    /// may not read as cleared while its money failed to post. The status write is a single
    /// SaveChanges, so no transaction wraps the payment the receipt service already committed.
    /// </summary>
    public async Task<bool> MarkClearedAsync(int chequeId, DateTime? clearedDate = null, bool? raiseReceipt = null)
    {
        Warnings.Clear();
        LastValidationError = null;

        var cheque = await _dbContext.Cheques.FirstOrDefaultAsync(c => c.Id == chequeId);
        if (cheque == null) return Fail("That cheque is no longer in the register — refresh the list.");

        if (cheque.Status == Cheque.StatusCleared)
            return Fail($"{cheque.ChequeNumber} was already cleared on {cheque.ClearedDateText}. "
                       + "A cheque cannot be cleared twice — that would count the money twice.");
        if (cheque.Status != Cheque.StatusPending && cheque.Status != Cheque.StatusHandedOver)
            return Fail(ExplainTerminalRefusal(cheque, "clear it"));

        var cleared = (clearedDate ?? DateTime.Today).Date;
        if (cleared > DateTime.Today)
            return Fail("The clearing date cannot be in the future — the bank has not honoured the cheque yet.");
        if (cleared < cheque.IssueDate.Date)
            return Fail($"The clearing date {cleared:dd-MMM-yyyy} is before the issue date {cheque.IssueDateText}; "
                      + "an instrument cannot be paid before it was drawn.");

        if (cleared < cheque.PresentmentDate.Date)
            Warnings.Add($"{cheque.ChequeNumber} is dated {cheque.PresentmentDateText} but cleared on "
                       + $"{cleared:dd-MMM-yyyy}: a post-dated cheque should not clear before its own date, so "
                       + "re-check the leaf and the presentment date.");

        if (await _financialYearService.IsPeriodLockedAsync(cleared))
            return Fail($"FY covering {cleared:dd-MMM-yyyy} is locked (ACC-07). Unlock it on the Financial Year "
                      + "screen or reverse the clear after the period is reopened — nothing was changed.");

        // Opt-in per cheque: null = follow the row's own flag, true = force it, false = never.
        var wantsReceipt = raiseReceipt ?? cheque.CreateReceiptOnClear;
        Payment? receipt = null;

        if (wantsReceipt && cheque.IsInbound)
        {
            receipt = await RaiseReceiptAsync(cheque, cleared);
            if (receipt == null) return false; // LastValidationError already carries the reason.
        }
        else if (wantsReceipt && !cheque.IsInbound)
        {
            Warnings.Add($"{cheque.ChequeNumber} is an outgoing cheque, so no receipt was raised. "
                       + "Record the supplier payment on the Payments screen if it has to appear in the ledger.");
        }

        var before = Snapshot(cheque);
        cheque.Status = Cheque.StatusCleared;
        cheque.ClearedDate = cleared;
        cheque.UpdatedAt = DateTime.UtcNow;

        if (receipt != null)
        {
            cheque.CreateReceiptOnClear = true;
            cheque.PaymentId = receipt.Id;
            cheque.PaymentNumber = receipt.PaymentNumber;
        }

        if (!await SaveTransitionAsync(cheque)) return false;

        await _auditService.RecordAsync("ClearCheque", "Cheque", cheque.Id, cheque.RowLabel,
            before, Snapshot(cheque));
        return true;
    }

    /// <summary>
    /// Open → Bounced. A reason is mandatory: without it the bounced amount cannot be chased, and a
    /// bounced row is final so the reason can never be filled in later.
    /// </summary>
    public async Task<bool> MarkBouncedAsync(int chequeId, string? bounceReason, DateTime? bouncedDate = null)
    {
        Warnings.Clear();
        LastValidationError = null;

        var reason = (bounceReason ?? string.Empty).Trim();
        if (reason.Length == 0)
            return Fail("A bounce needs a reason (e.g. \"Insufficient funds\"). Bounced is final, so the "
                      + "reason has to be on the row the first time.");

        var cheque = await _dbContext.Cheques.FirstOrDefaultAsync(c => c.Id == chequeId);
        if (cheque == null) return Fail("That cheque is no longer in the register — refresh the list.");

        if (cheque.Status == Cheque.StatusBounced)
            return Fail($"{cheque.ChequeNumber} is already bounced on {cheque.BouncedDateText}. "
                      + "Bounced is final — it cannot be bounced again.");
        if (cheque.Status != Cheque.StatusPending && cheque.Status != Cheque.StatusHandedOver)
            return Fail(ExplainTerminalRefusal(cheque, "bounce it"));

        var bounced = (bouncedDate ?? DateTime.Today).Date;
        if (bounced > DateTime.Today)
            return Fail("The bounce date cannot be in the future — the bank has not returned the cheque yet.");
        if (bounced < cheque.IssueDate.Date)
            return Fail($"The bounce date {bounced:dd-MMM-yyyy} is before the issue date {cheque.IssueDateText}; "
                      + "an instrument cannot be returned before it exists.");

        if (await _financialYearService.IsPeriodLockedAsync(bounced))
            return Fail($"FY covering {bounced:dd-MMM-yyyy} is locked (ACC-07). The bounce was not recorded.");

        var before = Snapshot(cheque);
        cheque.Status = Cheque.StatusBounced;
        cheque.BouncedDate = bounced;
        cheque.BounceReason = reason.Length > 300 ? reason[..300] : reason;
        cheque.UpdatedAt = DateTime.UtcNow;

        if (!await SaveTransitionAsync(cheque)) return false;

        await _auditService.RecordAsync("BounceCheque", "Cheque", cheque.Id, cheque.RowLabel,
            before, Snapshot(cheque));
        return true;
    }

    /// <summary>
    /// Open → Cancelled (wrong leaf, stop-payment, cheque lost). Refused once a receipt stands on the
    /// cheque: cancelling then would leave money received with no instrument behind it.
    /// </summary>
    public async Task<bool> CancelChequeAsync(int chequeId, string? reason = null)
    {
        Warnings.Clear();
        LastValidationError = null;

        var cheque = await _dbContext.Cheques.FirstOrDefaultAsync(c => c.Id == chequeId);
        if (cheque == null) return Fail("That cheque is no longer in the register — refresh the list.");

        if (cheque.Status == Cheque.StatusCancelled)
            return Fail($"{cheque.ChequeNumber} is already cancelled.");
        if (cheque.Status != Cheque.StatusPending && cheque.Status != Cheque.StatusHandedOver)
            return Fail(ExplainTerminalRefusal(cheque, "cancel it"));

        if (cheque.HasLinkedReceipt)
            return Fail($"{cheque.ChequeNumber} already raised receipt {cheque.PaymentNumber}. Delete or reverse "
                      + "that receipt on the Payments screen first, then cancel the cheque — otherwise the "
                      + "receipt points at an instrument that no longer exists.");

        var before = Snapshot(cheque);
        var note = (reason ?? string.Empty).Trim();
        if (note.Length > 0)
            cheque.Notes = string.IsNullOrWhiteSpace(cheque.Notes)
                ? $"Cancelled: {Truncate(note, 460)}"
                : $"{cheque.Notes} | Cancelled: {Truncate(note, 400)}";

        cheque.Status = Cheque.StatusCancelled;
        cheque.UpdatedAt = DateTime.UtcNow;

        if (!await SaveTransitionAsync(cheque)) return false;

        await _auditService.RecordAsync("CancelCheque", "Cheque", cheque.Id, cheque.RowLabel,
            before, Snapshot(cheque));
        return true;
    }

    /// <summary>Only a Pending row with no receipt behind it can be deleted; anything else is audit history.</summary>
    public async Task<bool> DeleteChequeAsync(int chequeId)
    {
        Warnings.Clear();
        LastValidationError = null;

        var cheque = await _dbContext.Cheques.FirstOrDefaultAsync(c => c.Id == chequeId);
        if (cheque == null) return Fail("That cheque is no longer in the register — refresh the list.");

        if (cheque.Status != Cheque.StatusPending)
            return Fail($"Only a Pending cheque can be deleted. {cheque.ChequeNumber} is {cheque.Status}: "
                      + "cancelled and bounced rows stay in the book as evidence, and a cleared row is money.");
        if (cheque.HasLinkedReceipt)
            return Fail($"{cheque.ChequeNumber} is linked to receipt {cheque.PaymentNumber}. "
                      + "Remove the receipt on the Payments screen first.");

        var before = Snapshot(cheque);
        try
        {
            _dbContext.Cheques.Remove(cheque);
            await _dbContext.SaveChangesAsync();
            await _auditService.RecordAsync("Delete", "Cheque", cheque.Id, before: before, after: null,
                entityLabel: cheque.RowLabel);
            return true;
        }
        catch (Exception ex)
        {
            LastValidationError = $"The cheque could not be deleted: {ex.Message}";
            return false;
        }
    }

    // ---------------------------------------------------------------- KPI + CSV

    public ChequeRegisterKpis BuildKpis(IReadOnlyList<Cheque> rows)
    {
        var kpis = new ChequeRegisterKpis { RowCount = rows.Count };
        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        foreach (var c in rows)
        {
            if (c.IsOpen)
            {
                if (c.IsInbound)
                {
                    kpis.PendingInCount++;
                    kpis.PendingInValue += c.Amount;
                }
                else
                {
                    kpis.PendingOutCount++;
                    kpis.PendingOutValue += c.Amount;
                }

                // Age bucket runs from the presentment date: that is the day the shop can bank it.
                var late = c.DaysPastPresentment;
                if (late <= 0) { kpis.NotYetDueCount++; kpis.NotYetDueValue += c.Amount; }
                else if (late <= 30) kpis.Days1To30Value += c.Amount;
                else if (late <= 60) kpis.Days31To60Value += c.Amount;
                else if (late <= 90) kpis.Days61To90Value += c.Amount;
                else kpis.Over90Value += c.Amount;

                if (c.IsOverdueToPresent)
                {
                    kpis.OverdueToPresentCount++;
                    kpis.OverdueToPresentValue += c.Amount;
                }
            }
            else if (c.Status == Cheque.StatusCleared
                     && c.ClearedDate != null
                     && c.ClearedDate.Value.Date >= monthStart
                     && c.ClearedDate.Value.Date <= DateTime.Today)
            {
                kpis.ClearedThisMonthCount++;
                kpis.ClearedThisMonthValue += c.Amount;
            }
            else if (c.Status == Cheque.StatusBounced)
            {
                kpis.BouncedCount++;
                kpis.BouncedValue += c.Amount;
            }
            else if (c.Status == Cheque.StatusCancelled)
            {
                kpis.CancelledCount++;
            }
        }

        return kpis;
    }

    public string BuildCsv(IReadOnlyList<Cheque> rows, ChequeRegisterKpis kpis, DateTime from, DateTime to)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Cheque Register,{from:dd-MMM-yyyy} to {to:dd-MMM-yyyy}");
        sb.AppendLine($"Pending received,{kpis.PendingInValue:F2},Pending paid out,{kpis.PendingOutValue:F2}," +
                      $"Cleared this month,{kpis.ClearedThisMonthValue:F2},Bounced,{kpis.BouncedValue:F2}," +
                      $"Bounced count,{kpis.BouncedCount},Overdue to present,{kpis.OverdueToPresentCount}");
        sb.AppendLine($"Pending age buckets (from presentment date),{Escape(kpis.PendingAgeProfile)}");
        sb.AppendLine();
        sb.AppendLine("Direction,Cheque no,Party type,Party,Bank,Issue date,Presentment date,Amount,Status," +
                      "Cleared date,Bounced date,Bounce reason,Receipt,Auto receipt,Store,Notes");

        foreach (var c in rows)
        {
            sb.AppendLine(string.Join(',',
                Escape(c.DirectionLabel), Escape(c.ChequeNumber), Escape(c.PartyType), Escape(c.PartyName),
                Escape(c.BankName ?? string.Empty), c.IssueDate.ToString("yyyy-MM-dd"),
                c.PresentmentDate.ToString("yyyy-MM-dd"), c.Amount.ToString("F2"), Escape(c.Status),
                c.ClearedDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                c.BouncedDate?.ToString("yyyy-MM-dd") ?? string.Empty, Escape(c.BounceReason ?? string.Empty),
                Escape(c.PaymentNumber ?? string.Empty), c.CreateReceiptOnClear ? "Yes" : "No",
                c.StoreId?.ToString() ?? string.Empty, Escape(c.Notes ?? string.Empty)));
        }

        sb.AppendLine($"TOTAL,{rows.Count} cheque(s),,,,," +
                      $",{rows.Sum(c => c.Amount):F2}");
        return sb.ToString();
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();

    // ---------------------------------------------------------------- internals

    /// <summary>
    /// Runs every PAY-11 rule and returns false with a plain-language <see cref="LastValidationError"/>.
    /// Non-blocking problems land in <see cref="Warnings"/> instead.
    /// </summary>
    private async Task<bool> ValidateAsync(Cheque cheque, bool isNew)
    {
        if (!Cheque.DirectionOptions.Contains(cheque.Direction))
            return Fail($"Direction must be {Cheque.DirectionIn} (received) or {Cheque.DirectionOut} (paid out).");
        if (!Cheque.PartyTypeOptions.Contains(cheque.PartyType))
            return Fail("Party type must be Customer or Supplier.");
        if (string.IsNullOrWhiteSpace(cheque.PartyName))
            return Fail("A party name is needed — whose cheque is this?");
        if (string.IsNullOrWhiteSpace(cheque.ChequeNumber))
            return Fail("The cheque number (the leaf number printed by the bank) is required.");
        if (cheque.Amount < Epsilon)
            return Fail($"Amount must be at least ₹0.01 — you entered {cheque.Amount:N2}, and a ₹0 cheque "
                      + "only clutters the register.");

        cheque.PartyName = cheque.PartyName.Trim();
        cheque.ChequeNumber = cheque.ChequeNumber.Trim();
        cheque.BankName = string.IsNullOrWhiteSpace(cheque.BankName) ? null : cheque.BankName.Trim();
        cheque.Notes = string.IsNullOrWhiteSpace(cheque.Notes) ? null : cheque.Notes.Trim();

        // Day-precision fields: normalise to UTC midnight so the register never shows a half-day
        // drift, and the two date rules below compare whole days.
        cheque.IssueDate = cheque.IssueDate.Date;
        cheque.PresentmentDate = cheque.PresentmentDate.Date;

        if (cheque.IssueDate > DateTime.Today)
            return Fail($"Issue date {cheque.IssueDate:dd-MMM-yyyy} is in the future — the leaf was not drawn yet.");
        if (cheque.IssueDate < DateTime.Today.AddYears(-MaxBackdatedYears))
            return Fail($"Issue date {cheque.IssueDate:dd-MMM-yyyy} is more than {MaxBackdatedYears} years old; "
                      + "that looks like a typo, and a cheque that stale has no value in the register.");
        if (cheque.PresentmentDate.Date < cheque.IssueDate.Date)
            return Fail($"The presentment date {cheque.PresentmentDate:dd-MMM-yyyy} is before the issue date "
                      + $"{cheque.IssueDate:dd-MMM-yyyy}. A cheque cannot be payable before it was drawn.");

        var windowEnd = cheque.IssueDate.AddMonths(Cheque.PdcWindowMonths);
        if (cheque.PresentmentDate > windowEnd)
            return Fail($"The presentment date {cheque.PresentmentDate:dd-MMM-yyyy} is more than "
                      + $"{Cheque.PdcWindowMonths} months past the issue date. A post-dated cheque held that long "
                      + "is not bankable — fix the date, or enter it with the date the bank will actually see.");

        // Warn, never block: shops legitimately book a cheque the day after they received it.
        if (cheque.PresentmentDate.Date < DateTime.Today)
            Warnings.Add($"Presentment date {cheque.PresentmentDate:dd-MMM-yyyy} has already passed — present this "
                      + "cheque at the bank straight away instead of parking it in the register.");

        await ResolvePartyIdsAsync(cheque);

        var duplicate = await _dbContext.Cheques.AsNoTracking().FirstOrDefaultAsync(c =>
            c.Id != cheque.Id && c.Direction == cheque.Direction && c.ChequeNumber == cheque.ChequeNumber);
        if (duplicate != null)
        {
            var where = duplicate.Status == Cheque.StatusCancelled ? " (a cancelled row)" : string.Empty;
            return Fail($"Cheque {cheque.ChequeNumber} is already in the register{where} as a "
                      + $"{duplicate.DirectionLabel.ToLowerInvariant()} cheque for {duplicate.PartyName}. "
                      + "A leaf number identifies one instrument, so it cannot be booked twice.");
        }

        return true;
    }

    /// <summary>
    /// PAY-11: the party ids are optional and resolved by exact (case-insensitive) name match, so a
    /// hand-written drawer name still ties to the customer/supplier master when one exists. The name
    /// comparison runs client-side (same convention as PAY-02), which keeps it collation-proof.
    /// </summary>
    private async Task ResolvePartyIdsAsync(Cheque cheque)
    {
        cheque.CustomerId = null;
        cheque.SupplierId = null;
        if (string.IsNullOrWhiteSpace(cheque.PartyName)) return;

        if (cheque.IsInbound)
        {
            var customers = await _dbContext.Customers.AsNoTracking()
                .Select(c => new { c.Id, c.Name }).ToListAsync();
            cheque.CustomerId = customers
                .FirstOrDefault(c => string.Equals(c.Name, cheque.PartyName, StringComparison.OrdinalIgnoreCase))?.Id;
        }
        else
        {
            var suppliers = await _dbContext.Suppliers.AsNoTracking()
                .Select(s => new { s.Id, s.Name }).ToListAsync();
            cheque.SupplierId = suppliers
                .FirstOrDefault(s => string.Equals(s.Name, cheque.PartyName, StringComparison.OrdinalIgnoreCase))?.Id;
        }
    }

    /// <summary>
    /// Raises the receipt through PAY-03 (FIFO allocation over the customer's open invoices) and
    /// returns it, or null with <see cref="LastValidationError"/> set. Invoice balances and status
    /// are written by PaymentService — never here. Money is never half-applied: if the receipt does
    /// not save, the cheque stays Pending and the caller reports the reason.
    /// </summary>
    private async Task<Payment?> RaiseReceiptAsync(Cheque cheque, DateTime clearedOn)
    {
        var allocations = await _paymentService.BuildFifoAllocationsAsync(cheque.PartyName, cheque.Amount);
        var appliedToInvoices = allocations
            .Where(a => a.AllocationType == "Invoice")
            .Sum(a => a.AllocatedAmount);

        var payment = new Payment
        {
            PaymentDate = clearedOn,
            Direction = Cheque.DirectionIn,
            PartyType = Cheque.PartyCustomer,
            PartyId = cheque.CustomerId,
            PartyName = cheque.PartyName,
            Amount = cheque.Amount,
            PaymentMode = "Cheque",
            Reference = $"Cheque {cheque.ChequeNumber}"
                      + (string.IsNullOrWhiteSpace(cheque.BankName) ? string.Empty : $" · {cheque.BankName}"),
            Notes = $"PAY-11: raised when cheque {cheque.ChequeNumber} cleared on {clearedOn:dd-MMM-yyyy}",
            StoreId = cheque.StoreId ?? _authService.CurrentUser?.StoreId,
            Allocations = allocations,
        };

        if (!await _paymentService.CreatePaymentAsync(payment))
        {
            Fail($"The cheque is still {cheque.Status}: the {cheque.Amount:C} receipt could not be recorded. "
               + "Clear it without raising a receipt (row action \"Mark Cleared\" with the auto-receipt box "
               + "ticked off), or fix the invoices and try again.");
            return null;
        }

        // PAY-03 keeps anything over the open invoices as an advance line, so a customer with no
        // dues still gets a receipt — it just lands entirely as an advance, which is worth saying.
        Warnings.Add(appliedToInvoices <= Epsilon
            ? $"Receipt {payment.PaymentNumber} raised for cheque {cheque.ChequeNumber}: {cheque.PartyName} has no "
              + $"open invoices, so the whole {cheque.Amount:C} is held as an advance."
            : $"Receipt {payment.PaymentNumber} raised against cheque {cheque.ChequeNumber}: "
              + $"{appliedToInvoices:C} applied oldest-first"
              + (appliedToInvoices + Epsilon < cheque.Amount
                  ? $", {cheque.Amount - appliedToInvoices:C} held as an advance."
                  : "."));
        return payment;
    }

    private async Task<bool> SaveTransitionAsync(Cheque cheque)
    {
        try
        {
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            LastValidationError = $"The status change was not saved: {ex.Message}";
            return false;
        }
    }

    /// <summary>Scalar snapshot for the audit diff — the same field list the register grid shows.</summary>
    private static object Snapshot(Cheque c) => new
    {
        c.Id,
        c.Direction,
        c.PartyType,
        c.PartyName,
        c.ChequeNumber,
        c.BankName,
        c.IssueDate,
        c.PresentmentDate,
        c.Amount,
        c.Status,
        c.ClearedDate,
        c.BouncedDate,
        c.BounceReason,
        c.CreateReceiptOnClear,
        c.PaymentId,
        c.PaymentNumber,
        c.StoreId,
        c.Notes,
    };

    /// <summary>Form values folded onto the tracked row so validation sees exactly what will persist.</summary>
    private static Cheque CloneForValidation(Cheque stored, Cheque incoming) => new()
    {
        Id = stored.Id,
        Direction = incoming.Direction,
        PartyType = incoming.PartyType,
        PartyName = incoming.PartyName,
        ChequeNumber = incoming.ChequeNumber,
        BankName = incoming.BankName,
        IssueDate = incoming.IssueDate,
        PresentmentDate = incoming.PresentmentDate,
        Amount = incoming.Amount,
        CreateReceiptOnClear = incoming.CreateReceiptOnClear,
        Notes = incoming.Notes,
        StoreId = incoming.StoreId ?? stored.StoreId,
        CustomerId = stored.CustomerId,
        SupplierId = stored.SupplierId,
        Status = stored.Status,
    };

    private static string ExplainTerminalRefusal(Cheque cheque, string verb) => cheque.Status switch
    {
        Cheque.StatusCleared => $"{cheque.ChequeNumber} cleared on {cheque.ClearedDateText}. You cannot {verb}: "
                              + "the money side is already recorded, so reversing it here would double-count.",
        Cheque.StatusBounced => $"{cheque.ChequeNumber} bounced on {cheque.BouncedDateText} "
                              + $"({cheque.BounceReason}). Bounced is final, so you cannot {verb} — "
                              + "book a fresh cheque row if the party issued a new instrument.",
        _ => $"{cheque.ChequeNumber} is cancelled. Cancelled is final, so you cannot {verb} — add the "
           + "replacement as a new row in the register.",
    };

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private bool Fail(string message)
    {
        LastValidationError = message;
        return false;
    }
}
