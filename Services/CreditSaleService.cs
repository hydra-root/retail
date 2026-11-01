using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// PAY-02: credit-sale accounting + policy. Read-only over invoices/payments; the only
/// mutation (settle) delegates money movement to IPaymentService (PAY-03 FIFO) and never
/// duplicates it. The hard per-customer limit block remains InvoiceService's PAY-05 check —
/// this service predicts that verdict and layers the CreditSaleSetting policy on top so a
/// single call site (EvaluateAsync) can gate a credit sale without contradicting billing.
///
/// SQLite stores money as TEXT, so every numeric comparison below happens CLIENT-SIDE after
/// materialising (the repo has been bitten by server-side decimal comparisons).
/// </summary>
public class CreditSaleService : ICreditSaleService
{
    private const decimal Epsilon = 0.01m;

    private readonly AppDbContext _dbContext;
    private readonly IAuthService _authService;
    private readonly IAuditService _auditService;
    private readonly IPaymentService _paymentService;

    public string? LastValidationError { get; private set; }
    public List<string> Warnings { get; } = new();

    public CreditSaleService(AppDbContext dbContext, IAuthService authService,
        IAuditService auditService, IPaymentService paymentService)
    {
        _dbContext = dbContext;
        _authService = authService;
        _auditService = auditService;
        _paymentService = paymentService;
    }

    // ---------------------------------------------------------------- settings

    public async Task<CreditSaleSetting> GetSettingsAsync()
    {
        var setting = await _dbContext.CreditSaleSettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (setting == null)
        {
            setting = new CreditSaleSetting { Id = 1 };
            _dbContext.CreditSaleSettings.Add(setting);
            await _dbContext.SaveChangesAsync();
        }
        return setting;
    }

    // ---------------------------------------------------------------- credit ledger

    public async Task<List<CreditSaleRow>> GetCreditSalesAsync(DateTime from, DateTime to)
    {
        var setting = await GetSettingsAsync();
        var today = DateTime.Now.Date;
        var fromDay = from.Date;
        var toDay = to.Date;

        var facts = await LoadCreditFactsAsync(setting);
        var blocked = await BuildBlockedMapAsync(setting, facts);

        var rows = new List<CreditSaleRow>();
        foreach (var f in facts)
        {
            // Ledger membership: explicitly billed as credit, or still carried as a receivable.
            if (!(IsCredit(f) && f.Date.Date >= fromDay && f.Date.Date <= toDay)) continue;

            var daysOutstanding = f.IsOpen ? (today - f.Date.Date).Days : 0;
            var daysPastDue = f.IsOpen ? Math.Max(0, (today - f.DueDate.Date).Days) : 0;

            rows.Add(new CreditSaleRow
            {
                InvoiceId = f.Id,
                InvoiceNumber = f.Number,
                InvoiceDate = f.Date,
                CustomerId = f.CustomerId,
                CustomerName = f.CustomerName,
                Gstin = f.Gstin,
                PaymentMode = f.Mode,
                CreditDays = f.CreditDays,
                DueDate = f.DueDate,
                InvoiceValue = f.Total,
                Collected = f.Paid,
                WriteOff = f.WriteOff,
                Balance = f.Balance,
                Status = f.Status,
                IsOpen = f.IsOpen,
                DaysOutstanding = daysOutstanding,
                DaysPastDue = daysPastDue,
                Bucket = BucketOf(f.IsOpen, daysOutstanding),
                IsOverdue = f.IsOpen && (today - f.DueDate.Date).Days > setting.GraceDays,
                IsBlocked = f.CustomerId.HasValue && blocked.TryGetValue(f.CustomerId.Value, out var b) && b.Blocked,
            });
        }

        return rows
            .OrderBy(r => r.DueDate)
            .ThenBy(r => r.InvoiceNumber)
            .ToList();
    }

    public async Task<List<CreditSaleRow>> GetOverdueAsync()
    {
        // Overdue is a point-in-time state, so ignore the range and take all history.
        var rows = await GetCreditSalesAsync(DateTime.Now.Date.AddYears(-30), DateTime.Now.Date.AddYears(1));
        return rows.Where(r => r.IsOverdue).ToList();
    }

    public async Task<List<CustomerCreditSummary>> GetCustomerCreditSummaryAsync()
    {
        var setting = await GetSettingsAsync();
        var (summaries, _) = await BuildSummariesAsync(setting);
        return summaries.Values
            .OrderByDescending(s => s.Outstanding)
            .ThenBy(s => s.CustomerName)
            .ToList();
    }

