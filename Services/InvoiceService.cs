using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class InvoiceService : IInvoiceService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuthService _authService;
    private readonly IAuditService _auditService;
    private readonly IStockPolicyService _stockPolicyService;
    private readonly IAutoPostingService _autoPosting;
    private readonly INumberSeriesService _numberSeries;

    /// <summary>Human-readable reason for the most recent validation failure (STK-06/PAY-05).</summary>
    public string? LastValidationError { get; private set; }

    /// <summary>STK-06: non-blocking near-expiry (â‰¤30 days) warnings from the last create call.</summary>
    public List<string> NearExpiryWarnings { get; } = new();

    /// <summary>PAY-05: non-blocking credit-limit/override warnings from the last create call.</summary>
    public List<string> CreditLimitWarnings { get; } = new();

    /// <summary>PAY-05: reason the manager override did not take effect (bad user/password).</summary>
    public string? LastOverrideError { get; private set; }

    /// <summary>STK-03: non-blocking negative-stock warnings from the last create/convert call.</summary>
    public List<string> StockPolicyWarnings { get; } = new();

    public InvoiceService(AppDbContext dbContext, IAuthService authService, IAuditService auditService,
        IStockPolicyService stockPolicyService, IAutoPostingService autoPostingService,
        INumberSeriesService numberSeriesService)
    {
        _dbContext = dbContext;
        _authService = authService;
        _auditService = auditService;
        _stockPolicyService = stockPolicyService;
        _autoPosting = autoPostingService;
        _numberSeries = numberSeriesService;
    }

    public Task<List<Invoice>> GetAllInvoicesAsync() =>
        _dbContext.Invoices.Include(i => i.Items).Include(i => i.Customer)
            .OrderByDescending(i => i.InvoiceDate).ToListAsync();

    public Task<Invoice?> GetInvoiceByIdAsync(int id) =>
        _dbContext.Invoices.Include(i => i.Items).Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == id);

    public Task<Invoice?> GetInvoiceByNumberAsync(string invoiceNumber) =>
        _dbContext.Invoices.Include(i => i.Items).Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber);

    public async Task<bool> CreateInvoiceAsync(Invoice invoice, string? overrideUsername = null, string? overridePassword = null)
    {
        try
        {
            using var txn = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                invoice.CreatedAt = System.DateTime.UtcNow;
                LastValidationError = null;
                LastOverrideError = null;
                NearExpiryWarnings.Clear();
                CreditLimitWarnings.Clear();
                StockPolicyWarnings.Clear();

                // USR-01/USR-03: tag the invoice with the current user's store
                if (invoice.StoreId == null)
                    invoice.StoreId = _authService.CurrentUser?.StoreId;

                // Validate GSTIN for B2B invoices
                if (!string.IsNullOrWhiteSpace(invoice.CustomerGSTIN) &&
                    !TaxCalculationEngine.IsValidGSTIN(invoice.CustomerGSTIN))
                {
                    LastValidationError = "Invalid GSTIN format.";
                    return false;
                }

                // STK-06: block expired products at billing; warn on near-expiry
                var itemProductIds = invoice.Items.Select(i => i.ProductId).Where(id => id > 0).Distinct().ToList();
                foreach (var pid in itemProductIds)
                {
                    var product = await _dbContext.Products.FindAsync(pid);
                    if (product == null) continue;

                    if (product.ExpiryDate.HasValue)
                    {
                        var days = (product.ExpiryDate.Value - System.DateTime.Today).Days;
                        if (days < 0)
                        {
                            LastValidationError = $"Product '{product.Name}' is EXPIRED (expired on {product.ExpiryDate:dd-MMM-yyyy}) and cannot be billed.";
                            return false;
                        }
                        if (days <= 30 && NearExpiryWarnings.Count == 0)
                            NearExpiryWarnings.Add($"Product '{product.Name}' expires in {days} day(s) on {product.ExpiryDate:dd-MMM-yyyy}.");
                    }
                }

                // Credit limit check for credit sales
                if (invoice.CustomerId.HasValue)
                {
                    var customer = await _dbContext.Customers.FindAsync(invoice.CustomerId.Value);
                    if (customer != null && customer.CreditLimit > 0)
                    {
                        var outstandingInvoices = await _dbContext.Invoices
                            .Where(i => i.CustomerId == invoice.CustomerId && i.Status != "Paid" && !i.IsEstimate)
                            .Select(i => new { i.TotalAmount, i.PaidAmount, i.WriteOffAmount })
                            .ToListAsync();
                        var outstanding = outstandingInvoices.Sum(i => i.TotalAmount - i.PaidAmount - i.WriteOffAmount);
                        var totalOutstanding = outstanding + invoice.TotalAmount;

                        // PAY-05: warn at 80%+ utilization (non-blocking)
                        if (totalOutstanding >= customer.CreditLimit * 0.8m && totalOutstanding <= customer.CreditLimit)
                            CreditLimitWarnings.Add(
                                $"{customer.Name} is at {totalOutstanding / customer.CreditLimit * 100m:F0}% of credit limit ({totalOutstanding:C} of {customer.CreditLimit:C}).");

                        if (totalOutstanding > customer.CreditLimit)
                        {
                            // PAY-05: manager override - admin credentials allow the sale past the limit
                            var overridden = false;
                            if (!string.IsNullOrWhiteSpace(overrideUsername) && !string.IsNullOrWhiteSpace(overridePassword))
                            {
                                var user = await _dbContext.Users
                                    .FirstOrDefaultAsync(u => u.Username == overrideUsername && u.IsActive);
                                if (user != null && (user.Role == "Admin" || (user.RoleObj != null && user.RoleObj.Name == "Admin")))
                                {
                                    overridden = ComputeSha256(overridePassword) == user.PasswordHash;
                                    if (overridden)
                                        CreditLimitWarnings.Add(
                                            $"Credit limit exceeded for {customer.Name} but overridden by manager '{user.Username}' (outstanding {totalOutstanding:C} vs limit {customer.CreditLimit:C}).");
                                    else
                                        LastOverrideError = "Override password is incorrect.";
                                }
                                else
                                {
                                    LastOverrideError = $"Override user '{overrideUsername}' is not an active admin.";
                                }
                            }

                            if (!overridden)
                            {
                                LastValidationError = $"Credit limit exceeded for {customer.Name} (limit {customer.CreditLimit:C}, outstanding would be {totalOutstanding:C})."
                                    + " An admin can override with manager username/password.";
                                return false; // Credit limit exceeded
                            }
                        }
                    }
                }

                // Determine inter-state vs intra-state
                // For simplicity, assume supplier state is "07" (Delhi) - in production, this would come from company settings
                var supplierStateCode = "07";
                var isInterState = TaxCalculationEngine.IsInterState(supplierStateCode, invoice.PlaceOfSupply);

                // Calculate tax for each item using the tax engine
                // INV-02: Bill of Supply (non-GST) carries no tax
                if (invoice.IsBillOfSupply)
                {
                    foreach (var item in invoice.Items)
                    {
                        item.TaxPercentage = 0;
                        item.CGST = 0; item.SGST = 0; item.IGST = 0; item.Cess = 0;
                        item.TaxAmount = 0;
                        item.Total = item.Amount;
                    }
                    invoice.CGST = 0; invoice.SGST = 0; invoice.IGST = 0; invoice.Cess = 0;
                    invoice.TaxAmount = 0;
                    invoice.TotalAmount = invoice.Items.Sum(i => i.Amount) - invoice.Discount + invoice.RoundOff;
                }
                else
                {
                    var taxResult = TaxCalculationEngine.CalculateInvoiceTax(
                        invoice.Items,
                        isInterState,
                        invoice.RoundOff);

                    // Set invoice-level tax breakdown
                    invoice.SubTotal = invoice.Items.Sum(i => i.Amount);
                    invoice.CGST = taxResult.CGST;
                    invoice.SGST = taxResult.SGST;
                    invoice.IGST = taxResult.IGST;
                    invoice.Cess = taxResult.Cess;
                    invoice.TaxAmount = taxResult.TotalTax;
                    invoice.TotalAmount = taxResult.TotalAmount;
                }

                // INV-07: estimates do not settle like invoices
                if (invoice.IsEstimate)
                    invoice.Status = "Estimate";
                else
                    invoice.Status = invoice.PaymentMode == "Cash" || invoice.PaidAmount >= invoice.TotalAmount
                        ? "Paid" : invoice.PaidAmount > 0 ? "Partial" : "Pending";

                // STK-03: the negative-stock policy decides whether an oversell is blocked, warned or allowed.
                // Estimates are exempt here (INV-07: stock only moves on conversion, which re-checks).
                if (!invoice.IsEstimate &&
                    !await CheckStockPolicyAsync(invoice, $"Invoice {invoice.InvoiceNumber}"))
                    return false;

                _dbContext.Invoices.Add(invoice);
                await _dbContext.SaveChangesAsync();

                // INV-07: estimates hold no stock; stock moves on conversion
                if (!invoice.IsEstimate)
                {
                foreach (var item in invoice.Items)
                {
                    if (item.ProductId.HasValue)
                    {
                        var product = await _dbContext.Products.FindAsync(item.ProductId.Value);
                        if (product != null)
                        {
                            product.StockQty -= item.Quantity;
                            product.UpdatedAt = System.DateTime.UtcNow;
                            _dbContext.StockTransactions.Add(new StockTransaction
                            {
                                ProductId = item.ProductId.Value,
                                TransactionType = "Sale",
                                QuantityChange = -item.Quantity,
                                Reference = $"Invoice {invoice.InvoiceNumber}",
                                TransactionDate = System.DateTime.UtcNow,
                                StoreId = invoice.StoreId
                            });
                        }
                    }
                }

                await _dbContext.SaveChangesAsync();
                }

                await txn.CommitAsync();

                // ACC-10: audit the invoice save after a successful commit
                var auditAction = invoice.IsEstimate ? "CreateEstimate" : "Create";
                await _auditService.RecordAsync(auditAction, "Invoice", invoice.Id, invoice.InvoiceNumber, null, invoice);

                // ACC-02: post the invoice to the books; a posting failure never fails the save.
                try
                {
                    await _autoPosting.PostInvoiceAsync(invoice.Id);
                }
                catch { /* posting must never fail an invoice save */ }

                return true;
            }
            catch
            {
                await txn.RollbackAsync();
                throw;
            }
        }
        catch { return false; }
    }

    /// <summary>
    /// STK-03: runs every product line on the invoice through the configured
    /// negative-stock policy. Returns false only in Block mode; Warn mode records
    /// the shortfall on StockPolicyWarnings and lets the sale through.
    /// </summary>
    private async Task<bool> CheckStockPolicyAsync(Invoice invoice, string context)
    {
        var blocked = new List<string>();

        foreach (var group in invoice.Items.Where(i => i.ProductId.HasValue).GroupBy(i => i.ProductId!.Value))
        {
            var product = await _dbContext.Products.FindAsync(group.Key);
            if (product == null) continue;

            var asked = group.Sum(i => i.Quantity);
            var (allowed, message) = await _stockPolicyService.EvaluateAsync(
                product.Name, product.StockQty, -asked, context);

            if (!allowed) blocked.Add(message!);
            else if (message != null) StockPolicyWarnings.Add(message);
        }

        if (blocked.Count > 0)
        {
            LastValidationError = "Not enough stock: " + string.Join(" ", blocked) +
                ". Reduce the quantity, receive a purchase, or relax the policy in Stock Policy.";
            return false;
        }

        return true;
    }

    /// <summary>INV-07: convert an estimate into a real invoice (regenerates number, moves stock).</summary>
    public async Task<bool> ConvertEstimateToInvoiceAsync(int invoiceId)
    {
        try
        {
            using var txn = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                var invoice = await _dbContext.Invoices
                    .Include(i => i.Items)
                    .FirstOrDefaultAsync(i => i.Id == invoiceId);

                if (invoice == null || !invoice.IsEstimate) return false;

                // STK-03: estimates reserved no stock, so the on-hand position is checked at conversion.
                LastValidationError = null;
                StockPolicyWarnings.Clear();
                if (!await CheckStockPolicyAsync(invoice, $"Estimate {invoice.InvoiceNumber} conversion"))
                    return false;

                invoice.IsEstimate = false;
                invoice.InvoiceNumber = await GenerateNextInvoiceNumberAsync();
                invoice.InvoiceDate = System.DateTime.UtcNow;

                // Move stock now (estimates never touched stock at creation)
                foreach (var item in invoice.Items)
                {
                    if (item.ProductId.HasValue)
                    {
                        var product = await _dbContext.Products.FindAsync(item.ProductId.Value);
                        if (product != null)
                        {
                            product.StockQty -= item.Quantity;
                            product.UpdatedAt = System.DateTime.UtcNow;
                            _dbContext.StockTransactions.Add(new StockTransaction
                            {
                                ProductId = item.ProductId.Value,
                                TransactionType = "Sale",
                                QuantityChange = -item.Quantity,
                                Reference = $"Invoice {invoice.InvoiceNumber}",
                                TransactionDate = System.DateTime.UtcNow,
                                StoreId = invoice.StoreId
                            });
                        }
                    }
                }

                invoice.Status = invoice.PaymentMode == "Cash" || invoice.PaidAmount >= invoice.TotalAmount
                    ? "Paid" : invoice.PaidAmount > 0 ? "Partial" : "Pending";

                await _dbContext.SaveChangesAsync();
                await txn.CommitAsync();

                // ACC-10: audit the conversion (estimate → invoice)
                await _auditService.RecordAsync("ConvertEstimate", "Invoice", invoice.Id, invoice.InvoiceNumber, "Estimate", "Invoice");

                // ACC-02: the converted invoice posts to the books; never fails the conversion.
                try
                {
                    await _autoPosting.PostInvoiceAsync(invoice.Id);
                }
                catch { /* posting must never fail an invoice conversion */ }

                return true;
            }
            catch
            {
                await txn.RollbackAsync();
                throw;
            }
        }
        catch
        {
            LastValidationError = "Failed to convert the estimate to an invoice.";
            return false;
        }
    }

    /// <summary>PAY-12: write off the remaining amount of an invoice (bad debts / settlement discount).</summary>
    public async Task<bool> WriteOffInvoiceAsync(int invoiceId, decimal writeOffAmount)
    {
        try
        {
            var invoice = await _dbContext.Invoices
                .Include(i => i.Items)
                .FirstOrDefaultAsync(i => i.Id == invoiceId);

            if (invoice == null || invoice.IsEstimate) return false;

            var remaining = invoice.TotalAmount - invoice.PaidAmount - invoice.WriteOffAmount;
            if (writeOffAmount <= 0 || writeOffAmount > remaining)
            {
                LastValidationError = $"Write-off amount must be between 0 and {remaining:C}.";
                return false;
            }

            using var txn = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                invoice.WriteOffAmount += writeOffAmount;
                if (invoice.PaidAmount + invoice.WriteOffAmount >= invoice.TotalAmount)
                    invoice.Status = "Paid";

                // Journal: debit Bad Debts expense, credit Debtors (receivable)
                var expenseAccount = await _dbContext.Accounts.FirstOrDefaultAsync(a => a.Name == "Bad Debts");
                if (expenseAccount == null)
                {
                    expenseAccount = new Account
                    {
                        Name = "Bad Debts",
                        AccountType = "Expense",
                        IsSystem = true
                    };
                    _dbContext.Accounts.Add(expenseAccount);
                    await _dbContext.SaveChangesAsync();
                }
                var debtorsAccount = await _dbContext.Accounts.FirstOrDefaultAsync(a => a.Name == "Debtors");

                var entry = new JournalEntry
                {
                    EntryDate = System.DateTime.UtcNow,
                    ReferenceType = "WriteOff",
                    ReferenceId = invoice.Id.ToString(),
                    Narration = $"Write-off {invoice.InvoiceNumber}",
                    Lines = new List<JournalLine>()
                };

                if (expenseAccount != null)
                    entry.Lines.Add(new JournalLine
                    {
                        AccountId = expenseAccount.Id,
                        Debit = writeOffAmount,
                        Credit = 0,
                        Narration = $"Bad debts {invoice.InvoiceNumber}"
                    });
                if (debtorsAccount != null)
                    entry.Lines.Add(new JournalLine
                    {
                        AccountId = debtorsAccount.Id,
                        Debit = 0,
                        Credit = writeOffAmount,
                        Narration = $"Receivable settled {invoice.InvoiceNumber}"
                    });

                _dbContext.JournalEntries.Add(entry);

                await _dbContext.SaveChangesAsync();
                await txn.CommitAsync();

                // ACC-10: audit the write-off (before: total, after: with write-off applied)
                await _auditService.RecordAsync("WriteOff", "Invoice", invoice.Id, invoice.InvoiceNumber,
                    new { PaidAmount = invoice.PaidAmount - writeOffAmount, WriteOffAmount = invoice.WriteOffAmount - writeOffAmount, invoice.Status },
                    new { invoice.PaidAmount, invoice.WriteOffAmount, invoice.Status });
                return true;
            }
            catch
            {
                await txn.RollbackAsync();
                throw;
            }
        }
        catch
        {
            LastValidationError ??= "Failed to write off the invoice.";
            return false;
        }
    }

    /// <summary>
    /// INV-10: edit rules. Invoices in locked financial periods cannot be
    /// updated without an admin override (username/password required);
    /// collected invoices (any amount paid) also need the override, with the
    /// change recorded in the audit trail.
    /// </summary>
    public async Task<bool> UpdateInvoiceAsync(Invoice invoice, string? overrideUsername = null, string? overridePassword = null)
    {
        try
        {
            var fresh = await _dbContext.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoice.Id);
            if (fresh == null) { LastValidationError = "Invoice not found."; return false; }

            var touchingLockedPeriod = await _dbContext.FinancialYears.AsNoTracking()
                .AnyAsync(f => f.IsLocked && f.StartDate <= fresh.InvoiceDate.Date && f.EndDate >= fresh.InvoiceDate.Date);
            var hasPayments = fresh.PaidAmount > 0m;

            if (touchingLockedPeriod || hasPayments)
            {
                var adminOk = !string.IsNullOrWhiteSpace(overrideUsername)
                    && _dbContext.Users.Any(u => u.Username == overrideUsername
                        && u.IsActive && (u.Role == "Admin" || u.RoleObj!.Name == "Admin")
                        && PasswordHasher.Verify(overridePassword ?? string.Empty, u.PasswordHash));
                if (!adminOk)
                {
                    LastOverrideError = null;
                    LastValidationError = touchingLockedPeriod
                        ? "This invoice falls in a locked financial period - changes need an admin override (enter admin username/password)."
                        : "This invoice has payments recorded - changes need an admin override (enter admin username/password).";
                    return false;
                }
                LastOverrideError = null;
                CreditLimitWarnings.Add($"Admin override '{overrideUsername}' used to change a protected invoice (INV-10 rules).");
            }

            _dbContext.Invoices.Update(invoice);
            await _dbContext.SaveChangesAsync();
            await _auditService.RecordAsync("Update", "Invoice", invoice.Id,
                invoice.InvoiceNumber, fresh, invoice);
            return true;
        }
        catch (Exception ex)
        {
            LastValidationError = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// INV-10: delete rules. Estimates (stock never moved) can always be
    /// deleted; unpaid invoices need an admin override; paid/locked-period
    /// invoices cannot be deleted at all.
    /// </summary>
    public async Task<bool> DeleteInvoiceAsync(int id, string? overrideUsername = null, string? overridePassword = null)
    {
        try
        {
            var invoice = await _dbContext.Invoices.FirstOrDefaultAsync(i => i.Id == id);
            if (invoice == null) { LastValidationError = "Invoice not found."; return false; }

            if (invoice.PaidAmount > 0m)
            {
                LastValidationError = $"Invoice {invoice.InvoiceNumber} has payments recorded ({invoice.PaidAmount:C}) - it cannot be deleted. Use a write-off or credit note instead.";
                return false;
            }

            var touchingLockedPeriod = await _dbContext.FinancialYears.AsNoTracking()
                .AnyAsync(f => f.IsLocked && f.StartDate <= invoice.InvoiceDate.Date && f.EndDate >= invoice.InvoiceDate.Date);
            if (touchingLockedPeriod)
            {
                LastValidationError = $"Invoice {invoice.InvoiceNumber} is dated in a locked financial period - deletion is not allowed.";
                return false;
            }

            if (!invoice.IsEstimate)
            {
                var adminOk = !string.IsNullOrWhiteSpace(overrideUsername)
                    && _dbContext.Users.Any(u => u.Username == overrideUsername
                        && u.IsActive && (u.Role == "Admin" || u.RoleObj!.Name == "Admin")
                        && PasswordHasher.Verify(overridePassword ?? string.Empty, u.PasswordHash));
                if (!adminOk)
                {
                    LastValidationError = "Deleting a live invoice requires an admin override (enter admin username/password).";
                    return false;
                }
            }

            // Restore stock for non-estimate invoices (INV-10 delete rule)
            if (!invoice.IsEstimate && invoice.StoreId != null)
            {
                foreach (var item in await _dbContext.InvoiceItems.Where(x => x.InvoiceId == id).ToListAsync())
                {
                    if (item.ProductId.HasValue)
                    {
                        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId.Value);
                        if (product != null && product.StockQty >= 0)
                        {
                            product.StockQty += item.Quantity;
                            _dbContext.StockTransactions.Add(new StockTransaction
                            {
                                ProductId = item.ProductId.Value,
                                TransactionType = "Invoice Delete",
                                QuantityChange = item.Quantity,
                                Reference = $"Invoice {invoice.InvoiceNumber} deleted",
                                StoreId = invoice.StoreId
                            });
                        }
                    }
                }
            }

            _dbContext.Invoices.Remove(invoice);
            await _dbContext.SaveChangesAsync();
            await _auditService.RecordAsync("Delete", "Invoice", id, invoice.InvoiceNumber, invoice, null);
            LastValidationError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastValidationError = ex.Message;
            return false;
        }
    }

    public async Task<string> GenerateNextInvoiceNumberAsync()
    {
        // INV-03: the active financial year's number series issues the number when one
        // is configured; the count-based fallback below still covers an empty series set.
        var fy = await _dbContext.FinancialYears.AsNoTracking().FirstOrDefaultAsync(f => f.IsActive);
        try
        {
            var n = await _numberSeries.GetNextNumberAsync("Invoice", fy?.Name ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(n)) return n;
        }
        catch { }

        var count = await _dbContext.Invoices.CountAsync();
        var date = System.DateTime.Today.ToString("yyyyMMdd");
        return $"INV-{date}-{count + 1:D4}";
    }

    public Task<List<Invoice>> GetPendingInvoicesAsync() =>
        _dbContext.Invoices.Include(i => i.Items).Include(i => i.Customer)
            .Where(i => i.Status != "Paid")
            .OrderByDescending(i => i.InvoiceDate).ToListAsync();

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();

    /// <summary>PAY-05: SHA-256 hash matching AuthService's stored format (lowercase hex).</summary>
    private static string ComputeSha256(string password)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

