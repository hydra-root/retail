namespace RetailBillingApp.Services;

/// <summary>
/// INT-01: unified address book across the Customer and Supplier masters, vCard export
/// for a phone address book, and header-aware CSV import that creates parties through
/// the existing ICustomerService / ISupplierService.
///
/// There is deliberately no Contact table: the book is derived from Customers + Suppliers
/// on every load, so a phone number, an outstanding or an inactive flag can never disagree
/// with the party master it came from (single source of truth).
/// </summary>
public interface IContactService
{
    /// <summary>Party filter values the screen offers; "All" means no filter.</summary>
    List<string> PartyTypes { get; }

    /// <summary>Human-readable reason for the most recently rejected operation.</summary>
    string? LastValidationError { get; }

    /// <summary>Non-blocking problems from the last load / import (odd phone or email shapes,
    /// columns a party type does not store).</summary>
    List<string> Warnings { get; }

    /// <summary>One row per customer and per supplier, with outstanding, document history
    /// and the data-quality flags the shopkeeper needs.</summary>
    Task<List<ContactInfo>> GetContactsAsync();

    /// <summary>RFC 6350 / 2426 style vCard 3.0 text for one contact (CRLF, folded lines).</summary>
    string BuildVCard(ContactInfo contact);

    /// <summary>One contact as a single-card .vcf on the Desktop; returns the full path.</summary>
    Task<string> ExportVCardAsync(ContactInfo contact);

    /// <summary>The given contacts as one multi-card .vcf on the Desktop; returns the full path.</summary>
    Task<string> ExportVCardsAsync(IEnumerable<ContactInfo> contacts);

    /// <summary>The given contacts written to a user-picked .vcf path; returns that path.</summary>
    Task<string> SaveVCardsAsync(IEnumerable<ContactInfo> contacts, string filePath);

    /// <summary>Write the 12-column import template to the Desktop and return its path.</summary>
    Task<string> ExportTemplateCsvAsync();

    /// <summary>Parse, validate and dedupe a CSV without any database writes.</summary>
    Task<ContactImportResult> PreviewCsvAsync(string filePath);

    /// <summary>Validate then create every clean row through the party services inside one
    /// transaction (rolled back in full if any insert fails).</summary>
    Task<ContactImportResult> CommitCsvAsync(string filePath);
}

/// <summary>INT-01: party keys used by the address book and the CSV Party column.</summary>
public static class ContactParties
{
    public const string All = "All";
    public const string Customer = "Customer";
    public const string Supplier = "Supplier";

    /// <summary>Customer / Supplier (case-insensitive); null when the value is neither.</summary>
    public static string? Normalize(string? party) => party?.Trim().ToLowerInvariant() switch
    {
        "customer" or "client" or "buyer" => Customer,
        "supplier" or "vendor" => Supplier,
        _ => null,
    };
}

/// <summary>
/// INT-01: one address-book row. Plain DTO (no INotifyPropertyChanged) - the contacts
/// screen wraps it for row selection, exactly like PAY-08 wraps ReminderCandidate.
/// </summary>
public class ContactInfo
{
    /// <summary>"Customer" or "Supplier" - which master this row came from.</summary>
    public string PartyType { get; set; } = ContactParties.Customer;

    /// <summary>Id in that master (CustomerId or SupplierId).</summary>
    public int PartyId { get; set; }

    /// <summary>Stable identity across the two tables, e.g. "Customer:12".</summary>
    public string Key => $"{PartyType}:{PartyId}";

    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? GSTIN { get; set; }
    public string? State { get; set; }
    public string? Address { get; set; }

