using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class PaymentService : IPaymentService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly IAutoPostingService _autoPosting;

    public PaymentService(AppDbContext dbContext, IAuditService auditService,
        IAutoPostingService autoPostingService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _autoPosting = autoPostingService;
    }

    public Task<List<Payment>> GetAllPaymentsAsync() =>
        _dbContext.Payments
            .Include(p => p.Allocations)
            .Include(p => p.Splits)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();

    public Task<Payment?> GetPaymentByIdAsync(int id) =>
        _dbContext.Payments
            .Include(p => p.Allocations)
            .Include(p => p.Splits)
            .FirstOrDefaultAsync(p => p.Id == id);

    public async Task<string> GenerateNextPaymentNumberAsync(string direction)
    {
        var count = await _dbContext.Payments.CountAsync(p => p.Direction == direction);
        var date = System.DateTime.Today.ToString("yyyyMMdd");
        var prefix = direction == "In" ? "RCPT" : "PAY";
        return $"{prefix}-{date}-{count + 1:D4}";
    }

    public async Task<bool> CreatePaymentAsync(Payment payment)
    {
        try
        {
            // PAY-01: validate the split lines before persisting anything; a payment with
            // split lines records PaymentMode "Split" so registers/reports show it distinctly.
            var splitError = ValidateSplits(payment);
            if (splitError != null) return false;

            if (payment.Splits.Count > 0)
                payment.PaymentMode = "Split";

            using var txn = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                payment.CreatedAt = System.DateTime.UtcNow;

                if (string.IsNullOrWhiteSpace(payment.PaymentNumber))
                {
                    payment.PaymentNumber = await GenerateNextPaymentNumberAsync(payment.Direction);
                }

                // PAY-01: the added payment graph also inserts its Splits (same traversal
                // that saves Allocations), inside this transaction.
                _dbContext.Payments.Add(payment);
                await _dbContext.SaveChangesAsync();

                // Process allocations - update invoice paid amounts
                foreach (var alloc in payment.Allocations)
                {
                    if (alloc.InvoiceId.HasValue)
                    {
                        var invoice = await _dbContext.Invoices.FindAsync(alloc.InvoiceId.Value);
                        if (invoice != null)
                        {
                            invoice.PaidAmount += alloc.AllocatedAmount;
                            invoice.Status = invoice.PaidAmount >= invoice.TotalAmount
                                ? "Paid" : invoice.PaidAmount > 0 ? "Partial" : "Pending";
                        }
                    }
                }

                await _dbContext.SaveChangesAsync();
                await txn.CommitAsync();

                // ACC-10: audit the payment (with its key fields)
                await _auditService.RecordAsync(payment.Direction == "In" ? "ReceiptIn" : "PaymentOut",
                    "Payment", payment.Id, payment.PaymentNumber, null,
                    new { payment.PaymentNumber, payment.Direction, payment.PartyType, payment.PartyName, payment.Amount, payment.PaymentMode, payment.Reference });

                // ACC-02: post the payment/receipt to the books; a posting failure never fails the save.
                try
                {
                    await _autoPosting.PostPartyPaymentAsync(payment.Id);
                }
                catch { /* posting must never fail a payment save */ }

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

    public async Task<bool> UpdatePaymentAsync(Payment payment)
    {
        try
        {
            _dbContext.Payments.Update(payment);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeletePaymentAsync(int id)
    {
        try
        {
            // PAY-03: reverse the invoice paid-amounts applied by this payment before removing it
            // PAY-01: Splits are included so the cascade removes them with the payment (no orphans)
            var payment = await _dbContext.Payments
                .Include(p => p.Allocations)
                .Include(p => p.Splits)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (payment == null) return false;

            using var txn = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                foreach (var alloc in payment.Allocations.Where(a => a.InvoiceId.HasValue))
                {
                    var invoice = await _dbContext.Invoices.FindAsync(alloc.InvoiceId.Value);
                    if (invoice != null)
                    {
                        invoice.PaidAmount -= alloc.AllocatedAmount;
                        if (invoice.PaidAmount < 0) invoice.PaidAmount = 0;
                        invoice.Status = invoice.PaidAmount >= invoice.TotalAmount
                            ? "Paid" : invoice.PaidAmount > 0 ? "Partial" : "Pending";
                    }
                }

                _dbContext.Payments.Remove(payment);
                await _dbContext.SaveChangesAsync();
                await txn.CommitAsync();

                // ACC-10: audit the deletion (with the removed snapshot)
                await _auditService.RecordAsync("Delete", "Payment", id, payment.PaymentNumber, payment, null);
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

    public Task<List<Payment>> GetPaymentsByPartyAsync(int partyId, string partyType) =>
        _dbContext.Payments
            .Include(p => p.Allocations)
            .Include(p => p.Splits)
            .Where(p => p.PartyId == partyId && p.PartyType == partyType)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();

    /// <summary>PAY-03: open invoices for a party (oldest first). Estimates excluded — they are not receivables.</summary>
    public async Task<List<OutstandingInvoice>> GetOutstandingInvoicesAsync(string partyName, bool inbound)
    {
        var name = (partyName ?? string.Empty).Trim();
        if (name.Length == 0) return new List<OutstandingInvoice>();

        var invoices = await _dbContext.Invoices
            .Where(i => !i.IsEstimate
                        && i.CustomerName == name
                        && i.TotalAmount - i.PaidAmount - i.WriteOffAmount > 0)
            .OrderBy(i => i.InvoiceDate)
            .Select(i => new OutstandingInvoice
            {
                Id = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                InvoiceDate = i.InvoiceDate,
                TotalAmount = i.TotalAmount,
                PaidAmount = i.PaidAmount,
                WriteOffAmount = i.WriteOffAmount
            })
            .ToListAsync();

        return invoices;
    }

    /// <summary>PAY-03: build the receipt text and save it to the Desktop; returns the path or null.</summary>
    public async Task<string?> PrintReceiptAsync(int paymentId)
    {
        var payment = await _dbContext.Payments
            .Include(p => p.Allocations)
            .Include(p => p.Splits)
            .FirstOrDefaultAsync(p => p.Id == paymentId);

        if (payment == null) return null;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("==========================================");
        sb.AppendLine("        PAYMENT RECEIPT");
        sb.AppendLine("==========================================");
        sb.AppendLine($"Receipt No : {payment.PaymentNumber}");
        sb.AppendLine($"Date       : {payment.PaymentDate:dd-MMM-yyyy}");
        sb.AppendLine($"{(payment.Direction == "In" ? "Received from" : "Paid to")}  : {payment.PartyName}");
        sb.AppendLine($"Amount     : {payment.Amount:C}");
        sb.AppendLine($"Mode       : {payment.PaymentMode}");
        if (!string.IsNullOrWhiteSpace(payment.Reference))
            sb.AppendLine($"Reference  : {payment.Reference}");

        // PAY-01: split-mode breakdown (only printed for split payments; single-mode stays as above)
        if (payment.Splits.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("--- Payment Splits ---");
            foreach (var split in payment.Splits)
            {
                var line = $"{split.Mode,-15} {split.Amount,12:C}";
                if (!string.IsNullOrWhiteSpace(split.Reference))
                    line += $"  (ref: {split.Reference})";
                if (split.Mode == "Credit")
                    line += "  (not settled now - stays outstanding)";
                sb.AppendLine(line);
            }
            sb.AppendLine($"{"Total",-15} {payment.Splits.Sum(s => s.Amount),12:C}");
        }

        sb.AppendLine();
        sb.AppendLine("--- Allocations ---");
        if (payment.Allocations.Count == 0)
        {
            sb.AppendLine("(advance / no invoice allocations)");
        }
        else
        {
            foreach (var alloc in payment.Allocations)
            {
                sb.AppendLine($"{alloc.InvoiceNumber,-20} {alloc.AllocatedAmount,12:C}  ({alloc.AllocationType})");
            }
        }
        sb.AppendLine("==========================================");
        sb.AppendLine("Thank you for your business!");

        var path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            $"Receipt_{payment.PaymentNumber.Replace('/', '-').Replace('\\', '-')}.txt");
        await System.IO.File.WriteAllTextAsync(path, sb.ToString());

        return path;
    }

    /// <summary>PAY-03: allocate an amount across a party's open invoices oldest-first (FIFO); returns the allocation list.</summary>
    public async Task<List<PaymentAllocation>> BuildFifoAllocationsAsync(string partyName, decimal amount)
    {
        var openInvoices = await GetOutstandingInvoicesAsync(partyName, inbound: true);
        var allocations = new List<PaymentAllocation>();
        var remaining = amount;

        foreach (var inv in openInvoices)
        {
            if (remaining <= 0) break;
            var applied = Math.Min(remaining, inv.Remaining);
            allocations.Add(new PaymentAllocation
            {
                AllocationType = "Invoice",
                InvoiceId = inv.Id,
                InvoiceNumber = inv.InvoiceNumber,
                AllocatedAmount = applied
            });
            remaining -= applied;
        }

        if (remaining > 0)
        {
            allocations.Add(new PaymentAllocation
            {
                AllocationType = "Excess",
                InvoiceId = null,
                InvoiceNumber = "(Advance)",
                AllocatedAmount = remaining
            });
        }

        return allocations;
    }

    /// <summary>
    /// PAY-01: validate split lines before saving; returns null when valid. A payment with zero
    /// split lines is the legacy single-mode form and always passes. Rules: every line needs a
    /// mode and an amount &gt; 0, cheque references must be unique within the payment, and the
    /// lines must total the payment amount exactly (₹0.01 tolerance, same pattern as ACC-03).
    /// </summary>
    private static string? ValidateSplits(Payment payment)
    {
        if (payment.Splits.Count == 0) return null;

        for (var i = 0; i < payment.Splits.Count; i++)
        {
            var split = payment.Splits[i];
            if (string.IsNullOrWhiteSpace(split.Mode))
                return $"Split line {i + 1}: a payment mode is required.";
            if (split.Amount <= 0)
                return $"Split line {i + 1} ({split.Mode}): amount must be greater than zero.";
        }

        // Cheque numbers identify the instrument — the same one cannot appear twice in a payment.
        var duplicateCheque = payment.Splits
            .Where(s => s.Mode == "Cheque" && !string.IsNullOrWhiteSpace(s.Reference))
            .GroupBy(s => s.Reference!.Trim(), System.StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateCheque != null)
            return $"Duplicate cheque reference \"{duplicateCheque.Key}\" in the split lines.";

        var splitTotal = payment.Splits.Sum(s => s.Amount);
        if (System.Math.Abs(splitTotal - payment.Amount) > 0.01m)
            return $"Split lines total {splitTotal:N2} but the payment amount is {payment.Amount:N2}; they must match exactly.";

        return null;
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
}
