using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace RetailBillingApp.Services;

/// <summary>
/// SEC-06: TallyPrime XML import files + Excel-friendly CSV/SpreadsheetML exports.
/// XML is built exclusively with XElement/XAttribute so every user-supplied string is
/// escaped by the framework; a malformed invoice name can never break out of a node.
/// </summary>
public class TallyExportService : ITallyExportService
{
    private readonly AppDbContext _dbContext;
    private readonly IInvoiceService _invoiceService;
    private readonly IPurchaseService _purchaseService;
    private readonly IJournalService _journalService;
    private readonly IAccountService _accountService;
    private readonly IPaymentService _paymentService;

    public TallyExportService(AppDbContext dbContext, IInvoiceService invoiceService,
        IPurchaseService purchaseService, IJournalService journalService,
        IAccountService accountService, IPaymentService paymentService)
    {
        _dbContext = dbContext;
        _invoiceService = invoiceService;
        _purchaseService = purchaseService;
        _journalService = journalService;
        _accountService = accountService;
        _paymentService = paymentService;
    }

    /// <summary>
    /// Our chart-of-accounts group names -> TallyPrime standard group names. Anything
    /// unmapped falls back on the group's Type (Asset/Liability/...), which is why the
    /// dictionary only carries the names this app's users actually create.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> TallyGroupMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Assets"] = "Fixed Assets",
            ["Current Assets"] = "Current Assets",
            ["Fixed Assets"] = "Fixed Assets",
            ["Bank"] = "Bank Accounts",
            ["Bank Accounts"] = "Bank Accounts",
            ["Cash"] = "Cash-in-Hand",
            ["Cash and Bank"] = "Bank Accounts",
            ["Liabilities"] = "Current Liabilities",
            ["Current Liabilities"] = "Current Liabilities",
            ["Loans"] = "Loans (Advanced)",
            ["Loan"] = "Loans (Advanced)",
            ["Income"] = "Indirect Incomes",
            ["Indirect Income"] = "Indirect Incomes",
            ["Revenue"] = "Sales Accounts",
            ["Sales"] = "Sales Accounts",
            ["Sales Accounts"] = "Sales Accounts",
            ["Expenses"] = "Indirect Expenses",
            ["Indirect Expenses"] = "Indirect Expenses",
            ["Direct Expenses"] = "Direct Expenses",
            ["Direct Income"] = "Direct Incomes",
            ["Equity"] = "Capital Account",
            ["Capital"] = "Capital Account",
            ["Stock"] = "Stock-in-Hand",
            ["Inventory"] = "Stock-in-Hand",
            ["Purchase Accounts"] = "Purchase Accounts",
            ["Tax"] = "Duties & Taxes",
            ["Taxes"] = "Duties & Taxes",
            ["Duties & Taxes"] = "Duties & Taxes",
            ["Receivables"] = "Sundry Debtors",
            ["Payables"] = "Sundry Creditors",
            ["Sundry Debtors"] = "Sundry Debtors",
            ["Sundry Creditors"] = "Sundry Creditors",
        };

    internal static string MapToTallyGroup(Account account)
    {
        if (account.Group != null && TallyGroupMap.TryGetValue(account.Group.Name, out var mapped))
            return mapped;
        return FallbackGroup(account.Group?.Type ?? account.AccountType);
    }

    private static string FallbackGroup(string type) => type switch
    {
        "Asset" => "Current Assets",
        "Liability" => "Current Liabilities",
        "Income" => "Indirect Incomes",
        "Expense" => "Indirect Expenses",
        "Equity" => "Capital Account",
        _ => "Suspense",
    };

    // Duty / helper ledger names emitted by the voucher builders; masters export creates them
    // under "Duties & Taxes" so the vouchers resolve on import.
    private const string CgstOutput = "CGST Output";
    private const string SgstOutput = "SGST Output";
    private const string IgstOutput = "IGST Output";
    private const string CessOutput = "Cess Output";
    private const string GstInput = "GST Input";
    private const string RoundingOff = "Rounding Off";
    private const string CashLedger = "Cash";
    private const string BankLedger = "Bank Account";
    private const string CashSalesParty = "Cash Sales";

    private static readonly string[] CashBankGroups = { "Cash-in-Hand", "Bank Accounts" };

    private static string Fmt(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
    private static string FmtDate(DateTime date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    private static string FileNameDate(DateTime date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static bool InRange(DateTime date, DateTime from, DateTime to) =>
        date.Date >= from.Date && date.Date <= to.Date;

    // ---------------------------------------------------------------- envelope plumbing

    private static XElement Msg(params XElement[] children)
    {
        var message = new XElement("TALLYMESSAGE", new XAttribute(XNamespace.Xmlns + "UDF", "TallyUDF"));
        message.Add(children);
        return message;
    }

    private static XDocument Envelope(params (string ReportName, string Include, List<XElement> Messages)[] blocks)
    {
        var importData = new XElement("IMPORTDATA");
        foreach (var (reportName, include, messages) in blocks)
        {
            importData.Add(new XElement("REQUESTDESC", new XElement("REPORTNAME", reportName)));
            importData.Add(new XElement("REQUESTDATA", new XElement("TALLYINCLUDE", include)));
            importData.Add(messages);
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("ENVELOPE",
                new XElement("HEADER", new XElement("TALLYREQUEST", "Import Data")),
                new XElement("BODY", importData)));
    }

    private static string ResolveDirectory(string? targetDirectory) =>
        string.IsNullOrWhiteSpace(targetDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            : targetDirectory;

    private static async Task<TallyExportResult> SaveXmlAsync(TallyExportResult result, XDocument document,
        string prefix, DateTime from, DateTime to, string? targetDirectory)
    {
        var directory = ResolveDirectory(targetDirectory);
        Directory.CreateDirectory(directory);
        var suffix = to == DateTime.MinValue ? string.Empty : $"_{FileNameDate(from)}-{FileNameDate(to)}";
        var path = Path.Combine(directory, $"{prefix}{suffix}.xml");
        // XDocument.ToString() silently drops the <?xml ...?> declaration Tally expects,
        // so write through an XmlWriter configured to emit it.
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            Async = true,
        };
        using (var writer = XmlWriter.Create(path, settings))
        {
            document.Save(writer);
            await writer.FlushAsync();
        }

        result.Success = true;
        result.FilePath = path;
        result.XmlElementCount = document.Descendants().Count();
        return result;
    }

    private static async Task<TallyExportResult> SaveCsvAsync(TallyExportResult result, StringBuilder csv,
        string prefix, DateTime from, DateTime to, string? targetDirectory)
    {
        var directory = ResolveDirectory(targetDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{prefix}_{FileNameDate(from)}-{FileNameDate(to)}.csv");
        // BOM + CRLF: without them Excel mangles the rupee sign and joins rows on some readers.
        await File.WriteAllTextAsync(path, csv.ToString(), new UTF8Encoding(true));

        result.Success = true;
        result.FilePath = path;
        return result;
    }

    private static bool ValidateRange(DateTime from, DateTime to, TallyExportResult result)
    {
        if (from > to)
        {
            result.Error = $"Invalid date range: {from:yyyy-MM-dd} is after {to:yyyy-MM-dd}.";
            return false;
        }
        return true;
    }

    /// <summary>RFC4180: quote when the value contains a comma, quote or newline; double embedded quotes.</summary>
    private static string CsvField(string? value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static void CsvRow(StringBuilder sb, params string[] fields) =>
        sb.Append(string.Join(',', fields)).Append("\r\n");

    // ---------------------------------------------------------------- document loading

    private async Task<(List<Invoice> Exportable, TallyExportResult Result)> LoadSalesAsync(
        DateTime from, DateTime to, int? storeId, TallyExportResult result)
    {
        var invoices = await _invoiceService.GetAllInvoicesAsync();
        var exportable = new List<Invoice>();
        foreach (var invoice in invoices.Where(i => InRange(i.InvoiceDate, from, to))
                                        .Where(i => storeId == null || i.StoreId == storeId)
                                        .OrderBy(i => i.InvoiceDate))
        {
            if (invoice.IsEstimate)
            {
                result.SkippedCount++;
                result.Warnings.Add($"{invoice.InvoiceNumber}: estimate excluded - not a sale until converted.");
                continue;
            }
            if (string.Equals(invoice.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                result.SkippedCount++;
                result.Warnings.Add($"{invoice.InvoiceNumber}: cancelled invoice excluded.");
                continue;
            }
            if (invoice.TotalAmount <= 0)
            {
                result.SkippedCount++;
                result.Warnings.Add($"{invoice.InvoiceNumber}: zero/negative total excluded.");
                continue;
            }
            exportable.Add(invoice);
        }
        return (exportable, result);
    }

    private async Task<(List<Purchase> Exportable, TallyExportResult Result)> LoadPurchasesAsync(
        DateTime from, DateTime to, TallyExportResult result)
    {
        var purchases = await _purchaseService.GetAllPurchasesAsync();
        foreach (var purchase in purchases.Where(p => InRange(p.PurchaseDate, from, to) && p.TotalAmount <= 0))
        {
            result.SkippedCount++;
            result.Warnings.Add($"{purchase.PurchaseNumber}: zero/negative total excluded.");
        }
        var exportable = purchases
            .Where(p => InRange(p.PurchaseDate, from, to) && p.TotalAmount > 0)
            .OrderBy(p => p.PurchaseDate)
            .ToList();
        return (exportable, result);
    }

    private async Task<(List<JournalEntry> Exportable, TallyExportResult Result)> LoadJournalsAsync(
        DateTime from, DateTime to, TallyExportResult result)
    {
        var entries = await _journalService.GetAllJournalEntriesAsync();
        var exportable = new List<JournalEntry>();
        foreach (var entry in entries.Where(e => InRange(e.EntryDate, from, to)).OrderBy(e => e.EntryDate))
        {
            var lines = entry.Lines ?? new List<JournalLine>();
            if (lines.Count == 0)
            {
                result.SkippedCount++;
                result.Warnings.Add($"Journal entry dated {entry.EntryDate:yyyy-MM-dd}: no lines, excluded.");
                continue;
            }
            var debit = lines.Sum(l => l.Debit);
            var credit = lines.Sum(l => l.Credit);
            if (Math.Abs(debit - credit) > 0.005m)
            {
                result.SkippedCount++;
                result.Warnings.Add($"Journal entry dated {entry.EntryDate:yyyy-MM-dd} is unbalanced " +
                    $"(Dr {debit:F2} / Cr {credit:F2}) - Tally rejects unbalanced vouchers, excluded.");
                continue;
            }
            exportable.Add(entry);
        }
        return (exportable, result);
    }

    private async Task<(List<Payment> Exportable, TallyExportResult Result)> LoadPaymentsAsync(
        DateTime from, DateTime to, int? storeId, TallyExportResult result)
    {
        var payments = await _paymentService.GetAllPaymentsAsync();
        var exportable = new List<Payment>();
        foreach (var payment in payments.Where(p => InRange(p.PaymentDate, from, to))
                                        .Where(p => storeId == null || p.StoreId == storeId)
                                        .OrderBy(p => p.PaymentDate))
        {
            if (payment.Amount <= 0)
            {
                result.SkippedCount++;
                result.Warnings.Add($"{payment.PaymentNumber}: zero amount excluded.");
                continue;
            }
            if (payment.Splits.Any(s => string.Equals(s.Mode, "Credit", StringComparison.OrdinalIgnoreCase)))
            {
                result.SkippedCount++;
                result.Warnings.Add($"{payment.PaymentNumber}: contains an unsettled Credit split - " +
                    "not a real cash movement yet, excluded.");
                continue;
            }
            exportable.Add(payment);
        }
        return (exportable, result);
    }

    // ---------------------------------------------------------------- voucher builders

    private static void AddDuty(XElement voucher, string ledger, decimal amount)
    {
        if (amount <= 0) return;
        voucher.Add(new XElement("DUTYLEDGERS",
            new XElement("LEDGERNAME", ledger),
            new XElement("DRCRATETOTAL", Fmt(amount)),
            new XElement("ISREVERSEPOSTING", "Yes")));
    }

    private static XElement BuildSalesMessage(Invoice invoice, List<string> warnings)
    {
        var party = string.IsNullOrWhiteSpace(invoice.CustomerName) ? CashSalesParty : invoice.CustomerName!;
        if (string.IsNullOrWhiteSpace(invoice.CustomerGSTIN) && !invoice.IsBillOfSupply)
            warnings.Add($"{invoice.InvoiceNumber}: party has no GSTIN - the ledger imports without GST tagging.");

        var voucher = new XElement("VOUCHER",
            new XAttribute("VCHTYPE", "Sales"),
            new XAttribute("ACTION", "Create"),
            new XAttribute("OBJVIEW", "Sales Voucher"),
            new XElement("DATE", FmtDate(invoice.InvoiceDate)),
            new XElement("EFFECTIVEDATE", FmtDate(invoice.InvoiceDate)),
            new XElement("VOUCHERNUMBER", invoice.InvoiceNumber),
            new XElement("AUTHOR", "RetailBillingApp"),
            new XElement("NAMES", party),
            new XElement("PARTYLEDGERNAME", party),
            new XElement("ISINVOICE", "Yes"),
            new XElement("NARRATION", string.IsNullOrWhiteSpace(invoice.Notes)
                ? $"Sale {invoice.InvoiceNumber}" : invoice.Notes!));

        foreach (var item in invoice.Items)
        {
            var entry = new XElement("ALLINVLEDGERS",
                new XElement("LEDGERNAME", item.ProductName),
                new XElement("DRCRATETOTAL", Fmt(item.Amount)),
                new XElement("ISDEEMEDPOSITIVE", "Yes"),
                new XElement("ENTRYVALUETYPE", "Invoiced"),
                new XElement("INCLUSIONTYPE", "In"),
                new XElement("RATEOFDESC", Fmt(item.DiscountPct)));
            // HSN cannot ride a sales BATCH (the app does not track batch on invoice lines);
            // it is exported on the stock-item master instead.
            if (!string.IsNullOrWhiteSpace(item.HSNCode))
                entry.Add(new XElement("HSNCODE", item.HSNCode!));
            voucher.Add(entry);
        }

        if (invoice.IsBillOfSupply)
        {
            warnings.Add($"{invoice.InvoiceNumber}: Bill of Supply exported with no duty lines " +
                "(non-GST sale in Tally terms).");
        }
        else
        {
            var intra = invoice.CGST > 0 || invoice.SGST > 0;
            var inter = invoice.IGST > 0;
            if (intra && inter)
                warnings.Add($"{invoice.InvoiceNumber}: carries both CGST/SGST and IGST - Tally expects " +
                    "one supply type per voucher; verify the split after import.");
            AddDuty(voucher, CgstOutput, invoice.CGST);
            AddDuty(voucher, SgstOutput, invoice.SGST);
            AddDuty(voucher, IgstOutput, invoice.IGST);
            if (invoice.Cess > 0)
            {
                AddDuty(voucher, CessOutput, invoice.Cess);
                warnings.Add($"{invoice.InvoiceNumber}: Cess exported as one '{CessOutput}' ledger; for " +
                    "intra-state supplies Tally splits it into CGST Cess / SGST Cess.");
            }
            if (invoice.PlaceOfSupply is null or { Length: 0 } && !string.IsNullOrWhiteSpace(invoice.CustomerGSTIN))
                warnings.Add($"{invoice.InvoiceNumber}: no Place of Supply on the invoice.");
        }

        if (invoice.RoundOff != 0)
        {
            voucher.Add(new XElement("ALLLEDGERENTRIES",
                new XElement("LEDGERNAME", RoundingOff),
                new XElement("DRCRATETOTAL", Fmt(Math.Abs(invoice.RoundOff))),
                new XElement("ISDEEMEDPOSITIVE", invoice.RoundOff < 0 ? "Yes" : "No")));
        }

        voucher.Add(new XElement("TOTALDRCR", Fmt(invoice.TotalAmount)));
        return Msg(voucher);
    }

    private static XElement BuildPurchaseMessage(Purchase purchase, List<string> warnings)
    {
        var party = string.IsNullOrWhiteSpace(purchase.SupplierName) ? "Unregistered Supplier" : purchase.SupplierName!;
        var voucher = new XElement("VOUCHER",
            new XAttribute("VCHTYPE", "Purchase"),
            new XAttribute("ACTION", "Create"),
            new XAttribute("OBJVIEW", "Purchase Voucher"),
            new XElement("DATE", FmtDate(purchase.PurchaseDate)),
            new XElement("EFFECTIVEDATE", FmtDate(purchase.PurchaseDate)),
            new XElement("VOUCHERNUMBER", purchase.PurchaseNumber),
            new XElement("AUTHOR", "RetailBillingApp"),
            new XElement("NAMES", party),
            new XElement("PARTYLEDGERNAME", party),
            new XElement("ISINVOICE", "Yes"),
            new XElement("NARRATION", string.IsNullOrWhiteSpace(purchase.Notes)
                ? $"Purchase {purchase.PurchaseNumber}" : purchase.Notes!));

        foreach (var item in purchase.Items)
        {
            var entry = new XElement("ALLINVLEDGERS",
                new XElement("LEDGERNAME", item.ProductName),
                new XElement("DRCRATETOTAL", Fmt(item.Amount)),
                new XElement("ISDEEMEDPOSITIVE", "Yes"),
                new XElement("ENTRYVALUETYPE", "Invoiced"),
                new XElement("INCLUSIONTYPE", "In"));
            if (!string.IsNullOrWhiteSpace(item.HSNCode))
                entry.Add(new XElement("HSNCODE", item.HSNCode!));
            if (!string.IsNullOrWhiteSpace(item.Batch))
            {
                var batch = new XElement("BATCH", new XElement("NAME", item.Batch!));
                if (item.ExpiryDate.HasValue)
                    batch.Add(new XElement("EXPDT", FmtDate(item.ExpiryDate.Value)));
                entry.Add(batch);
            }
            voucher.Add(entry);
        }

        if (purchase.TaxAmount > 0)
        {
            AddDuty(voucher, GstInput, purchase.TaxAmount);
            warnings.Add($"{purchase.PurchaseNumber}: the app stores purchase GST as one figure - exported " +
                $"as a single '{GstInput}' duty line; split into ITC-eligible CGST/SGST/IGST in Tally.");
        }

        voucher.Add(new XElement("TOTALDRCR", Fmt(purchase.TotalAmount)));
        return Msg(voucher);
    }

    private static XElement BuildJournalMessage(JournalEntry entry, List<string> warnings)
    {
        var accountGroups = entry.Lines
            .Where(l => l.Account != null)
            .Select(l => MapToTallyGroup(l.Account!))
            .ToList();
        var isContra = entry.Lines.Count >= 2 && accountGroups.All(g => CashBankGroups.Contains(g));

        var voucher = new XElement("VOUCHER",
            new XAttribute("VCHTYPE", isContra ? "Contra" : "Journal"),
            new XAttribute("ACTION", "Create"),
            new XAttribute("OBJVIEW", "Accounting Voucher"),
            new XElement("DATE", FmtDate(entry.EntryDate)),
            new XElement("EFFECTIVEDATE", FmtDate(entry.EntryDate)),
            new XElement("AUTHOR", "RetailBillingApp"),
            new XElement("NARRATION", string.IsNullOrWhiteSpace(entry.Narration)
                ? $"Journal {entry.EntryDate:yyyy-MM-dd}" : entry.Narration!));

        foreach (var line in entry.Lines)
        {
            if (line.Account == null)
            {
                warnings.Add($"Journal entry {entry.EntryDate:yyyy-MM-dd}: a line references a deleted " +
                    "account - line skipped.");
                continue;
            }
            if (line.Account.Group == null)
                warnings.Add($"Account '{line.Account.Name}' has no group - mapped from its account " +
                    $"type to '{MapToTallyGroup(line.Account)}'.");

            voucher.Add(new XElement("ALLLEDGERENTRIES",
                new XElement("LEDGERNAME", line.Account.Name),
                new XElement("DRCRATETOTAL", Fmt(Math.Max(line.Debit, line.Credit))),
                new XElement("ISDEEMEDPOSITIVE", line.Debit > 0 ? "Yes" : "No")));
        }

        voucher.Add(new XElement("TOTALDRCR", Fmt(entry.Lines.Sum(l => l.Debit))));
        return Msg(voucher);
    }

    private static string ModeLedger(string mode) =>
        string.Equals(mode, "Cash", StringComparison.OrdinalIgnoreCase) ? CashLedger : BankLedger;

    private static XElement BuildPaymentMessage(Payment payment, List<string> warnings)
    {
        var isReceipt = string.Equals(payment.Direction, "In", StringComparison.OrdinalIgnoreCase);
        var party = string.IsNullOrWhiteSpace(payment.PartyName) ? CashSalesParty : payment.PartyName!;

        var voucher = new XElement("VOUCHER",
            new XAttribute("VCHTYPE", isReceipt ? "Receipt" : "Payment"),
            new XAttribute("ACTION", "Create"),
            new XAttribute("OBJVIEW", isReceipt ? "Receipt Voucher" : "Payment Voucher"),
            new XElement("DATE", FmtDate(payment.PaymentDate)),
            new XElement("EFFECTIVEDATE", FmtDate(payment.PaymentDate)),
            new XElement("VOUCHERNUMBER", payment.PaymentNumber),
            new XElement("AUTHOR", "RetailBillingApp"),
            new XElement("NAMES", party),
            new XElement("PARTYLEDGERNAME", party),
            new XElement("NARRATION", string.IsNullOrWhiteSpace(payment.Reference)
                ? $"{(isReceipt ? "Receipt" : "Payment")} {payment.PaymentNumber}" : payment.Reference!));

        var settleLines = payment.Splits.Count > 0
            ? payment.Splits.Select(s => (s.Mode, s.Amount)).ToList()
            : new List<(string Mode, decimal Amount)> { (payment.PaymentMode, payment.Amount) };
        if (payment.Splits.Count > 0)
            warnings.Add($"{payment.PaymentNumber}: split settlement exported as one entry per split line.");

        foreach (var (mode, amount) in settleLines)
        {
            var ledger = ModeLedger(mode);
            if (ledger == BankLedger && !string.Equals(mode, "Cash", StringComparison.OrdinalIgnoreCase))
                voucher.Add(new XElement("BANKALLOCATIONS",
                    new XElement("LEDGERNAME", BankLedger),
                    new XElement("DRCRATETOTAL", Fmt(amount)),
                    new XElement("PAYMODE", mode)));
            voucher.Add(new XElement("ALLLEDGERENTRIES",
                new XElement("LEDGERNAME", ledger),
                new XElement("DRCRATETOTAL", Fmt(amount)),
                // a receipt debits cash/bank, a payment credits it
                new XElement("ISDEEMEDPOSITIVE", isReceipt ? "Yes" : "No")));
        }

        voucher.Add(new XElement("ALLLEDGERENTRIES",
            new XElement("LEDGERNAME", party),
            new XElement("DRCRATETOTAL", Fmt(payment.Amount)),
            new XElement("ISDEEMEDPOSITIVE", isReceipt ? "No" : "Yes")));

        voucher.Add(new XElement("TOTALDRCR", Fmt(payment.Amount)));
        return Msg(voucher);
    }

    // ---------------------------------------------------------------- masters builder

    private async Task<List<XElement>> BuildMasterMessagesAsync(List<string> warnings)
    {
        var messages = new List<XElement>();

        var groups = await _dbContext.AccountGroups.AsNoTracking().ToListAsync();
        foreach (var group in groups)
        {
            if (TallyGroupMap.TryGetValue(group.Name, out var standard) &&
                string.Equals(standard, group.Name, StringComparison.OrdinalIgnoreCase))
                continue; // Tally already ships this standard group; recreating it only causes prompts.

            var parent = group.Type switch
            {
                "Income" or "Expense" => "Profit & Loss A/c",
                "Equity" => "Capital Account",
                _ => "Balance Sheet",
            };
            messages.Add(Msg(new XElement("GROUP",
                new XAttribute("ACTION", "Create"),
                new XAttribute("NAME", group.Name),
                new XElement("PARENT", TallyGroupMap.TryGetValue(group.Name, out var p) ? p : parent))));
        }

        var accounts = await _accountService.GetAllAccountsAsync();
        foreach (var account in accounts.Where(a => a.IsActive))
        {
            var tallyGroup = MapToTallyGroup(account);
            if (account.Group == null)
                warnings.Add($"Account '{account.Name}' has no group - master created under '{tallyGroup}'.");
            messages.Add(Msg(new XElement("LEDGER",
                new XAttribute("ACTION", "Create"),
                new XAttribute("NAME", account.Name),
                new XElement("PARENT", tallyGroup))));
        }

        foreach (var (ledger, parent) in new[]
        {
            (CgstOutput, "Duties & Taxes"), (SgstOutput, "Duties & Taxes"), (IgstOutput, "Duties & Taxes"),
            (CessOutput, "Duties & Taxes"), (GstInput, "Duties & Taxes"), (RoundingOff, "Indirect Expenses"),
            (CashLedger, "Cash-in-Hand"), (BankLedger, "Bank Accounts"), (CashSalesParty, "Sundry Debtors"),
        })
        {
            messages.Add(Msg(new XElement("LEDGER",
                new XAttribute("ACTION", "Create"),
                new XAttribute("NAME", ledger),
                new XElement("PARENT", parent))));
        }

        var customers = await _dbContext.Customers.AsNoTracking().ToListAsync();
        foreach (var customer in customers)
            messages.Add(Msg(new XElement("LEDGER",
                new XAttribute("ACTION", "Create"),
                new XAttribute("NAME", customer.Name),
                new XElement("PARENT", "Sundry Debtors"))));

        var suppliers = await _dbContext.Suppliers.AsNoTracking()
            .Where(s => s.IsActive).ToListAsync();
        foreach (var supplier in suppliers)
            messages.Add(Msg(new XElement("LEDGER",
                new XAttribute("ACTION", "Create"),
                new XAttribute("NAME", supplier.Name),
                new XElement("PARENT", "Sundry Creditors"))));

        var categories = await _dbContext.Categories.AsNoTracking().ToListAsync();
        foreach (var category in categories)
            messages.Add(Msg(new XElement("STOCKCATEGORY",
                new XAttribute("ACTION", "Create"),
                new XAttribute("NAME", category.Name),
                new XElement("PARENT", "Primary"))));

        var products = await _dbContext.Products.AsNoTracking()
            .Include(p => p.Unit).ToListAsync();
        var defaultUnitWarned = false;
        foreach (var product in products)
        {
            var unit = product.Unit?.Symbol ?? product.Unit?.Name;
            if (string.IsNullOrWhiteSpace(unit))
            {
                unit = "Nos";
                if (!defaultUnitWarned)
                {
                    warnings.Add("Products without a unit are exported with base unit 'Nos' - create " +
                        "matching units in Tally or remap on import.");
                    defaultUnitWarned = true;
                }
            }
            var stockItem = new XElement("STOCKITEM",
                new XAttribute("ACTION", "Create"),
                new XAttribute("NAME", product.Name),
                new XElement("CATEGORY", product.Category ?? "Primary"),
                new XElement("BASEUNITS", unit!));
            if (!string.IsNullOrWhiteSpace(product.HSNCode))
                stockItem.Add(new XElement("HSNSYNCODE", product.HSNCode!));
            messages.Add(Msg(stockItem));
        }
        return messages;
    }

    // ---------------------------------------------------------------- public XML exports

    public async Task<TallyExportResult> ExportSalesXmlAsync(DateTime from, DateTime to,
        int? storeId = null, string? targetDirectory = null)
    {
        var result = new TallyExportResult();
        try
        {
            if (!ValidateRange(from, to, result)) return result;
            var (invoices, _) = await LoadSalesAsync(from, to, storeId, result);
            if (invoices.Count == 0)
            {
                result.Error = $"No saleable invoices between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.";
                return result;
            }
            var messages = invoices.Select(i => BuildSalesMessage(i, result.Warnings)).ToList();
            result.ExportedCount = messages.Count;
            var document = Envelope(("Vouchers", "All Vouchers", messages));
            return await SaveXmlAsync(result, document, "TallySales", from, to, targetDirectory);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    public async Task<TallyExportResult> ExportPurchaseXmlAsync(DateTime from, DateTime to,
        string? targetDirectory = null)
    {
        var result = new TallyExportResult();
        try
        {
            if (!ValidateRange(from, to, result)) return result;
            var (purchases, _) = await LoadPurchasesAsync(from, to, result);
            if (purchases.Count == 0)
            {
                result.Error = $"No purchases between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.";
                return result;
            }
            var messages = purchases.Select(p => BuildPurchaseMessage(p, result.Warnings)).ToList();
            result.ExportedCount = messages.Count;
            var document = Envelope(("Vouchers", "All Vouchers", messages));
            return await SaveXmlAsync(result, document, "TallyPurchase", from, to, targetDirectory);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    public async Task<TallyExportResult> ExportJournalXmlAsync(DateTime from, DateTime to,
        string? targetDirectory = null)
    {
        var result = new TallyExportResult();
        try
        {
            if (!ValidateRange(from, to, result)) return result;
            var (entries, _) = await LoadJournalsAsync(from, to, result);
            if (entries.Count == 0)
            {
                result.Error = $"No balanced journal entries between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.";
                return result;
            }
            var messages = entries.Select(e => BuildJournalMessage(e, result.Warnings)).ToList();
            result.ExportedCount = messages.Count;
            var document = Envelope(("Vouchers", "All Vouchers", messages));
            return await SaveXmlAsync(result, document, "TallyJournal", from, to, targetDirectory);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    public async Task<TallyExportResult> ExportPaymentReceiptXmlAsync(DateTime from, DateTime to,
        int? storeId = null, string? targetDirectory = null)
    {
        var result = new TallyExportResult();
        try
        {
            if (!ValidateRange(from, to, result)) return result;
            var (payments, _) = await LoadPaymentsAsync(from, to, storeId, result);
            if (payments.Count == 0)
            {
                result.Error = $"No payments or receipts between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.";
                return result;
            }
            var messages = payments.Select(p => BuildPaymentMessage(p, result.Warnings)).ToList();
            result.ExportedCount = messages.Count;
            var document = Envelope(("Vouchers", "All Vouchers", messages));
            return await SaveXmlAsync(result, document, "TallyPayments", from, to, targetDirectory);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    public async Task<TallyExportResult> ExportMastersXmlAsync(string? targetDirectory = null)
    {
        var result = new TallyExportResult();
        try
        {
            var messages = await BuildMasterMessagesAsync(result.Warnings);
            result.ExportedCount = messages.Count;
            var document = Envelope(("Masters", "All Masters", messages));
            return await SaveXmlAsync(result, document, "TallyMasters", DateTime.Today, DateTime.MinValue,
                targetDirectory);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    public async Task<TallyExportResult> ExportCombinedXmlAsync(DateTime from, DateTime to,
        int? storeId = null, string? targetDirectory = null)
    {
        var result = new TallyExportResult();
        try
        {
            if (!ValidateRange(from, to, result)) return result;

            var (invoices, _) = await LoadSalesAsync(from, to, storeId, result);
            var (purchases, _) = await LoadPurchasesAsync(from, to, result);
            var (journals, _) = await LoadJournalsAsync(from, to, result);
            var (payments, _) = await LoadPaymentsAsync(from, to, storeId, result);

            var total = invoices.Count + purchases.Count + journals.Count + payments.Count;
            if (total == 0)
            {
                result.Error = $"No documents of any type between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.";
                return result;
            }

            var masterMessages = await BuildMasterMessagesAsync(result.Warnings);
            var voucherMessages = new List<XElement>();
            voucherMessages.AddRange(invoices.Select(i => BuildSalesMessage(i, result.Warnings)));
            voucherMessages.AddRange(purchases.Select(p => BuildPurchaseMessage(p, result.Warnings)));
            voucherMessages.AddRange(journals.Select(e => BuildJournalMessage(e, result.Warnings)));
            voucherMessages.AddRange(payments.Select(p => BuildPaymentMessage(p, result.Warnings)));

            result.ExportedCount = voucherMessages.Count;
            var document = Envelope(
                ("Masters", "All Masters", masterMessages),
                ("Vouchers", "All Vouchers", voucherMessages));
            return await SaveXmlAsync(result, document, "TallyCombined", from, to, targetDirectory);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    // ---------------------------------------------------------------- CSV exports

    public async Task<TallyExportResult> ExportSalesCsvAsync(DateTime from, DateTime to,
        int? storeId = null, string? targetDirectory = null)
    {
        var result = new TallyExportResult();
        try
        {
            if (!ValidateRange(from, to, result)) return result;
            var (invoices, _) = await LoadSalesAsync(from, to, storeId, result);
            if (invoices.Count == 0)
            {
                result.Error = $"No saleable invoices between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.";
                return result;
            }

            var sb = new StringBuilder();
            CsvRow(sb, "Sales", "Invoice No", "Date", "Customer", "GSTIN", "Taxable",
                "CGST", "SGST", "IGST", "Cess", "Total", "Mode", "Status");
            foreach (var i in invoices)
            {
                CsvRow(sb, CsvField(i.TypeLabel), CsvField(i.InvoiceNumber),
                    i.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    CsvField(i.CustomerName ?? CashSalesParty), i.CustomerGSTIN ?? string.Empty,
                    Fmt(i.SubTotal), Fmt(i.CGST), Fmt(i.SGST), Fmt(i.IGST), Fmt(i.Cess),
                    Fmt(i.TotalAmount), CsvField(i.PaymentMode), CsvField(i.Status));
                result.ExportedCount++;
            }
            return await SaveCsvAsync(result, sb, "TallySales", from, to, targetDirectory);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    public async Task<TallyExportResult> ExportPurchaseCsvAsync(DateTime from, DateTime to,
        string? targetDirectory = null)
    {
        var result = new TallyExportResult();
        try
        {
            if (!ValidateRange(from, to, result)) return result;
            var (purchases, _) = await LoadPurchasesAsync(from, to, result);
            if (purchases.Count == 0)
            {
                result.Error = $"No purchases between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.";
                return result;
            }

            var sb = new StringBuilder();
            CsvRow(sb, "Purchase", "Bill No", "Date", "Supplier", "GSTIN", "Taxable",
                "GST", "Total", "Mode", "Status");
            foreach (var p in purchases)
            {
                CsvRow(sb, CsvField("Purchase"), CsvField(p.PurchaseNumber),
                    p.PurchaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    CsvField(p.SupplierName ?? "Unregistered Supplier"),
                    p.Supplier?.GSTIN ?? string.Empty,
                    Fmt(p.SubTotal), Fmt(p.TaxAmount), Fmt(p.TotalAmount),
                    CsvField(p.PaymentMode), CsvField(p.Status));
                result.ExportedCount++;
            }
            return await SaveCsvAsync(result, sb, "TallyPurchase", from, to, targetDirectory);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    private record DayBookRow(DateTime Date, string Type, string DocumentNo, string Party, string GstIn,
        string Taxable, string Cgst, string Sgst, string Igst, string Cess, string Total, string Mode, string Status);

    private async Task<List<DayBookRow>> LoadDayBookRowsAsync(DateTime from, DateTime to, int? storeId,
        TallyExportResult result)
    {
        var (invoices, _) = await LoadSalesAsync(from, to, storeId, result);
        var (purchases, _) = await LoadPurchasesAsync(from, to, result);
        var (payments, _) = await LoadPaymentsAsync(from, to, storeId, result);

        var rows = new List<DayBookRow>();
        foreach (var i in invoices)
            rows.Add(new DayBookRow(i.InvoiceDate, i.TypeLabel, i.InvoiceNumber,
                i.CustomerName ?? CashSalesParty, i.CustomerGSTIN ?? string.Empty,
                Fmt(i.SubTotal), Fmt(i.CGST), Fmt(i.SGST), Fmt(i.IGST), Fmt(i.Cess),
                Fmt(i.TotalAmount), i.PaymentMode, i.Status));
        foreach (var p in purchases)
            rows.Add(new DayBookRow(p.PurchaseDate, "Purchase", p.PurchaseNumber,
                p.SupplierName ?? "Unregistered Supplier", p.Supplier?.GSTIN ?? string.Empty,
                Fmt(p.SubTotal), "-", "-", "-", "-", Fmt(p.TotalAmount), p.PaymentMode, p.Status));
        foreach (var pay in payments)
            rows.Add(new DayBookRow(pay.PaymentDate,
                string.Equals(pay.Direction, "In", StringComparison.OrdinalIgnoreCase) ? "Receipt" : "Payment",
                pay.PaymentNumber, pay.PartyName ?? CashSalesParty, string.Empty,
                "-", "-", "-", "-", "-", Fmt(pay.Amount), pay.PaymentMode, string.Empty));

        result.ExportedCount = rows.Count;
        return rows.OrderBy(r => r.Date).ThenBy(r => r.DocumentNo).ToList();
    }

    public async Task<TallyExportResult> ExportDayBookCsvAsync(DateTime from, DateTime to,
        int? storeId = null, string? targetDirectory = null)
    {
        var result = new TallyExportResult();
        try
        {
            if (!ValidateRange(from, to, result)) return result;
            var rows = await LoadDayBookRowsAsync(from, to, storeId, result);
            if (rows.Count == 0)
            {
                result.Error = $"Nothing to show in the day book between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.";
                return result;
            }

            var sb = new StringBuilder();
            CsvRow(sb, "Date", "Type", "Document No", "Party", "GSTIN", "Taxable",
                "CGST", "SGST", "IGST", "Cess", "Total", "Mode", "Status");
            foreach (var r in rows)
                CsvRow(sb, r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    CsvField(r.Type), CsvField(r.DocumentNo), CsvField(r.Party), CsvField(r.GstIn),
                    r.Taxable, r.Cgst, r.Sgst, r.Igst, r.Cess, r.Total, CsvField(r.Mode), CsvField(r.Status));
            return await SaveCsvAsync(result, sb, "TallyDayBook", from, to, targetDirectory);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    public async Task<TallyExportResult> ExportDayBookSpreadsheetXmlAsync(DateTime from, DateTime to,
        int? storeId = null, string? targetDirectory = null)
    {
        var result = new TallyExportResult();
        try
        {
            if (!ValidateRange(from, to, result)) return result;
            var rows = await LoadDayBookRowsAsync(from, to, storeId, result);
            if (rows.Count == 0)
            {
                result.Error = $"Nothing to show in the day book between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.";
                return result;
            }

            var ss = XNamespace.Get("urn:schemas-microsoft-com:office:spreadsheet");
            var excel = XNamespace.Get("urn:schemas-microsoft-com:office:excel");

            XElement Cell(string value, bool numeric) =>
                new(ss + "Cell", new XElement(ss + "Data",
                    new XAttribute(ss + "Type", numeric ? "Number" : "String"), value));

            var header = new XElement(ss + "Row",
                Cell("Date", false), Cell("Type", false), Cell("Document No", false), Cell("Party", false),
                Cell("Taxable", true), Cell("Total", true), Cell("Mode", false));
            var body = rows.Select(r => new XElement(ss + "Row",
                Cell(r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), false),
                Cell(r.Type, false), Cell(r.DocumentNo, false), Cell(r.Party, false),
                Cell(r.Taxable, true), Cell(r.Total, true), Cell(r.Mode, false)));

            var workbook = new XElement(ss + "Workbook",
                new XAttribute(XNamespace.Xmlns + "ss", ss.NamespaceName),
                new XAttribute(excel + "Author", "RetailBillingApp"),
                new XElement(ss + "Worksheet",
                    new XAttribute(ss + "Name", "Day Book"),
                    new XElement(ss + "Table", new XElement(ss + "Column",
                        new XAttribute(ss + "AutoWidth", "1")), header, body)));

            var document = new XDocument(new XDeclaration("1.0", "utf-8", "no"), workbook);
            result.Warnings.Add("SpreadsheetML .xml opens directly in Excel; a true .xlsx container would " +
                "require a zip/NuGet package this build is not allowed to add.");
            return await SaveXmlAsync(result, document, "DayBook", from, to, targetDirectory);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    // ---------------------------------------------------------------- preview

    public async Task<TallyPreviewResult> PreviewAsync(TallyDocumentType docType, TallyExportFormat format,
        DateTime from, DateTime to, int? storeId = null)
    {
        var preview = new TallyPreviewResult();
        try
        {
            if (from > to)
            {
                preview.Error = "Start date is after end date.";
                return preview;
            }
            var result = new TallyExportResult();
            switch (docType)
            {
                case TallyDocumentType.Sales:
                {
                    var (invoices, _) = await LoadSalesAsync(from, to, storeId, result);
                    preview.DocumentCount = invoices.Count;
                    preview.SkippedCount = result.SkippedCount;
                    break;
                }
                case TallyDocumentType.Purchases:
                {
                    var (purchases, _) = await LoadPurchasesAsync(from, to, result);
                    preview.DocumentCount = purchases.Count;
                    preview.SkippedCount = result.SkippedCount;
                    break;
                }
                case TallyDocumentType.Journal:
                {
                    var (entries, _) = await LoadJournalsAsync(from, to, result);
                    preview.DocumentCount = entries.Count;
                    preview.SkippedCount = result.SkippedCount;
                    break;
                }
                case TallyDocumentType.PaymentsReceipts:
                {
                    var (payments, _) = await LoadPaymentsAsync(from, to, storeId, result);
                    preview.DocumentCount = payments.Count;
                    preview.SkippedCount = result.SkippedCount;
                    break;
                }
                case TallyDocumentType.Masters:
                {
                    var accounts = await _dbContext.Accounts.AsNoTracking().CountAsync(a => a.IsActive);
                    var groups = await _dbContext.AccountGroups.AsNoTracking().CountAsync();
                    var customers = await _dbContext.Customers.AsNoTracking().CountAsync();
                    var suppliers = await _dbContext.Suppliers.AsNoTracking().CountAsync(s => s.IsActive);
                    var products = await _dbContext.Products.AsNoTracking().CountAsync();
                    preview.DocumentCount = accounts + groups + customers + suppliers + products;
                    break;
                }
                case TallyDocumentType.Combined:
                {
                    if (format == TallyExportFormat.Csv)
                    {
                        var rows = await LoadDayBookRowsAsync(from, to, storeId, result);
                        preview.DocumentCount = rows.Count;
                    }
                    else
                    {
                        var (invoices, _) = await LoadSalesAsync(from, to, storeId, result);
                        var (purchases, _) = await LoadPurchasesAsync(from, to, result);
                        var (journals, _) = await LoadJournalsAsync(from, to, result);
                        var (payments, _) = await LoadPaymentsAsync(from, to, storeId, result);
                        preview.DocumentCount = invoices.Count + purchases.Count + journals.Count + payments.Count;
                    }
                    preview.SkippedCount = result.SkippedCount;
                    break;
                }
            }
            preview.Warnings.AddRange(result.Warnings);
            return preview;
        }
        catch (Exception ex)
        {
            preview.Error = ex.Message;
            return preview;
        }
    }
}
