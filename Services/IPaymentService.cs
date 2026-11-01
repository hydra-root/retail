using RetailBillingApp.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface IPaymentService
{
    Task<List<Payment>> GetAllPaymentsAsync();
    Task<Payment?> GetPaymentByIdAsync(int id);
    Task<string> GenerateNextPaymentNumberAsync(string direction);
    Task<bool> CreatePaymentAsync(Payment payment);
    Task<bool> UpdatePaymentAsync(Payment payment);
    Task<bool> DeletePaymentAsync(int id);
    Task<List<Payment>> GetPaymentsByPartyAsync(int partyId, string partyType);

    /// <summary>PAY-03: open (unpaid) invoices for a party, oldest first (FIFO order).</summary>
    Task<List<OutstandingInvoice>> GetOutstandingInvoicesAsync(string partyName, bool inbound);

    /// <summary>PAY-03: build and save the receipt as a text file on Desktop; returns the path.</summary>
    Task<string?> PrintReceiptAsync(int paymentId);

    /// <summary>PAY-03: plan FIFO allocations (oldest invoice first, leftover as advance) without saving.</summary>
    Task<List<PaymentAllocation>> BuildFifoAllocationsAsync(string partyName, decimal amount);

    Task<int> SaveChangesAsync();
}

/// <summary>PAY-03: an open invoice a receipt can be applied against.</summary>
public class OutstandingInvoice
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal WriteOffAmount { get; set; }
    public decimal Remaining => Math.Max(0, TotalAmount - PaidAmount - WriteOffAmount);
}

/// <summary>PAY-01: per-settle-mode totals for the payments register summary grid.</summary>
public class PaymentModeSummaryRow
{
    public string Mode { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public decimal Received { get; set; }
    public decimal Paid { get; set; }

    /// <summary>"Credit" means not settled now — that portion stays outstanding, it is not received cash.</summary>
    public bool SettlesNow { get; set; } = true;
    public string Status => SettlesNow ? "Settled" : "Outstanding (credit)";
}
