using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.Linq;

namespace RetailBillingApp.Services;

/// <summary>
/// ACC-02: every sale / purchase / payment document posts through here as a
/// balanced double-entry journal via <see cref="IJournalService.PostJournalEntryAsync"/>.
/// The service is failure-safe: it never throws, exposes the reason for the last
/// failed posting through <see cref="LastError"/>, and is idempotent per document
/// — a journal already posted for the same reference is a no-op returning true.
/// </summary>
public class AutoPostingService : IAutoPostingService
{
    private readonly AppDbContext _dbContext;
    private readonly IJournalService _journalService;

    /// <summary>Reason the most recent posting failed (null when it succeeded).</summary>
    public string? LastError { get; private set; }

    public AutoPostingService(AppDbContext dbContext, IJournalService journalService)
    {
        _dbContext = dbContext;
        _journalService = journalService;
    }

    // ---------------------------------------------------------------- documents

    /// <summary>ACC-02: post a register invoice. Invoice balance identity is
    /// TotalAmount = SubTotal - Discount + TaxAmount + RoundOff; estimates post nothing.</summary>
    public async Task<bool> PostInvoiceAsync(int invoiceId)
    {
        var invoice = await _dbContext.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId);
        if (invoice == null)
        {
            LastError = $"Invoice {invoiceId} not found.";
            return false;
        }

        // INV-07: estimates hold no books; the journal appears only on conversion.
        if (invoice.IsEstimate)
            return true;

        if (await AlreadyPostedAsync("Invoice", invoice.Id.ToString()))
            return true;

        var accounts = await GetAccountsAsync("Cash", "Debtors", "Sales", "GST Output", "Write Off");
        if (!Require(accounts, "Cash", "Debtors", "Sales"))
            return false;

        var invoiceNumber = invoice.InvoiceNumber;
        var lines = new List<JournalLine>();

        // Debits: cash settled at billing, the receivable balance, the written-off part.
        AddLine(lines, accounts, "Cash", Math.Min(invoice.PaidAmount, invoice.TotalAmount), 0m, "Cash received at billing");
        AddLine(lines, accounts, "Debtors", invoice.TotalAmount - invoice.PaidAmount - invoice.WriteOffAmount, 0m, $"Customer receivable {invoiceNumber}");
        AddLine(lines, accounts, "Write Off", invoice.WriteOffAmount, 0m, $"Written-off balance {invoiceNumber}");

        // Credits: taxable value after discount, and the tax collected.
        AddLine(lines, accounts, "Sales", 0m, invoice.SubTotal - invoice.Discount, "Sales revenue");
        AddLine(lines, accounts, "GST Output", 0m, invoice.TaxAmount, "GST collected");

        var entry = new JournalEntry
        {
            EntryDate = invoice.InvoiceDate,
            Narration = $"Sale invoice {invoiceNumber}",
            ReferenceType = "Invoice",
            ReferenceId = invoice.Id.ToString(),
            Lines = lines
        };