    // ---------------------------------------------------------------- evaluate (PAY-02 + PAY-05)

    public async Task<CreditEvaluation> EvaluateAsync(int customerId, decimal amount)
    {
        Warnings.Clear();
        LastValidationError = null;

        var setting = await GetSettingsAsync();
        var facts = await LoadCreditFactsAsync(setting);
        var (summaries, _) = await BuildSummariesAsync(setting, facts);

        var customer = await _dbContext.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId);
        var result = new CreditEvaluation
        {
            CustomerId = customerId,
            CustomerName = customer?.Name ?? "Unknown",
            RequestedAmount = amount,
        };

        if (customer == null)
        {
            LastValidationError = "Customer not found.";
            result.Allowed = false;
            result.Verdict = "NoCustomer";
            result.Reason = "No such customer — a registered customer is required to extend credit.";
            return result;
        }

        var outstanding = summaries.TryGetValue(customerId, out var s) ? s.Outstanding : 0m;
        var ageingBlocked = summaries.TryGetValue(customerId, out var s2) && s2.AgeingBlocked;

        result.CreditLimit = customer.CreditLimit;
        result.Outstanding = outstanding;
        result.ProjectedOutstanding = outstanding + amount;
        result.Headroom = customer.CreditLimit > 0 ? customer.CreditLimit - outstanding : 0m;

        // PAY-05 floor, reproduced exactly: block only when a positive limit is strictly exceeded.
        var hardLimitBreached = customer.CreditLimit > 0 && result.ProjectedOutstanding > customer.CreditLimit;

        // Allowed never widens beyond PAY-05: false whenever the hard limit is breached or a
        // non-overridable policy (ageing) block applies.
        result.Allowed = !hardLimitBreached && !ageingBlocked;

        if (result.Allowed)
        {
            if (customer.CreditLimit <= 0)
            {
                result.Verdict = "NoLimitSet";
                result.Reason = $"{customer.Name} has no credit limit, so PAY-05 does not restrict the sale.";
            }
            else if (result.ProjectedOutstanding >= customer.CreditLimit * 0.8m)
            {
                var pct = result.ProjectedOutstanding / customer.CreditLimit * 100m;
                result.Verdict = "Warning";
                result.Reason = $"{customer.Name} would be at {pct:F0}% of credit limit ({result.ProjectedOutstanding:C} of {customer.CreditLimit:C}).";
                Warnings.Add(result.Reason);
                result.Warnings.Add(result.Reason);
            }
            else
            {
                result.Verdict = "Allowed";
                result.Reason = $"{amount:C} of credit is available to {customer.Name} (headroom {result.Headroom:C}).";
            }
            return result;
        }

        if (ageingBlocked)
        {
            result.BlockedByPolicy = true;
            result.Verdict = "BlockedByAgeing";
            result.Reason = $"Blocked: {customer.Name} has an unpaid invoice aged past the "
                          + $"{setting.MaxAgeingDaysBeforeBlocked}-day credit-sale limit (Credit Sale Settings)."
                          + (hardLimitBreached ? " The credit limit is also exceeded." : string.Empty);
            return result;
        }

