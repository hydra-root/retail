using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.Globalization;
using System.IO;
using System.Text;

namespace RetailBillingApp.Services;

/// <summary>
/// INT-01: unified contacts / address book. The book is *derived* from Customers and
/// Suppliers on every load (no Contact table), vCard export lands as a UTF-8 BOM .vcf on
/// the Desktop like the other exporters, and CSV import re-uses the STK-12 parsing style
/// (quote-aware, flexible headers, preview then commit) while creating parties through
/// ICustomerService / ISupplierService so their validation and audits still run.
/// </summary>
public class ContactService : IContactService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuthService _authService;
    private readonly IAuditService _auditService;
    private readonly ICustomerService _customerService;
    private readonly ISupplierService _supplierService;
    private readonly IReportService _reportService;
    private readonly IMessagingService _messagingService;

    public List<string> PartyTypes { get; } =
        new() { ContactParties.All, ContactParties.Customer, ContactParties.Supplier };

    public string? LastValidationError { get; private set; }

    public List<string> Warnings { get; } = new();

    /// <summary>Field lengths from the Customer / Supplier model annotations - a longer CSV
    /// cell would fail inside the sibling service's swallowed catch, so the row is rejected
    /// with a readable reason instead.</summary>
    private const int NameMax = 200;
    private const int PhoneMax = 20;
    private const int EmailMax = 200;
    private const int AddressMax = 500;
    private const int StateMax = 100;
    private const int GroupMax = 100;

    /// <summary>vCard 3.0 folds content lines at 75 octets (RFC 2426 section 3).</summary>
    private const int MaxLineOctets = 75;

    public ContactService(AppDbContext dbContext, IAuthService authService, IAuditService auditService,
        ICustomerService customerService, ISupplierService supplierService,
        IReportService reportService, IMessagingService messagingService)
    {
        _dbContext = dbContext;
        _authService = authService;
        _auditService = auditService;
        _customerService = customerService;
        _supplierService = supplierService;
        _reportService = reportService;
        _messagingService = messagingService;
    }

    // ------------------------------------------------------------------ address book

    public async Task<List<ContactInfo>> GetContactsAsync()
    {
        LastValidationError = null;
        Warnings.Clear();

        var customers = await _dbContext.Customers.AsNoTracking().ToListAsync();
        var suppliers = await _dbContext.Suppliers.AsNoTracking().ToListAsync();

        // Outstanding comes from ACC-05 / RPT-09 themselves (unpaid non-estimate invoices,
        // unpaid purchase bills) so this screen can never disagree with Receivables/Payables.
        var receivables = await _reportService.GetReceivablesAsync();
        var payables = await _reportService.GetPayablesAsync();
        var owedByCustomer = SumById(receivables
            .Where(r => r.CustomerId > 0)
            .Select(r => (r.CustomerId, r.OutstandingAmount)));
        var owedToSupplier = SumById(payables
            .Where(p => p.Id > 0)
            .Select(p => (p.Id, p.Amount)));

        // Document history: slim projections grouped in memory (same style as the report
        // service) - cheap for a shop-sized book and no reliance on GroupBy translation.
        var invoiceDocs = await _dbContext.Invoices.AsNoTracking()
            .Where(i => i.CustomerId != null && !i.IsEstimate && i.Status != "Cancelled")
            .Select(i => new { i.CustomerId, i.InvoiceDate, i.Status })
            .ToListAsync();

        var purchaseDocs = await _dbContext.Purchases.AsNoTracking()
            .Where(p => p.SupplierId != null && p.Status != "Cancelled")
            .Select(p => new { p.SupplierId, p.PurchaseDate, p.Status })
            .ToListAsync();

        var invoiceStats = GroupDocs(invoiceDocs.Select(i => (i.CustomerId!.Value, i.InvoiceDate, i.Status)));
        var purchaseStats = GroupDocs(purchaseDocs.Select(p => (p.SupplierId!.Value, p.PurchaseDate, p.Status)));

        var countryPrefix = DigitsOnly((await _messagingService.GetSettingsAsync()).DefaultWhatsAppCountryCode);

        var rows = new List<ContactInfo>();

        foreach (var c in customers)
        {
            var stats = StatsFor(invoiceStats, c.Id);
            rows.Add(new ContactInfo
            {
                PartyType = ContactParties.Customer,
                PartyId = c.Id,
                Name = c.Name ?? string.Empty,
                Phone = TrimToNull(c.Mobile),
                Email = TrimToNull(c.Email),
                GSTIN = TrimToNull(c.GSTIN),
                State = TrimToNull(c.State),
                Address = TrimToNull(c.Address),
                Group = TrimToNull(c.CustomerGroup),
                CreatedAt = c.CreatedAt,
                // The Customer master carries no IsActive column; the screen shows "n/a"
                // instead of inventing a flag.
                IsActive = true,
                HasActiveFlag = false,
                CreditLimit = c.CreditLimit,
                CreditDays = c.CreditDays,
                Outstanding = owedByCustomer.TryGetValue(c.Id, out var receivable) ? receivable : 0m,
                DocumentCount = stats.count,
                PendingDocuments = stats.pending,
                LastDocumentDate = stats.last,
                DocumentKind = "invoice",
                WhatsAppNumber = BuildWhatsAppNumber(c.Mobile, countryPrefix),
            });
        }

        foreach (var sup in suppliers)
        {
            var stats = StatsFor(purchaseStats, sup.Id);
            rows.Add(new ContactInfo
            {
                PartyType = ContactParties.Supplier,
                PartyId = sup.Id,
                Name = sup.Name ?? string.Empty,
                Phone = TrimToNull(sup.Mobile),
                Email = TrimToNull(sup.Email),
                GSTIN = TrimToNull(sup.GSTIN),
                State = null,     // Supplier master has no State column
                Address = TrimToNull(sup.Address),
                Group = null,     // Supplier master has no group/class column
                CreatedAt = sup.CreatedAt,
                IsActive = sup.IsActive,
                HasActiveFlag = true,
                OpeningBalance = sup.OpeningBalance,
                Outstanding = owedToSupplier.TryGetValue(sup.Id, out var payable) ? payable : 0m,
                DocumentCount = stats.count,
                PendingDocuments = stats.pending,
                LastDocumentDate = stats.last,
                DocumentKind = "purchase bill",
                WhatsAppNumber = BuildWhatsAppNumber(sup.Mobile, countryPrefix),
            });
        }

        ApplyDataQualityFlags(rows);

        return rows
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.PartyType, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Fills MissingPhone / MissingEmail / DuplicatePhone plus the plain-language
    /// issue list. Duplicate numbers match on dialled digits, so 098765, 98765 and +91 98765
    /// collide - and a number used by both a customer and a supplier is flagged as probably
    /// the same person entered twice.</summary>
    private void ApplyDataQualityFlags(List<ContactInfo> rows)
    {
        var duplicates = new Dictionary<ContactInfo, (int Others, bool AcrossParties)>();

        var keyed = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Phone))
            .Select(r => (Row: r, Key: DiallingKey(r.Phone!)))
            .Where(x => x.Key.Length >= 7)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in keyed.Where(g => g.Count() > 1))
        {
            var across = group.Select(x => x.Row.PartyType).Distinct().Count() > 1;
            foreach (var item in group)
                duplicates[item.Row] = (group.Count() - 1, across);
        }

        foreach (var row in rows)
        {
            row.MissingPhone = string.IsNullOrWhiteSpace(row.Phone);
            row.MissingEmail = string.IsNullOrWhiteSpace(row.Email);

            if (row.MissingPhone) row.Issues.Add("No mobile - WhatsApp/SMS cannot reach them");
            if (row.MissingEmail) row.Issues.Add("No email");

            if (duplicates.TryGetValue(row, out var hit))
            {
                row.DuplicatePhone = true;
                row.PhoneSharedWith = hit.Others;
                row.SamePersonAcrossParties = hit.AcrossParties;
                row.Issues.Add(hit.AcrossParties
                    ? $"Mobile also used by {hit.Others} contact(s) of the other party type - probably the same person twice"
                    : $"Mobile shared with {hit.Others} other contact(s)");
            }

            if (!row.MissingPhone && !IsPlausiblePhone(row.Phone!))
                row.Issues.Add("Mobile does not look like a number");
            if (!row.MissingEmail && !IsPlausibleEmail(row.Email!))
                row.Issues.Add("Email does not look valid");
            if (!string.IsNullOrWhiteSpace(row.GSTIN) && !TaxCalculationEngine.IsValidGSTIN(row.GSTIN))
                row.Issues.Add("GSTIN fails the GST check used at billing");
            if (row.HasActiveFlag && !row.IsActive)
                row.Issues.Add("Marked inactive on the supplier master");
        }

        var flagged = rows.Count(r => r.HasIssues);
        if (flagged > 0)
            Warnings.Add($"{flagged} contact(s) have a data-quality problem - tick 'Data issues only' to see just those.");
    }

    // ---------------------------------------------------------------------- vCard

    public string BuildVCard(ContactInfo contact)
    {
        if (contact == null) return string.Empty;

        var sb = new StringBuilder();
        sb.Append("BEGIN:VCARD\r\n");
        sb.Append("VERSION:3.0\r\n");
        AppendValue(sb, "UID", $"retailbilling-{contact.Key}");
        AppendStructured(sb, "N", SplitName(contact.Name));
        AppendValue(sb, "FN", contact.Name);
        AppendValue(sb, "ORG", OrgFor(contact));
        if (!string.IsNullOrWhiteSpace(contact.Phone)) AppendValue(sb, "TEL;TYPE=CELL,VOICE", contact.Phone);
        if (!string.IsNullOrWhiteSpace(contact.Email)) AppendValue(sb, "EMAIL;TYPE=INTERNET", contact.Email);
        if (!string.IsNullOrWhiteSpace(contact.Address) || !string.IsNullOrWhiteSpace(contact.State))
            AppendStructured(sb, "ADR;TYPE=HOME",
                new[]
                {
                    string.Empty, string.Empty, contact.Address ?? string.Empty,
                    string.Empty, contact.State ?? string.Empty, string.Empty, string.Empty,
                });
        if (!string.IsNullOrWhiteSpace(contact.GSTIN)) AppendValue(sb, "X-GSTIN", contact.GSTIN);
        AppendCategoryList(sb, "CATEGORIES", CategoriesFor(contact));
        AppendValue(sb, "NOTE", NoteFor(contact));
        AppendValue(sb, "X-PARTY-TYPE", contact.PartyType);
        sb.Append("REV:")
            .Append(contact.CreatedAt.ToUniversalTime().ToString("yyyyMMddTHHmmss'Z'", CultureInfo.InvariantCulture))
            .Append("\r\n");
        sb.Append("END:VCARD\r\n");
        return sb.ToString();
    }

    public async Task<string> ExportVCardAsync(ContactInfo contact)
    {
        LastValidationError = null;
        if (contact == null)
        {
            LastValidationError = "Pick a contact row first.";
            return string.Empty;
        }

        var path = DesktopPath($"vCard_{SafeName(contact.Name)}_{contact.PartyType}_{DateTime.Now:yyyyMMdd-HHmmss}.vcf");
        await WriteVcfAsync(path, new[] { contact });
        await AuditExportAsync(1, path);
        return path;
    }

    public async Task<string> ExportVCardsAsync(IEnumerable<ContactInfo> contacts)
    {
        LastValidationError = null;
        var list = (contacts ?? Enumerable.Empty<ContactInfo>()).ToList();
        if (list.Count == 0)
        {
            LastValidationError = "Tick at least one contact row, or use 'Export all as vCard'.";
            return string.Empty;
        }

        var path = DesktopPath($"AddressBook_{list.Count}_{DateTime.Now:yyyyMMdd-HHmmss}.vcf");
        await WriteVcfAsync(path, list);
        await AuditExportAsync(list.Count, path);
        return path;
    }

    public async Task<string> SaveVCardsAsync(IEnumerable<ContactInfo> contacts, string filePath)
    {
        LastValidationError = null;
        var list = (contacts ?? Enumerable.Empty<ContactInfo>()).ToList();
        if (list.Count == 0)
        {
            LastValidationError = "Nothing to save - the current filter has no contacts.";
            return string.Empty;
        }
        if (string.IsNullOrWhiteSpace(filePath))
        {
            LastValidationError = "No destination file was chosen.";
            return string.Empty;
        }

        var path = filePath.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase) ? filePath : filePath + ".vcf";
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
        {
            LastValidationError = $"Folder not found: {directory}";
            return string.Empty;
        }

        await WriteVcfAsync(path, list);
        await AuditExportAsync(list.Count, path);
        return path;
    }

    /// <summary>Cards concatenated (a .vcf may hold many), written as UTF-8 with a BOM - the
    /// house convention for Desktop exports so Windows editors keep the ₹ and the names.</summary>
    private async Task WriteVcfAsync(string path, IEnumerable<ContactInfo> contacts)
    {
        var sb = new StringBuilder();
        foreach (var contact in contacts)
            sb.Append(BuildVCard(contact));

        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true));
        LastValidationError = null;
    }

    private static void AppendValue(StringBuilder sb, string key, string value) =>
        sb.Append(Fold($"{key}:{EscapeText(value)}")).Append("\r\n");

    private static void AppendStructured(StringBuilder sb, string key, IEnumerable<string> components) =>
        sb.Append(Fold($"{key}:{string.Join(";", components.Select(EscapeText))}")).Append("\r\n");

    /// <summary>CATEGORIES is a comma-separated list of TEXT values, so each category is
    /// escaped on its own and only the separators stay literal commas.</summary>
    private static void AppendCategoryList(StringBuilder sb, string key, IEnumerable<string> categories) =>
        sb.Append(Fold($"{key}:{string.Join(",", categories.Select(EscapeText))}")).Append("\r\n");

    /// <summary>vCard TEXT escaping: backslash first, then comma, semicolon, colon and
    /// newlines (a newline becomes the two-character sequence "\n").</summary>
    private static string EscapeText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("\\", "\\\\")
            .Replace(",", "\\,")
            .Replace(";", "\\;")
            .Replace(":", "\\:")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n")
            .Replace("\r", "\\n");
    }

    /// <summary>Folds a logical line into physical lines of at most 75 octets, continuations
    /// starting with a space. Never splits a surrogate pair and never ends a line on a lone
    /// backslash (that would orphan its escape).</summary>
    private static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= MaxLineOctets) return line;

        var result = new StringBuilder();
        var current = new StringBuilder();
        var currentOctets = 0;
        var budget = MaxLineOctets;

        var elements = StringInfo.GetTextElementEnumerator(line);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var octets = Encoding.UTF8.GetByteCount(element);

            if (currentOctets + octets > budget && current.Length > 0)
            {
                var carry = string.Empty;
                if (current[current.Length - 1] == '\\')
                {
                    current.Length--;
                    currentOctets--;
                    carry = "\\";
                }

                result.Append(current).Append("\r\n ");
                budget = MaxLineOctets - 1; // the folding space already costs one octet
                current = new StringBuilder(carry);
                currentOctets = carry.Length;
            }

            current.Append(element);
            currentOctets += octets;
        }

        result.Append(current);
        return result.ToString();
    }

    /// <summary>"Ravi Kumar" -> family "Kumar", given "Ravi"; single-token names land in
    /// given so phones still show the name under N and sort it correctly.</summary>
    private static string[] SplitName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? new[] { parts[^1], string.Join(' ', parts[..^1]), string.Empty, string.Empty, string.Empty }
            : new[] { string.Empty, trimmed, string.Empty, string.Empty, string.Empty };
    }

    /// <summary>ORG carries the party relationship (the task's "ORG/NOTE with party type"):
    /// the shop is the organisation this contact sits in, grouped by party type.</summary>
    private static string OrgFor(ContactInfo contact) => string.IsNullOrWhiteSpace(contact.Group)
        ? $"Retail Billing - {contact.PartyType}"
        : $"Retail Billing - {contact.PartyType} ({contact.Group})";

    /// <summary>Party type first, then the customer group when there is one; the caller joins
    /// them with literal commas (CATEGORIES is a comma-separated TEXT list).</summary>
    private static List<string> CategoriesFor(ContactInfo contact)
    {
        var categories = new List<string> { contact.PartyType };
        if (!string.IsNullOrWhiteSpace(contact.Group))
            categories.Add(contact.Group);
        return categories;
    }

    private static string NoteFor(ContactInfo contact)
    {
        var lines = new List<string>
        {
            $"Party type: {contact.PartyType}",
            contact.ActiveLabel == "n/a" ? null : $"Status: {contact.ActiveLabel}",
            $"Business terms: {contact.TermsLabel}",
            $"{(contact.PartyType == ContactParties.Supplier ? "Payable by us" : "Receivable from them")}: {contact.OutstandingLabel}",
            $"{(contact.PartyType == ContactParties.Supplier ? "Purchase bills" : "Invoices")}: {contact.DocumentsLabel}, {contact.PendingDocuments} unpaid, {contact.LastDocumentLabel}",
            $"State: {contact.State ?? "-"}",
            $"Added in Retail Billing: {contact.CreatedAt.ToLocalTime():dd-MMM-yyyy}",
            contact.HasIssues ? $"Data issues: {contact.IssueText}" : null,
            "Exported from the Retail Billing address book - edit this party in the app, not on the phone.",
        };
        return string.Join("\n", lines.Where(l => !string.IsNullOrEmpty(l)));
    }

    private async Task AuditExportAsync(int count, string path)
    {
        var user = _authService.CurrentUser?.Username ?? "system";
        await _auditService.RecordAsync("Export", "Contact", 0,
            $"vCard export by {user}: {count} contact(s) to {System.IO.Path.GetFileName(path)}", null, path);
    }

    // ------------------------------------------------------------------ CSV template

    public Task<string> ExportTemplateCsvAsync()
    {
        LastValidationError = null;
        var sb = new StringBuilder();
        sb.AppendLine("Party,Name,GSTIN,Phone,Email,Address,State,CustomerGroup,CreditLimit,CreditDays,OpeningBalance,IsActive");
        sb.AppendLine("Customer,Ravi Kumar,29ABCDE1234F1Z5,9876543210,ravi@example.com,\"12 MG Road, Bangalore\",Karnataka,Wholesale,5000.00,30,,");
        sb.AppendLine("Customer,Priya Sharma,,9812345678,priya@example.com,,Delhi,Retail,10000.00,15,,");
        sb.AppendLine("Supplier,Global Traders,27PQRSTV4567H1Z2,9800011122,accounts@globaltraders.example,Mumbai,,0,0,2500.00,Yes");
        sb.AppendLine("# Party defaults to Customer when blank; Supplier rows ignore State / CustomerGroup / CreditLimit / CreditDays,");
        sb.AppendLine("# Customer rows ignore OpeningBalance / IsActive. Blank GSTIN is fine (B2C). Rows duplicating an existing Phone or");
        sb.AppendLine("# GSTIN are skipped, never merged. Preview the file on screen before Commit.");

        var path = DesktopPath("ImportTemplate_Contacts.csv");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        return Task.FromResult(path);
    }

    // --------------------------------------------------------------------- CSV import

    public async Task<ContactImportResult> PreviewCsvAsync(string filePath)
    {
        LastValidationError = null;
        Warnings.Clear();

        var rows = ParseCsv(filePath, out var headerError);
        if (headerError != null) return ErrorResult(headerError);

        var existing = await LoadExistingKeysAsync();
        return BuildPreview(rows, existing);
    }

    public async Task<ContactImportResult> CommitCsvAsync(string filePath)
    {
        LastValidationError = null;
        Warnings.Clear();

        var rows = ParseCsv(filePath, out var headerError);
        if (headerError != null) return ErrorResult(headerError);

        var existing = await LoadExistingKeysAsync();
        var result = BuildPreview(rows, existing);
        result.SampleRows = null; // preview-only (STK-12 convention)
        if (result.Imported == 0)
            return result;

        // Materialise first: the loop below flips Skip on refused rows, and a deferred
        // Where() would be re-evaluated while it is being enumerated.
        var toImport = rows.Where(r => !r.Skip).ToList();

        await using var tx = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            foreach (var row in toImport)
            {
                // Created through the party services on purpose: their GSTIN/credit rules,
                // CreatedAt stamping and their own audit rows still apply.
                var ok = row.Party == ContactParties.Supplier
                    ? await _supplierService.AddSupplierAsync(BuildSupplier(row))
                    : await _customerService.AddCustomerAsync(BuildCustomer(row));

                if (ok)
                {
                    result.Created.Add($"{row.Party} - {row.Name.Trim()}");
                    continue;
                }

                // Both services swallow their exception and return false; rather than a
                // half-imported book, the whole batch rolls back.
                result.Errors.Add($"Row {row.RowNumber}: the party service refused '{row.Name.Trim()}'.");
            }

            if (result.Created.Count == 0)
            {
                await tx.RollbackAsync();
                LastValidationError = "Nothing could be created - the import was rolled back in full.";
                result.Imported = 0;
                result.Skipped = rows.Count;
                return result;
            }

            await _auditService.RecordAsync("Import", "Contact", 0,
                $"CSV contact import by {_authService.CurrentUser?.Username ?? "system"}: " +
                $"{result.Created.Count} created, {rows.Count - result.Created.Count} skipped " +
                $"({System.IO.Path.GetFileName(filePath)})", null, result.Created);

            await tx.CommitAsync();

            result.Imported = result.Created.Count;
            result.Skipped = rows.Count - result.Created.Count;
            return result;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            LastValidationError = $"Import rolled back: {ex.Message}";
            return ErrorResult($"Import rolled back: {ex.Message}");
        }
    }

    private static Customer BuildCustomer(ParsedRow row) => new()
    {
        Name = row.Name.Trim(),
        Mobile = NullIfBlank(row.Phone),
        Email = NullIfBlank(row.Email),
        Address = NullIfBlank(row.Address),
        GSTIN = NullIfBlank(row.GSTIN),
        State = NullIfBlank(row.State),
        CustomerGroup = NullIfBlank(row.Group),
        CreditLimit = row.CreditLimit ?? 0m,
        CreditDays = row.CreditDays ?? 30,
        CreatedAt = DateTime.UtcNow,
    };

    private static Supplier BuildSupplier(ParsedRow row) => new()
    {
        Name = row.Name.Trim(),
        Mobile = NullIfBlank(row.Phone),
        Email = NullIfBlank(row.Email),
        Address = NullIfBlank(row.Address),
        GSTIN = NullIfBlank(row.GSTIN),
        OpeningBalance = row.OpeningBalance ?? 0m,
        IsActive = row.IsActive ?? true,
        CreatedAt = DateTime.UtcNow,
    };

    /// <summary>One CSV data row with its parsed fields and its skip decision.</summary>
    private sealed class ParsedRow
    {
        public int RowNumber { get; init; }
        public string Party { get; set; } = ContactParties.Customer;
        public string Name { get; set; } = string.Empty;
        public string GSTIN { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public decimal? CreditLimit { get; set; }
        public int? CreditDays { get; set; }
        public decimal? OpeningBalance { get; set; }
        public bool? IsActive { get; set; }

        /// <summary>Validation reason, or null while the row is structurally valid.</summary>
        public string? Error { get; set; }

        /// <summary>Lenient shape notes - the row still imports, but the screen reports them.</summary>
        public List<string> Notes { get; } = new();

        public bool Skip { get; set; }
    }

    /// <summary>Parsing in the STK-12 style: quote-aware cells, flexible headers, per-row
    /// validation, no database access. Comment lines starting with # are ignored.</summary>
    private static List<ParsedRow> ParseCsv(string filePath, out string? headerError)
    {
        headerError = null;
        var rows = new List<ParsedRow>();

        if (!File.Exists(filePath))
        {
            headerError = $"File not found: {filePath}";
            return rows;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(filePath);
        }
        catch (Exception ex)
        {
            headerError = $"Could not read the file: {ex.Message}";
            return rows;
        }

        if (lines.Length == 0)
        {
            headerError = "CSV file is empty.";
            return rows;
        }

        Dictionary<string, int>? map = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;

            var cells = ParseCsvLine(line).Select(c => c.Trim()).ToList();

            if (map == null)
            {
                var normalized = cells.Select(NormalizeHeader).ToList();
                map = MapHeader(normalized,
                    ("party", new[] { "party", "partytype", "type", "customertype", "suppliertype", "kind" }),
                    ("name", new[] { "name", "contactname", "partyname", "customername", "suppliername", "firm", "company" }),
                    ("gstin", new[] { "gstin", "gstnumber", "gstinnumber", "gstno" }),
                    ("phone", new[] { "phone", "phonenumber", "mobile", "mobileno", "mobilenumber", "contactnumber" }),
                    ("email", new[] { "email", "emailaddress", "mail" }),
                    ("address", new[] { "address", "street" }),
                    ("state", new[] { "state", "region" }),
                    ("group", new[] { "customergroup", "group", "class", "customerclass" }),
                    ("creditlimit", new[] { "creditlimit", "creditamount", "limit" }),
                    ("creditdays", new[] { "creditdays", "creditperiod" }),
                    ("openingbalance", new[] { "openingbalance", "openingbal" }),
                    ("isactive", new[] { "isactive", "active", "status" }));

                if (map.ContainsKey("name")) continue; // header row consumed
                map = null;                            // not the header - keep scanning
                continue;
            }

            rows.Add(BuildRow(i + 1, map, cells));
        }

        if (map == null)
            headerError = "Header row not found - expected at least a Name column (plus Party, GSTIN, Phone, " +
                          "Email, Address, State, CustomerGroup, CreditLimit, CreditDays, OpeningBalance, IsActive).";
        if (rows.Count == 0 && headerError == null)
            headerError = "No data rows under the header.";
        return rows;
    }

    private static ParsedRow BuildRow(int rowNumber, Dictionary<string, int> map, List<string> cells)
    {
        var errors = new List<string>();
        var row = new ParsedRow
        {
            RowNumber = rowNumber,
            Name = GetField(map, "name", cells),
            GSTIN = GetField(map, "gstin", cells),
            Phone = GetField(map, "phone", cells),
            Email = GetField(map, "email", cells),
            Address = GetField(map, "address", cells),
            State = GetField(map, "state", cells),
            Group = GetField(map, "group", cells),
        };

        var partyRaw = GetField(map, "party", cells);
        if (string.IsNullOrWhiteSpace(partyRaw))
        {
            row.Party = ContactParties.Customer; // documented default
            row.Notes.Add("Party column blank - imported as Customer");
        }
        else
        {
            row.Party = ContactParties.Normalize(partyRaw) ?? string.Empty;
            if (row.Party.Length == 0)
                errors.Add($"Party '{partyRaw}' is neither Customer nor Supplier.");
        }

        if (string.IsNullOrWhiteSpace(row.Name))
            errors.Insert(0, "Name is required.");

        // GSTIN validated by the same engine the invoice screen uses.
        if (row.GSTIN.Length > 0)
        {
            if (!TaxCalculationEngine.IsValidGSTIN(row.GSTIN))
                errors.Add($"GSTIN '{row.GSTIN}' is not a valid 15-character GSTIN.");
            row.GSTIN = row.GSTIN.ToUpperInvariant();
        }

        // Phone / email are checked leniently: an odd shape is a warning, not a rejection.
        if (row.Phone.Length > 0 && !IsPlausiblePhone(row.Phone))
            row.Notes.Add($"Phone '{row.Phone}' does not look like a number");
        if (row.Email.Length > 0 && !IsPlausibleEmail(row.Email))
            row.Notes.Add($"Email '{row.Email}' does not look valid");

        row.CreditLimit = GetDecimal(map, "creditlimit", cells, "CreditLimit", errors);
        row.CreditDays = GetWhole(map, "creditdays", cells, "CreditDays", errors);
        row.OpeningBalance = GetDecimal(map, "openingbalance", cells, "OpeningBalance", errors);
        row.IsActive = GetBool(map, "isactive", cells, row.Notes);

        // Length ceilings copied from the model annotations.
        CheckLength(errors, row.Party, "Name", row.Name, NameMax);
        CheckLength(errors, row.Party, "Phone", row.Phone, PhoneMax);
        CheckLength(errors, row.Party, "Email", row.Email, EmailMax);
        CheckLength(errors, row.Party, "Address", row.Address, AddressMax);
        CheckLength(errors, row.Party, "GSTIN", row.GSTIN, row.Party == ContactParties.Supplier ? 20 : 15);

        if (row.Party == ContactParties.Supplier)
        {
            if (row.State.Length > 0) row.Notes.Add("Suppliers have no State column - ignored");
            if (row.Group.Length > 0) row.Notes.Add("Suppliers have no CustomerGroup column - ignored");
            if (row.CreditLimit != null) row.Notes.Add("Suppliers have no CreditLimit column - ignored");
            if (row.CreditDays != null) row.Notes.Add("Suppliers have no CreditDays column - ignored");
        }
        else if (row.Party == ContactParties.Customer)
        {
            if (row.OpeningBalance != null) row.Notes.Add("Customers have no OpeningBalance column - ignored");
            if (row.IsActive == false) row.Notes.Add("Customers have no IsActive column - the flag is ignored");
        }

        row.Error = errors.Count > 0 ? string.Join("; ", errors) : null;
        return row;
    }

    /// <summary>Preview + dedupe outcome shared by preview and commit. Dedupe keys are the
    /// dialling-normalised phone and the upper-cased GSTIN, checked against the database and
    /// inside the file; nothing is merged, the later row is skipped with a reason.</summary>
    private ContactImportResult BuildPreview(List<ParsedRow> rows, ExistingKeys existing)
    {
        var result = new ContactImportResult { SampleRows = new List<ContactImportRow>() };
        var seenPhones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenGstins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var error = row.Error;
            var phoneKey = DiallingKey(row.Phone);
            var gstinKey = row.GSTIN.Trim();

            if (error == null)
            {
                if (phoneKey.Length >= 7)
                {
                    if (existing.Phones.TryGetValue(phoneKey, out var phoneHolder))
                        error = $"Phone already belongs to {phoneHolder} - skipped, nothing was merged.";
                    else if (!seenPhones.Add(phoneKey))
                        error = "Phone is duplicated inside this file - only the first row is imported.";
                }
                else if (gstinKey.Length == 0)
                {
                    row.Notes.Add("No usable phone or GSTIN, so this row cannot be deduped");
                }

                if (error == null && gstinKey.Length > 0)
                {
                    if (existing.Gstins.TryGetValue(gstinKey, out var gstinHolder))
                        error = $"GSTIN already belongs to {gstinHolder} - skipped, nothing was merged.";
                    else if (!seenGstins.Add(gstinKey))
                        error = "GSTIN is duplicated inside this file - only the first row is imported.";
                }
            }

            if (error != null)
            {
                row.Skip = true;
                result.Skipped++;
                result.Errors.Add($"Row {row.RowNumber}: {error}");
            }
            else
            {
                result.Imported++;
            }

            foreach (var note in row.Notes)
                Warnings.Add($"Row {row.RowNumber}: {note}");

            result.SampleRows.Add(new ContactImportRow
            {
                Row = row.RowNumber,
                Party = row.Party.Length == 0 ? "(invalid)" : row.Party,
                Name = row.Name,
                Phone = row.Phone,
                Email = row.Email,
                GSTIN = row.GSTIN,
                Status = error == null ? "OK" : "Skipped",
                Error = error ?? string.Join("; ", row.Notes),
                Details = BuildDetails(row),
            });
        }

        if (rows.Count == 0)
            result.Errors.Add("The file has a header but no data rows.");
        return result;
    }

    private static string BuildDetails(ParsedRow row)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.Address)) parts.Add($"Address={row.Address}");
        if (!string.IsNullOrWhiteSpace(row.State)) parts.Add($"State={row.State}");
        if (!string.IsNullOrWhiteSpace(row.Group)) parts.Add($"Group={row.Group}");
        if (row.CreditLimit != null) parts.Add($"CreditLimit={row.CreditLimit.Value.ToString("0.00", CultureInfo.InvariantCulture)}");
        if (row.CreditDays != null) parts.Add($"CreditDays={row.CreditDays.Value}");
        if (row.OpeningBalance != null) parts.Add($"OpeningBalance={row.OpeningBalance.Value.ToString("0.00", CultureInfo.InvariantCulture)}");
        if (row.IsActive != null) parts.Add($"IsActive={row.IsActive.Value}");
        return string.Join(", ", parts);
    }

    private sealed class ExistingKeys
    {
        public Dictionary<string, string> Phones { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Gstins { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Existing phone / GSTIN owners across BOTH masters, so a CSV cannot add a
    /// number that is already a supplier when the row says Customer (the common double entry).</summary>
    private async Task<ExistingKeys> LoadExistingKeysAsync()
    {
        var keys = new ExistingKeys();

        var customers = await _dbContext.Customers.AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.Mobile, c.GSTIN }).ToListAsync();
        foreach (var c in customers)
        {
            var label = $"Customer {c.Name} (#{c.Id})";
            var phone = DiallingKey(c.Mobile);
            if (phone.Length >= 7) keys.Phones.TryAdd(phone, label);
            var gstin = (c.GSTIN ?? string.Empty).Trim().ToUpperInvariant();
            if (gstin.Length > 0) keys.Gstins.TryAdd(gstin, label);
        }

        var suppliers = await _dbContext.Suppliers.AsNoTracking()
            .Select(s => new { s.Id, s.Name, s.Mobile, s.GSTIN }).ToListAsync();
        foreach (var s in suppliers)
        {
            var label = $"Supplier {s.Name} (#{s.Id})";
            var phone = DiallingKey(s.Mobile);
            if (phone.Length >= 7) keys.Phones.TryAdd(phone, label);
            var gstin = (s.GSTIN ?? string.Empty).Trim().ToUpperInvariant();
            if (gstin.Length > 0) keys.Gstins.TryAdd(gstin, label);
        }

        return keys;
    }

    // ------------------------------------------------------------------ shared helpers

    private static Dictionary<int, (int count, int pending, DateTime? last)> GroupDocs(
        IEnumerable<(int PartyId, DateTime Date, string Status)> docs) =>
        docs.GroupBy(d => d.PartyId).ToDictionary(
            g => g.Key,
            g => (g.Count(), g.Count(d => d.Status != "Paid"), (DateTime?)g.Max(d => d.Date)));

    private static (int count, int pending, DateTime? last) StatsFor(
        Dictionary<int, (int count, int pending, DateTime? last)> stats, int partyId) =>
        stats.TryGetValue(partyId, out var value) ? value : NoDocs;

    /// <summary>A party with no documents yet; the explicit DateTime? keeps the tuple literal typed.</summary>
    private static readonly (int count, int pending, DateTime? last) NoDocs =
        (0, 0, default(DateTime?));

    private static Dictionary<int, decimal> SumById(IEnumerable<(int Id, decimal Amount)> pairs) =>
        pairs.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

    /// <summary>Quote-aware CSV split (same parser shape as STK-12): commas inside quoted
    /// fields, doubled quotes as a literal quote.</summary>
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }

    private static string NormalizeHeader(string header) => header.Trim()
        .Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty)
        .ToLowerInvariant();

    private static Dictionary<string, int> MapHeader(List<string> normalized, params (string Field, string[] Aliases)[] columns)
    {
        var map = new Dictionary<string, int>();
        foreach (var (field, aliases) in columns)
        {
            var index = normalized.Select((value, i) => (value, i))
                .Where(x => aliases.Contains(x.value))
                .Select(x => (int?)x.i)
                .FirstOrDefault();
            if (index != null) map[field] = index.Value;
        }
        return map;
    }

    private static string GetField(Dictionary<string, int> map, string field, List<string> cells) =>
        map.TryGetValue(field, out var index) && index < cells.Count ? cells[index].Trim() : string.Empty;

    private static decimal? GetDecimal(Dictionary<string, int> map, string field, List<string> cells,
        string label, List<string> errors)
    {
        var text = GetField(map, field, cells);
        if (text.Length == 0) return null;
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            errors.Add($"{label} '{text}' is not a valid number.");
            return null;
        }
        if (value < 0m)
        {
            errors.Add($"{label} cannot be negative.");
            return null;
        }
        return value;
    }

    private static int? GetWhole(Dictionary<string, int> map, string field, List<string> cells,
        string label, List<string> errors)
    {
        var text = GetField(map, field, cells);
        if (text.Length == 0) return null;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            errors.Add($"{label} '{text}' is not a whole number.");
            return null;
        }
        if (value < 0)
        {
            errors.Add($"{label} cannot be negative.");
            return null;
        }
        return value;
    }

    private static bool? GetBool(Dictionary<string, int> map, string field, List<string> cells, List<string> notes)
    {
        var text = GetField(map, field, cells);
        if (text.Length == 0) return null;
        switch (text.ToLowerInvariant())
        {
            case "true": case "1": case "yes": case "y": case "active": return true;
            case "false": case "0": case "no": case "n": case "inactive": return false;
            default:
                notes.Add($"IsActive '{text}' is not understood - defaulted to active");
                return null;
        }
    }

    private static void CheckLength(List<string> errors, string party, string label, string value, int max)
    {
        if (value.Length > max)
            errors.Add($"{label} is {value.Length} characters; the {(string.IsNullOrEmpty(party) ? "party" : party)} master allows {max}.");
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string DigitsOnly(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : new string(value.Where(char.IsDigit).ToArray());

    /// <summary>Digits only, with an Indian mobile compared on its last 10 so a 0-lead or
    /// +91 form of the same number collides; shorter/odd inputs keep their digit string.</summary>
    private static string DiallingKey(string? phone)
    {
        var digits = DigitsOnly(phone);
        // 09876543210 / +91 9876543210 both reduce to the 10-digit national number.
        if (digits.Length is >= 11 and <= 13) return digits[^10..];
        return digits;
    }

    /// <summary>Messaging Center rule (INT-04): a number longer than 10 digits already carries
    /// the country code, so a 10-digit Indian mobile gets the configured prefix.</summary>
    private static string BuildWhatsAppNumber(string? phone, string countryPrefix)
    {
        var digits = DigitsOnly(phone);
        if (digits.Length == 0) return string.Empty;
        if (digits.Length == 11 && digits[0] == '0') digits = digits[1..];
        return digits.Length > 10 ? digits
            : string.IsNullOrEmpty(countryPrefix) ? digits : countryPrefix + digits;
    }

    private static bool IsPlausiblePhone(string phone)
    {
        var digits = DiallingKey(phone);
        if (digits.Length is < 8 or > 15) return false;
        return phone.All(c => char.IsDigit(c) || c is ' ' or '-' or '+' or '(' or ')' or '.' or '/');
    }

    private static bool IsPlausibleEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 && at < email.Length - 1 && !email.Contains(' ') && email.IndexOf('.', at) > at;
    }

    private static ContactImportResult ErrorResult(string message) => new()
    {
        SampleRows = null,
        Skipped = 1,
        Errors = { message },
    };

    private static string SafeName(string? name)
    {
        var safe = string.Concat((name ?? string.Empty).Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        if (string.IsNullOrWhiteSpace(safe)) safe = $"Contact-{DateTime.Now:yyyyMMddHHmmss}";
        return safe.Length > 40 ? safe[..40] : safe;
    }

    private static string DesktopPath(string fileName) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
}