        return await PostAsync(entry);
    }

    /// <summary>ACC-02: post a POS Sale tablet sale (Amount / PaidAmount / OutstandingAmount).</summary>
    public async Task<bool> PostSaleAsync(int saleId)
    {
        var sale = await _dbContext.Sales.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == saleId);
        if (sale == null)
        {
            LastError = $"Sale {saleId} not found.";
            return false;
        }

        if (sale.Amount <= 0m)
            return true; // nothing to post

        if (await AlreadyPostedAsync("Sale", sale.Id.ToString()))
            return true;

        var accounts = await GetAccountsAsync("Cash", "Debtors", "Sales");
        if (!Require(accounts, "Cash", "Debtors", "Sales"))
            return false;

        var entry = new JournalEntry
        {
            EntryDate = sale.SaleDate,
            Narration = $"POS sale {sale.InvoiceNumber ?? sale.Id.ToString()}",
            ReferenceType = "Sale",
            ReferenceId = sale.Id.ToString(),
            Lines = new List<JournalLine>()
        };

        AddLine(entry.Lines, accounts, "Cash", Math.Min(sale.PaidAmount, sale.Amount), 0m, "Cash received");
        AddLine(entry.Lines, accounts, "Debtors", sale.Amount - sale.PaidAmount, 0m, "Customer receivable");
        AddLine(entry.Lines, accounts, "Sales", 0m, sale.Amount, "Sales revenue");

        return await PostAsync(entry);
    }

    /// <summary>ACC-02: post a purchase bill (Purchases + GST Input debited, Cash/Creditors credited).</summary>
    public async Task<bool> PostPurchaseAsync(int purchaseId)
    {
        var purchase = await _dbContext.Purchases.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == purchaseId);
        if (purchase == null)
        {
            LastError = $"Purchase {purchaseId} not found.";
            return false;
        }

        if (purchase.TotalAmount <= 0m)
            return true; // nothing to post

        if (await AlreadyPostedAsync("Purchase", purchase.Id.ToString()))
            return true;

        var accounts = await GetAccountsAsync("Purchases", "GST Input", "Cash", "Creditors");
        if (!Require(accounts, "Purchases", "Cash", "Creditors"))
            return false;

        var entry = new JournalEntry
        {
            EntryDate = purchase.PurchaseDate,
            Narration = $"Purchase bill {purchase.PurchaseNumber}",
            ReferenceType = "Purchase",
            ReferenceId = purchase.Id.ToString(),
            Lines = new List<JournalLine>()
        };

        AddLine(entry.Lines, accounts, "Purchases", purchase.SubTotal, 0m, "Purchase of goods");
        AddLine(entry.Lines, accounts, "GST Input", purchase.TaxAmount, 0m, "GST input credit");
        AddLine(entry.Lines, accounts, "Cash", 0m, Math.Min(purchase.PaidAmount, purchase.TotalAmount), "Paid at billing");
        AddLine(entry.Lines, accounts, "Creditors", 0m, purchase.TotalAmount - purchase.PaidAmount, "Supplier payable");

        return await PostAsync(entry);
    }

    /// <summary>ACC-02: keep the out-of-party flow posting: one line on the given
    /// account, the contra on Cash. Idempotent on ReferenceType "Payment" / accountId.</summary>
    public async Task<bool> PostPaymentAsync(int accountId, decimal amount, bool isDebit, string narration)
    {
        if (amount <= 0m)
            return true; // nothing to post

        if (await AlreadyPostedAsync("Payment", accountId.ToString()))
            return true;

        var cashAccount = await _dbContext.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Name == "Cash");
        if (cashAccount == null)
        {
            LastError = "Cash account is missing from the chart of accounts.";
            return false;
        }

        var entry = new JournalEntry
        {
            Narration = narration,
            ReferenceType = "Payment",
            ReferenceId = accountId.ToString(),
            Lines = new List<JournalLine>
            {
                new JournalLine { AccountId = accountId, Debit = isDebit ? amount : 0m, Credit = isDebit ? 0m : amount, Narration = narration },
                new JournalLine { AccountId = cashAccount.Id, Debit = isDebit ? 0m : amount, Credit = isDebit ? amount : 0m, Narration = "Cash payment/receipt" }
            }
        };

        return await PostAsync(entry);
    }

    /// <summary>ACC-02 / PAY-03: post the real party-based Payment (Direction In/Out).
    /// Money moves between the Cash/Bank till and the party's receivable/payable.</summary>
    public async Task<bool> PostPartyPaymentAsync(int paymentId)
    {
        var payment = await _dbContext.Payments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment == null)
        {
            LastError = $"Payment {paymentId} not found.";
            return false;
        }

        if (payment.Amount <= 0m)
            return true; // nothing to post

        if (await AlreadyPostedAsync("Payment", payment.Id.ToString()))
            return true;

        var isReceipt = string.Equals(payment.Direction, "In", StringComparison.OrdinalIgnoreCase);
        var partyPayable = string.Equals(payment.PartyType, "Supplier", StringComparison.OrdinalIgnoreCase)
            ? "Creditors" : "Debtors";

        // Bank mode posts against the Bank account by exact name; anything else (Cash,
        // Card, UPI, Split, ...) falls back to the Cash till.
        var accounts = await GetAccountsAsync("Cash", "Bank", partyPayable);
        var till = payment.PaymentMode == "Bank" && accounts.ContainsKey("Bank") ? "Bank" : "Cash";
        if (!Require(accounts, "Cash", partyPayable))
            return false;

        var entry = new JournalEntry
        {
            EntryDate = payment.PaymentDate,
            Narration = isReceipt
                ? $"Receipt {payment.PaymentNumber} from {payment.PartyName}"
                : $"Payment {payment.PaymentNumber} to {payment.PartyName}",
            ReferenceType = "Payment",
            ReferenceId = payment.Id.ToString(),
            Lines = new List<JournalLine>()
        };

        if (isReceipt)
        {
            AddLine(entry.Lines, accounts, till, payment.Amount, 0m, "Money received");
            AddLine(entry.Lines, accounts, partyPayable, 0m, payment.Amount, $"Settled {payment.PartyName}");
        }
        else
        {
            AddLine(entry.Lines, accounts, partyPayable, payment.Amount, 0m, $"Settled {payment.PartyName}");
            AddLine(entry.Lines, accounts, till, 0m, payment.Amount, "Money paid out");
        }

        return await PostAsync(entry);
    }

    /// <summary>ACC-02: post a POS receipt (part payment recorded against a Sale).</summary>
    public async Task<bool> PostSaleReceiptAsync(int saleId, decimal amount)
    {
        var sale = await _dbContext.Sales.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == saleId);
        if (sale == null)
        {
            LastError = $"Sale {saleId} not found.";
            return false;
        }

        if (amount <= 0m)
            return true; // nothing to post

        if (await AlreadyPostedAsync("SaleReceipt", saleId.ToString()))
            return true;

        var accounts = await GetAccountsAsync("Cash", "Debtors");
        if (!Require(accounts, "Cash", "Debtors"))
            return false;

        var entry = new JournalEntry
        {
            EntryDate = sale.SaleDate,
            Narration = $"POS receipt SA-{saleId}",
            ReferenceType = "SaleReceipt",
            ReferenceId = saleId.ToString(),
            Lines = new List<JournalLine>
            {
                new JournalLine { AccountId = accounts["Cash"].Id, Debit = amount, Credit = 0m, Narration = "Cash received" },
                new JournalLine { AccountId = accounts["Debtors"].Id, Debit = 0m, Credit = amount, Narration = "Sale receivable settled" }
            }
        };

        return await PostAsync(entry);
    }

    // ---------------------------------------------------------------- plumbing

    /// <summary>Idempotency guard: a journal already posted for this reference type + id.</summary>
    private Task<bool> AlreadyPostedAsync(string referenceType, string referenceId) =>
        _dbContext.JournalEntries.AsNoTracking()
            .AnyAsync(j => j.ReferenceType == referenceType && j.ReferenceId == referenceId);

    private async Task<Dictionary<string, Account>> GetAccountsAsync(params string[] names) =>
        (await _dbContext.Accounts.AsNoTracking()
            .Where(a => names.Contains(a.Name))
            .ToListAsync())
        .GroupBy(a => a.Name, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First());

    private bool Require(Dictionary<string, Account> accounts, params string[] names)
    {
        var missing = names.Where(n => !accounts.ContainsKey(n)).ToList();
        if (missing.Count == 0) return true;

        LastError = $"Chart of accounts is missing: {string.Join(", ", missing)}. Run the chart-of-accounts seed.";
        return false;
    }

    private static void AddLine(List<JournalLine> lines, Dictionary<string, Account> accounts,
        string accountName, decimal debit, decimal credit, string? narration)
    {
        // validation rejects 0-amount lines; drop them up front. A missing optional
        // account is tolerated — the balancing step keeps the entry correct.
        if (debit == 0m && credit == 0m) return;
        if (!accounts.TryGetValue(accountName, out var account)) return;

        lines.Add(new JournalLine
        {
            AccountId = account.Id,
            Debit = debit,
            Credit = credit,
            Narration = narration
        });
    }

    /// <summary>
    /// Assemble-and-post: balance the entry exactly (a nonzero residual is absorbed
    /// by the Round Off account), strip 0-amount lines (validation rejects them) and
    /// hand it to the journal service. Never throws; sets <see cref="LastError"/>.
    /// </summary>
    private async Task<bool> PostAsync(JournalEntry entry)
    {
        try
        {
            var residual = entry.Lines.Sum(l => l.Debit) - entry.Lines.Sum(l => l.Credit);
            if (residual != 0m)
            {
                var roundOff = await _dbContext.Accounts.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Name == "Round Off");
                if (roundOff == null)
                {
                    LastError = "Round Off account is missing; the entry cannot be balanced. Run the chart-of-accounts seed.";
                    return false;
                }

                entry.Lines.Add(new JournalLine
                {
                    AccountId = roundOff.Id,
                    Debit = residual > 0m ? 0m : Math.Abs(residual),
                    Credit = residual > 0m ? residual : 0m,
                    Narration = "Rounding adjustment"
                });
            }

            entry.Lines = entry.Lines.Where(l => l.Debit != 0m || l.Credit != 0m).ToList();
            if (entry.Lines.Count < 2)
            {
                LastError = "Nothing to post: the document produced fewer than two journal lines.";
                return false;
            }

            var ok = await _journalService.PostJournalEntryAsync(entry);
            LastError = ok ? null : (_journalService.LastValidationError ?? "Journal posting failed.");
            return ok;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }
}