        // Sole obstacle is the PAY-05 hard limit — an admin override at billing time clears it.
        result.NeedsAdminOverride = true;
        result.Verdict = setting.RequireAdminApprovalOverLimit ? "AdminApprovalRequired" : "BlockedByLimit";
        result.Reason = $"Credit limit exceeded for {customer.Name} (limit {customer.CreditLimit:C}, "
                      + $"outstanding would be {result.ProjectedOutstanding:C}). "
                      + "PAY-05 blocks this at billing unless a manager override (admin username/password) is supplied.";
        if (setting.RequireAdminApprovalOverLimit)
            result.Warnings.Add("Manager override required to proceed past the credit limit.");
        return result;
    }

    // ---------------------------------------------------------------- settle (delegates to PAY-03)

    public async Task<List<CreditAllocationPreview>> PreviewOldestFirstAsync(int customerId, decimal amount)
    {
        LastValidationError = null;
        if (amount <= 0)
        {
            LastValidationError = "Settlement amount must be greater than zero.";
            return new List<CreditAllocationPreview>();
        }

        var customer = await _dbContext.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId);
        if (customer == null)
        {
            LastValidationError = "Customer not found.";
            return new List<CreditAllocationPreview>();
        }

        var open = await _paymentService.GetOutstandingInvoicesAsync(customer.Name, inbound: true);
        var allocs = await _paymentService.BuildFifoAllocationsAsync(customer.Name, amount);
        return MapPreviews(open, allocs);
    }

    public async Task<CreditSettleResult> SettleOldestFirstAsync(int customerId, decimal amount,
        string paymentMode = "Cash", string? reference = null)
    {
        Warnings.Clear();
        LastValidationError = null;
        var result = new CreditSettleResult { TotalAmount = amount };

        if (amount <= 0)
        {
            result.Error = LastValidationError = "Settlement amount must be greater than zero.";
            return result;
        }

        var customer = await _dbContext.Customers.FirstOrDefaultAsync(c => c.Id == customerId);
        if (customer == null)
        {
            result.Error = LastValidationError = "Customer not found.";
            return result;
        }

        // Reuse the PAY-03 FIFO engine — do not reinvent allocation or invoice-status updates.
        var open = await _paymentService.GetOutstandingInvoicesAsync(customer.Name, inbound: true);
        var allocs = await _paymentService.BuildFifoAllocationsAsync(customer.Name, amount);

        if (allocs.Count == 0)
        {
            result.Error = LastValidationError = $"{customer.Name} has no open invoices to settle.";
            return result;
        }

        var payment = new Payment
        {
            PaymentDate = DateTime.Today,
            Direction = "In",
            PartyType = "Customer",
            PartyId = customer.Id,
            PartyName = customer.Name,
            Amount = amount,
            PaymentMode = string.IsNullOrWhiteSpace(paymentMode) ? "Cash" : paymentMode,
            Reference = reference,
            Notes = "PAY-02 settle oldest-first",
            StoreId = _authService.CurrentUser?.StoreId,
            Allocations = allocs,
        };

        var ok = await _paymentService.CreatePaymentAsync(payment);
        if (!ok)
        {
            result.Error = LastValidationError = "Failed to record the settlement payment.";
            return result;
        }

        result.Success = true;
        result.PaymentId = payment.Id;
        result.PaymentNumber = payment.PaymentNumber;
        result.Lines = MapPreviews(open, allocs);
        result.AllocatedToInvoices = allocs.Where(a => a.AllocationType == "Invoice").Sum(a => a.AllocatedAmount);
        result.AdvanceRemainder = allocs.Where(a => a.AllocationType != "Invoice").Sum(a => a.AllocatedAmount);

        if (result.AdvanceRemainder > Epsilon)
            Warnings.Add($"{result.AdvanceRemainder:C} exceeded all open invoices and is held as an advance.");

        // ACC-10: CreatePaymentAsync already audits the receipt; record the FIFO settle intent too.
        await _auditService.RecordAsync("SettleOldestFirst", "Customer", customer.Id,
            $"{customer.Name} FIFO settle {payment.PaymentNumber}", null,
            new { customer.Name, Amount = amount, payment.PaymentMode, result.AllocatedToInvoices, result.AdvanceRemainder, Lines = result.Lines.Count });

        return result;
    }

    // ---------------------------------------------------------------- CSV row material

    public string BuildCsv(IReadOnlyList<CreditSaleRow> rows, IReadOnlyList<CustomerCreditSummary> summaries,
        DateTime from, DateTime to)
    {
        var totalCredit = rows.Sum(r => r.InvoiceValue);
        var outstanding = rows.Sum(r => r.Balance);
        var overdue = rows.Where(r => r.IsOverdue).Sum(r => r.Balance);
        var blockedCustomers = summaries.Count(s => s.IsBlocked);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Credit Sales,{from:dd-MMM-yyyy} to {to:dd-MMM-yyyy}");
        sb.AppendLine($"Total credit sales,{totalCredit:F2},Outstanding,{outstanding:F2},Overdue,{overdue:F2},Blocked customers,{blockedCustomers}");
        sb.AppendLine();
        sb.AppendLine("Invoice,Invoice date,Customer,GSTIN,Payment mode,Credit days,Due date,Days outstanding,Days past due,Bucket,Invoice value,Collected,Write-off,Balance,Status,Overdue,Blocked");

        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(',',
                Escape(r.InvoiceNumber), r.InvoiceDate.ToString("yyyy-MM-dd"), Escape(r.CustomerName),
                Escape(r.Gstin), Escape(r.PaymentMode), r.CreditDays, r.DueDate.ToString("yyyy-MM-dd"),
                r.DaysOutstanding, r.DaysPastDue, Escape(r.Bucket),
                r.InvoiceValue.ToString("F2"), r.Collected.ToString("F2"), r.WriteOff.ToString("F2"),
                r.Balance.ToString("F2"), Escape(r.Status), r.IsOverdue ? "Overdue" : string.Empty,
                r.IsBlocked ? "Blocked" : string.Empty));
        }

        sb.AppendLine($"TOTAL,,,,,,,," +
                      $",{totalCredit:F2},{rows.Sum(r => r.Collected):F2},{rows.Sum(r => r.WriteOff):F2},{outstanding:F2}");
        return sb.ToString();
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    // ---------------------------------------------------------------- internals

    /// <summary>Minimal in-memory fact for one credit-relevant invoice (client-side decimal work).</summary>
    private sealed class CreditInvoiceFact
    {
        public int Id { get; set; }
        public string Number { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public int? CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string Gstin { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public decimal Paid { get; set; }
        public decimal WriteOff { get; set; }
        public decimal Balance { get; set; }
        public bool IsOpen { get; set; }
        public int CreditDays { get; set; }
        public DateTime DueDate { get; set; }
    }

    /// <summary>
    /// Loads all non-estimate, customer-linked invoices (safe server-side predicates only)
    /// and resolves due date + open state client-side.
    /// </summary>
    private async Task<List<CreditInvoiceFact>> LoadCreditFactsAsync(CreditSaleSetting setting)
    {
        var invoices = await _dbContext.Invoices.AsNoTracking()
            .Include(i => i.Customer)
            .Where(i => !i.IsEstimate && i.CustomerId != null)
            .ToListAsync();

        var facts = new List<CreditInvoiceFact>(invoices.Count);
        foreach (var i in invoices)
        {
            var balance = Math.Max(0m, i.TotalAmount - i.PaidAmount - i.WriteOffAmount);
            var isOpen = i.Status != "Paid" && balance > Epsilon;
            var creditDays = i.Customer != null && i.Customer.CreditDays > 0 ? i.Customer.CreditDays : setting.DefaultCreditDays;

            facts.Add(new CreditInvoiceFact
            {
                Id = i.Id,
                Number = i.InvoiceNumber,
                Date = i.InvoiceDate,
                CustomerId = i.CustomerId,
                CustomerName = string.IsNullOrWhiteSpace(i.CustomerName) ? (i.Customer?.Name ?? "Unknown") : i.CustomerName,
                Gstin = i.CustomerGSTIN ?? string.Empty,
                Mode = i.PaymentMode,
                Status = i.Status,
                Total = i.TotalAmount,
                Paid = i.PaidAmount,
                WriteOff = i.WriteOffAmount,
                Balance = balance,
                IsOpen = isOpen,
                CreditDays = creditDays,
                DueDate = i.InvoiceDate.Date.AddDays(creditDays),
            });
        }
        return facts;
    }

    private static bool IsCredit(CreditInvoiceFact f) =>
        string.Equals(f.Mode, "Credit", StringComparison.OrdinalIgnoreCase) || f.IsOpen;

    private static string BucketOf(bool isOpen, int ageDays) =>
        !isOpen ? "Settled"
        : ageDays <= 30 ? "Current (0-30)"
        : ageDays <= 60 ? "31-60"
        : ageDays <= 90 ? "61-90"
        : "90+";

    private async Task<(Dictionary<int, CustomerCreditSummary> Summaries, List<CreditInvoiceFact> Facts)> BuildSummariesAsync(
        CreditSaleSetting setting) =>
        await BuildSummariesAsync(setting, await LoadCreditFactsAsync(setting));

    private async Task<(Dictionary<int, CustomerCreditSummary> Summaries, List<CreditInvoiceFact> Facts)> BuildSummariesAsync(
        CreditSaleSetting setting, List<CreditInvoiceFact> facts)
    {
        var customers = await _dbContext.Customers.AsNoTracking().ToListAsync();
        var today = DateTime.Now.Date;

        var openByCustomer = facts
            .Where(f => f.IsOpen && f.CustomerId.HasValue)
            .GroupBy(f => f.CustomerId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var summaries = new Dictionary<int, CustomerCreditSummary>();

        foreach (var c in customers)
        {
            var open = openByCustomer.TryGetValue(c.Id, out var o) ? o : new List<CreditInvoiceFact>();
            var outstanding = open.Sum(f => f.Balance);

            // Only track customers who carry credit: a limit, or a live receivable.
            if (c.CreditLimit <= 0 && outstanding <= Epsilon) continue;

            var overdueCount = open.Count(f => (today - f.DueDate.Date).Days > setting.GraceDays);
            DateTime? oldestDue = open.Count > 0 ? open.Min(f => f.DueDate.Date) : null;
            var oldestPastDue = oldestDue.HasValue ? Math.Max(0, (today - oldestDue.Value.Date).Days) : 0;

            // PAY-05 always enforces the limit at billing; this flag only decides whether the ledger
            // presents the over-limit customer as "blocked" (the report/eval view of the same fact).
            var overLimit = c.CreditLimit > 0 && outstanding > c.CreditLimit;
            var limitBlocked = setting.BlockWhenOverLimit && overLimit;

            var inScope = string.IsNullOrWhiteSpace(setting.AppliesToCustomerGroup)
                || string.Equals(setting.AppliesToCustomerGroup, c.CustomerGroup, StringComparison.OrdinalIgnoreCase);
            var ageingBlocked = setting.IsEnabled && inScope && setting.MaxAgeingDaysBeforeBlocked > 0
                && open.Any(f => (today - f.Date.Date).Days >= setting.MaxAgeingDaysBeforeBlocked);

            var isBlocked = limitBlocked || ageingBlocked;
            var reason = ageingBlocked
                ? $"Unpaid invoice aged past the {setting.MaxAgeingDaysBeforeBlocked}-day limit"
                : limitBlocked
                    ? "Over credit limit (PAY-05 override available)"
                    : string.Empty;

            summaries[c.Id] = new CustomerCreditSummary
            {
                CustomerId = c.Id,
                CustomerName = c.Name,
                CustomerGroup = c.CustomerGroup,
                CreditLimit = c.CreditLimit,
                CreditDays = c.CreditDays > 0 ? c.CreditDays : setting.DefaultCreditDays,
                Outstanding = outstanding,
                Headroom = c.CreditLimit > 0 ? Math.Max(0, c.CreditLimit - outstanding) : 0m,
                UtilizationPercent = c.CreditLimit > 0 ? outstanding / c.CreditLimit * 100m : 0m,
                OpenInvoices = open.Count,
                OverdueCount = overdueCount,
                OldestDueDate = oldestDue,
                OldestDaysPastDue = oldestPastDue,
                IsOverLimit = overLimit,
                AgeingBlocked = ageingBlocked,
                NeedsAdminOverride = overLimit,
                IsBlocked = isBlocked,
                BlockedReason = reason,
            };
        }

        return (summaries, facts);
    }

    private async Task<Dictionary<int, (bool Blocked, bool NeedsOverride, string Reason)>> BuildBlockedMapAsync(
        CreditSaleSetting setting, List<CreditInvoiceFact> facts)
    {
        var (summaries, _) = await BuildSummariesAsync(setting, facts);
        return summaries.ToDictionary(kv => kv.Key, kv => (kv.Value.IsBlocked, kv.Value.NeedsAdminOverride, kv.Value.BlockedReason));
    }

    /// <summary>Zip the FIFO allocations onto the open-invoice balances for the preview/result grid.</summary>
    private static List<CreditAllocationPreview> MapPreviews(
        List<OutstandingInvoice> open, List<PaymentAllocation> allocs)
    {
        var byId = open.ToDictionary(o => o.Id, o => o);
        var lines = new List<CreditAllocationPreview>();

        foreach (var a in allocs)
        {
            if (a.AllocationType == "Invoice" && a.InvoiceId.HasValue && byId.TryGetValue(a.InvoiceId.Value, out var inv))
            {
                lines.Add(new CreditAllocationPreview
                {
                    InvoiceId = inv.Id,
                    InvoiceNumber = inv.InvoiceNumber,
                    InvoiceDate = inv.InvoiceDate,
                    BalanceBefore = inv.Remaining,
                    Applied = a.AllocatedAmount,
                    BalanceAfter = Math.Max(0, inv.Remaining - a.AllocatedAmount),
                });
            }
            else
            {
                lines.Add(new CreditAllocationPreview
                {
                    InvoiceNumber = a.InvoiceNumber ?? "(Advance)",
                    BalanceBefore = 0m,
                    Applied = a.AllocatedAmount,
                    BalanceAfter = 0m,
                    IsAdvance = true,
                });
            }
        }
        return lines;
    }
}