    /// <summary>Customer.CustomerGroup - always null for suppliers (the master has no such column).</summary>
    public string? Group { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Suppliers carry IsActive on the master; customers have no active flag at all.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>False when the underlying master has no IsActive column, so the screen can say so.</summary>
    public bool HasActiveFlag { get; set; }

    /// <summary>Customer-only credit terms (0 / 0 for suppliers).</summary>
    public decimal CreditLimit { get; set; }

    public int CreditDays { get; set; }

    /// <summary>Supplier-only opening balance (0 for customers).</summary>
    public decimal OpeningBalance { get; set; }

    /// <summary>ACC-05 receivable for a customer, RPT-09 payable for a supplier.</summary>
    public decimal Outstanding { get; set; }

    /// <summary>Non-cancelled documents naming this party (invoices or purchase bills).</summary>
    public int DocumentCount { get; set; }

    /// <summary>Of those, the ones still unpaid.</summary>
    public int PendingDocuments { get; set; }

    public DateTime? LastDocumentDate { get; set; }

    /// <summary>"invoice" / "purchase bill" - wording used in the labels.</summary>
    public string DocumentKind { get; set; } = "invoice";

    /// <summary>Mobile dialled for WhatsApp/SMS (country code added for 10-digit Indian numbers).</summary>
    public string WhatsAppNumber { get; set; } = string.Empty;

    // --- data-quality flags (the practical reason the screen exists) ---

    public bool MissingPhone { get; set; }

    public bool MissingEmail { get; set; }

    /// <summary>True when another contact - possibly the other party type - shares this number.</summary>
    public bool DuplicatePhone { get; set; }

    /// <summary>How many *other* contacts use the same normalised number.</summary>
    public int PhoneSharedWith { get; set; }

    /// <summary>True when the same number is a customer in one row and a supplier in another.</summary>
    public bool SamePersonAcrossParties { get; set; }

    /// <summary>Plain-language problems for this row; empty when the row is clean.</summary>
    public List<string> Issues { get; } = new();

    public bool HasIssues => Issues.Count > 0;

    public string IssueText => HasIssues ? string.Join(" · ", Issues) : "No problems";

    public string ActiveLabel => !HasActiveFlag
        ? "n/a"
        : IsActive ? "Active" : "Inactive";

    /// <summary>Money the party owes us (customer) or we owe them (supplier), ₹-formatted.</summary>
    public string OutstandingLabel => Outstanding >= 0m
        ? $"₹{Outstanding:N2}"
        : $"-₹{-Outstanding:N2}";

    public string TermsLabel => PartyType == ContactParties.Supplier
        ? (Math.Abs(OpeningBalance) > 0.01m ? $"Opening balance ₹{OpeningBalance:N2}" : "No opening balance")
        : (Math.Abs(CreditLimit) > 0.01m
            ? $"Credit ₹{CreditLimit:N2} for {CreditDays} days"
            : "Cash sale (no credit limit)");

    public string LastDocumentLabel => LastDocumentDate == null
        ? $"No {DocumentKind} yet"
        : $"Last {DocumentKind} {LastDocumentDate.Value.ToLocalTime():dd-MMM-yyyy}";

    public string DocumentsLabel => $"{DocumentCount} {DocumentKind}{(DocumentCount == 1 ? string.Empty : "s")}";

    /// <summary>Which Messaging Center channels this contact can actually be reached on.</summary>
    public string ChannelText
    {
        get
        {
            var channels = new List<string>();
            if (!string.IsNullOrWhiteSpace(Phone)) channels.Add("WhatsApp/SMS");
            if (!string.IsNullOrWhiteSpace(Email)) channels.Add("Email");
            return channels.Count > 0 ? string.Join(" + ", channels) : "No channel reachable";
        }
    }

    /// <summary>Where the shopkeeper edits this row - the master screen, not this screen.</summary>
    public string EditHint => PartyType == ContactParties.Supplier
        ? "Suppliers screen (this address book is read-only: it is derived from the masters)"
        : "Customers screen (this address book is read-only: it is derived from the masters)";
}

/// <summary>INT-01: outcome of a CSV preview or commit.</summary>
public class ContactImportResult
{
    /// <summary>Rows ready to import (preview) or actually created (commit).</summary>
    public int Imported { get; set; }

    /// <summary>Rows not imported - failed validation, or skipped by dedupe.</summary>
    public int Skipped { get; set; }

    /// <summary>One message per skipped/failed row, prefixed with its CSV row number.</summary>
    public List<string> Errors { get; } = new();

    /// <summary>What a commit created, e.g. "Customer - Ravi Kumar".</summary>
    public List<string> Created { get; } = new();

    /// <summary>Per-row preview result; null after a commit (same convention as STK-12).</summary>
    public List<ContactImportRow>? SampleRows { get; set; }

    public string Summary => $"{Imported} to import, {Skipped} skipped.";
}

/// <summary>INT-01: one CSV row as shown in the import preview grid.</summary>
public class ContactImportRow
{
    /// <summary>CSV line number (1-based, header is line 1).</summary>
    public int Row { get; set; }

    /// <summary>"Customer" / "Supplier" after the Party column was normalised.</summary>
    public string Party { get; set; } = ContactParties.Customer;

    public string Name { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string GSTIN { get; set; } = string.Empty;

    /// <summary>Other fields joined for display (state, group, terms).</summary>
    public string Details { get; set; } = string.Empty;

    /// <summary>"OK" or "Skipped".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Empty for clean rows, otherwise why the row was skipped.</summary>
    public string Error { get; set; } = string.Empty;
}
