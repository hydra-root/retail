using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.Globalization;
using System.IO;
using System.Text;

namespace RetailBillingApp.Services;

/// <summary>
/// STK-12: CSV import/export of Products, Customers and Suppliers. Parsing is quote-aware,
/// headers map flexibly, bad rows are skipped with row-number errors, commits run inside
/// one transaction with dedupe, and exports land on the Desktop.
/// </summary>
public class BulkImportService : IBulkImportService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public BulkImportService(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    public async Task<BulkImportResult> ValidateCsvAsync(string entityType, string filePath)
    {
        var rows = ParseFile(entityType, filePath, out var headerError);
        if (headerError != null) return ErrorResult(headerError);

        // Previews flag existing records too (reads only, no DB writes).
        var existingKeys = await LoadExistingKeysAsync(entityType);
        return BuildPreview(entityType, rows, existingKeys);
    }

    public async Task<BulkImportResult> CommitAsync(string entityType, string filePath)
    {
        if (IsSuppliers(entityType))
            return await CommitSupplierCsvAsync(filePath);

        var rows = ParseFile(entityType, filePath, out var headerError);
        if (headerError != null) return ErrorResult(headerError);

        var existingKeys = await LoadExistingKeysAsync(entityType);
        var result = BuildPreview(entityType, rows, existingKeys);
        result.SampleRows = null; // samples are preview-only
        if (result.Imported == 0)
            return result;

        await using var tx = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            foreach (var row in rows.Where(r => !r.Skip))
            {
                if (IsProducts(entityType))
                {
                    _dbContext.Products.Add(new Product
                    {
                        Name = row.Name.Trim(),
                        SKU = string.IsNullOrWhiteSpace(row.SKU) ? null : row.SKU.Trim(),
                        Barcode = string.IsNullOrWhiteSpace(row.Barcode) ? null : row.Barcode.Trim(),
                        Rate = row.Rate ?? 0m,
                        TaxPercentage = row.Tax ?? 0m,
                        StockQty = row.StockQty ?? 0, // StockQty = OpeningStock
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    _dbContext.Customers.Add(new Customer
                    {
                        Name = row.Name.Trim(),
                        GSTIN = string.IsNullOrWhiteSpace(row.GSTIN) ? null : row.GSTIN.Trim(),
                        Mobile = string.IsNullOrWhiteSpace(row.Phone) ? null : row.Phone.Trim(),
                        Address = string.IsNullOrWhiteSpace(row.Address) ? null : row.Address.Trim(),
                        CreditLimit = row.CreditLimit ?? 0m
                    });
                }
            }

            await _dbContext.SaveChangesAsync();
            await tx.CommitAsync();
            return result;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ---- STK-12 supplier flavor: same preview/commit phases mirrored on Suppliers ----

    /// <summary>STK-12: supplier CSV is valid CSV columns of the Supplier master; read only.</summary>
    public async Task<BulkImportResult> ValidateSupplierCsvAsync(string filePath) =>
        await ValidateCsvAsync(SuppliersEntity, filePath);

    /// <summary>
    /// STK-12: validation then inserts of all valid supplier rows inside one transaction,
    /// deduped on <see cref="BuildSupplierKey"/> (supplier name), each created row audited.
    /// </summary>
    public async Task<BulkImportResult> CommitSupplierCsvAsync(string filePath)
    {
        var rows = ParseFile(SuppliersEntity, filePath, out var headerError);
        if (headerError != null) return ErrorResult(headerError);

        var existingKeys = await LoadExistingKeysAsync(SuppliersEntity);
        var result = BuildPreview(SuppliersEntity, rows, existingKeys);
        result.SampleRows = null; // samples are preview-only
        if (result.Imported == 0)
            return result;

        await using var tx = await _dbContext.Database.BeginTransactionAsync();
        List<Supplier> created;
        try
        {
            created = new List<Supplier>();
            foreach (var row in rows.Where(r => !r.Skip))
            {
                var supplier = new Supplier
                {
                    Name = row.Name.Trim(),
                    Mobile = string.IsNullOrWhiteSpace(row.Phone) ? null : row.Phone.Trim(),
                    Email = string.IsNullOrWhiteSpace(row.Email) ? null : row.Email.Trim(),
                    GSTIN = string.IsNullOrWhiteSpace(row.GSTIN) ? null : row.GSTIN.Trim(),
                    Address = string.IsNullOrWhiteSpace(row.Address) ? null : row.Address.Trim(),
                    OpeningBalance = row.OpeningBalance ?? 0m,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };
                _dbContext.Suppliers.Add(supplier);
                created.Add(supplier);
            }

            await _dbContext.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        // Audit after the commit succeeded, so an audit write can never roll back the import.
        foreach (var supplier in created)
        {
            try
            {
                await _auditService.RecordAsync("Create", "Supplier", supplier.Id,
                    $"Imported supplier {supplier.Name}", null,
                    new { supplier.Name, supplier.Mobile, supplier.Email, supplier.GSTIN, supplier.Address, supplier.OpeningBalance });
            }
            catch
            {
                // Audit must never fail the import itself.
            }
        }

        return result;
    }

    public Task<string> ExportSupplierTemplateCsvAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Mobile,Email,GSTIN,Address,OpeningBalance");
        sb.AppendLine("Sharma Distributors,9876500011,sales@sharmadist.in,29ABCDE1234F1Z5,\"12 MG Road, Bangalore\",15000.00");
        sb.AppendLine("Global Traders,,orders@globaltraders.in,27PQRSTV4567H1Z2,Mumbai,0.00");
        sb.AppendLine("Metro Wholesale,9845012345,,07LMNOPQ6789K1Z3,Delhi,2500.00");

        var path = GetDesktopPath($"ImportTemplate_{SuppliersEntity}.csv");
        File.WriteAllText(path, sb.ToString());
        return Task.FromResult(path);
    }

    public async Task<string> ExportSupplierAllCsvAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Mobile,Email,GSTIN,Address,OpeningBalance");
        var suppliers = await _dbContext.Suppliers.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
        foreach (var s in suppliers)
            sb.AppendLine(string.Join(",", new[]
            {
                EscapeCsvField(s.Name),
                EscapeCsvField(s.Mobile ?? string.Empty),
                EscapeCsvField(s.Email ?? string.Empty),
                EscapeCsvField(s.GSTIN ?? string.Empty),
                EscapeCsvField(s.Address ?? string.Empty),
                s.OpeningBalance.ToString("0.00", CultureInfo.InvariantCulture)
            }));

        var path = GetDesktopPath($"{SuppliersEntity}_Export_{DateTime.Today:yyyyMMdd}.csv");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    public Task<string> ExportTemplateCsvAsync(string entityType)
    {
        var sb = new StringBuilder();
        if (IsProducts(entityType))
        {
            sb.AppendLine("Name,SKU,Barcode,Rate,TaxPercentage,OpeningStock");
            sb.AppendLine("Tea Powder 500g,TEA500,8901000012345,240.00,5,100");
            sb.AppendLine("Milk 1L,MLK1,8901000054321,55.00,0,60");
            sb.AppendLine("Notebook A4,NBA4,,45.50,12,25");
        }
        else if (IsSuppliers(entityType))
        {
            sb.AppendLine("Name,Mobile,Email,GSTIN,Address,OpeningBalance");
            sb.AppendLine("Sharma Distributors,9876500011,sales@sharmadist.in,29ABCDE1234F1Z5,\"12 MG Road, Bangalore\",15000.00");
            sb.AppendLine("Global Traders,,orders@globaltraders.in,27PQRSTV4567H1Z2,Mumbai,0.00");
            sb.AppendLine("Metro Wholesale,9845012345,,07LMNOPQ6789K1Z3,Delhi,2500.00");
        }
        else
        {
            sb.AppendLine("Name,GSTIN,Phone,Address,CreditLimit");
            sb.AppendLine("Ravi Kumar,29ABCDE1234F1Z5,9876543210,\"12 MG Road, Bangalore\",5000.00");
            sb.AppendLine("Priya Sharma,,9812345678,,10000.00");
            sb.AppendLine("Global Traders,27PQRSTV4567H1Z2,,Mumbai,0.00");
        }

        var path = GetDesktopPath($"ImportTemplate_{entityType}.csv");
        File.WriteAllText(path, sb.ToString());
        return Task.FromResult(path);
    }

    public async Task<string> ExportAllCsvAsync(string entityType)
    {
        var sb = new StringBuilder();
        if (IsProducts(entityType))
        {
            sb.AppendLine("Name,SKU,Barcode,Rate,TaxPercentage,OpeningStock");
            var products = await _dbContext.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
            foreach (var p in products)
                sb.AppendLine(string.Join(",", new[]
                {
                    EscapeCsvField(p.Name),
                    EscapeCsvField(p.SKU ?? string.Empty),
                    EscapeCsvField(p.Barcode ?? string.Empty),
                    p.Rate.ToString("0.00", CultureInfo.InvariantCulture),
                    p.TaxPercentage.ToString("0.##", CultureInfo.InvariantCulture),
                    p.StockQty.ToString(CultureInfo.InvariantCulture)
                }));
        }
        else
        {
            sb.AppendLine("Name,GSTIN,Phone,Address,CreditLimit");
            var customers = await _dbContext.Customers.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
            foreach (var c in customers)
                sb.AppendLine(string.Join(",", new[]
                {
                    EscapeCsvField(c.Name),
                    EscapeCsvField(c.GSTIN ?? string.Empty),
                    EscapeCsvField(c.Mobile ?? string.Empty),
                    EscapeCsvField(c.Address ?? string.Empty),
                    c.CreditLimit.ToString("0.00", CultureInfo.InvariantCulture)
                }));
        }
        else if (IsSuppliers(entityType))
        {
            sb.AppendLine("Name,Mobile,Email,GSTIN,Address,OpeningBalance");
            var suppliers = await _dbContext.Suppliers.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
            foreach (var s in suppliers)
                sb.AppendLine(string.Join(",", new[]
                {
                    EscapeCsvField(s.Name),
                    EscapeCsvField(s.Mobile ?? string.Empty),
                    EscapeCsvField(s.Email ?? string.Empty),
                    EscapeCsvField(s.GSTIN ?? string.Empty),
                    EscapeCsvField(s.Address ?? string.Empty),
                    s.OpeningBalance.ToString("0.00", CultureInfo.InvariantCulture)
                }));
        }

        var path = GetDesktopPath($"{entityType}_Export_{DateTime.Today:yyyyMMdd}.csv");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    #region Parsing and validation

    /// <summary>One CSV data row, parsed up-front. Error is null while the row is structurally valid.</summary>
    private sealed class CsvRow
    {
        public int RowNumber { get; init; }
        public string Name { get; init; } = string.Empty;
        public string SKU { get; init; } = string.Empty;
        public string Barcode { get; init; } = string.Empty;
        public string GSTIN { get; init; } = string.Empty;
        public string Phone { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Address { get; init; } = string.Empty;
        public decimal? Rate { get; init; }
        public decimal? Tax { get; init; }
        public int? StockQty { get; init; }
        public decimal? CreditLimit { get; init; }
        public decimal? OpeningBalance { get; init; }

        /// <summary>Validation error(s), or null when the row is structurally valid.</summary>
        public string? Error { get; set; }

        /// <summary>Set during preview when the row fails validation or dedupe; commit skips it.</summary>
        public bool Skip { get; set; }
    }

    /// <summary>
    /// Read the file, map headers flexibly, parse and validate every data row.
    /// Never touches the database. Rows whose Error is set are skipped later.
    /// </summary>
    private static List<CsvRow> ParseFile(string entityType, string filePath, out string? headerError)
    {
        headerError = null;
        var rows = new List<CsvRow>();

        if (!File.Exists(filePath))
        {
            headerError = $"File not found: {filePath}";
            return rows;
        }

        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0)
        {
            headerError = "CSV file is empty.";
            return rows;
        }

        var products = IsProducts(entityType);
        var suppliers = IsSuppliers(entityType);
        Dictionary<string, int>? map = null;

        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cells = ParseCsvLine(lines[i]).Select(c => c.Trim()).ToList();

            if (map == null)
            {
                var normalized = cells.Select(NormalizeHeader).ToList();
                map = products
                    ? MapHeader(normalized,
                        ("name", new[] { "name", "productname", "itemname" }),
                        ("sku", new[] { "sku", "skucode", "itemcode", "code" }),
                        ("barcode", new[] { "barcode", "barcodenumber", "barcodeno" }),
                        ("rate", new[] { "rate", "price", "sellingprice", "saleprice", "mrp" }),
                        ("taxpercentage", new[] { "taxpercentage", "taxpercent", "gstpercentage", "gstpercent", "tax", "gst" }),
                        ("openingstock", new[] { "openingstock", "stockqty", "openstock", "stock", "quantity", "qty" }))
                    : suppliers
                        ? MapHeader(normalized,
                            ("name", new[] { "name", "suppliername", "partyname", "companyname" }),
                            ("phone", new[] { "phone", "phonenumber", "mobile", "mobilenumber", "mobileno", "contact", "contactnumber" }),
                            ("email", new[] { "email", "emailaddress", "mail" }),
                            ("gstin", new[] { "gstin", "gstnumber", "gstinnumber", "gstno" }),
                            ("address", new[] { "address" }),
                            ("openingbalance", new[] { "openingbalance", "balance", "openbalance", "openingamount" }))
                        : MapHeader(normalized,
                        ("name", new[] { "name", "customername", "partyname" }),
                        ("gstin", new[] { "gstin", "gstnumber", "gstinnumber", "gstno" }),
                        ("phone", new[] { "phone", "phonenumber", "mobile", "mobilenumber", "mobileno", "contact" }),
                        ("address", new[] { "address" }),
                        ("creditlimit", new[] { "creditlimit", "creditamount", "limit" }));
                if (map.ContainsKey("name"))
                    continue; // header row consumed; data starts on the next line
                map = null;   // not the header row - keep scanning
                continue;
            }

            rows.Add(products
                ? BuildProductRow(i + 1, map, cells)
                : suppliers
                    ? BuildSupplierRow(i + 1, map, cells)
                    : BuildCustomerRow(i + 1, map, cells));
        }

        if (map == null)
        {
            headerError = products
                ? "Header row not found - expected columns Name (plus SKU, Barcode, Rate, TaxPercentage, OpeningStock)."
                : suppliers
                    ? "Header row not found - expected columns Name (plus Mobile, Email, GSTIN, Address, OpeningBalance)."
                    : "Header row not found - expected columns Name (plus GSTIN, Phone, Address, CreditLimit).";
        }
        return rows;
    }

    private static CsvRow BuildProductRow(int rowNumber, Dictionary<string, int> map, List<string> cells)
    {
        var errors = new List<string>();
        var row = new CsvRow
        {
            RowNumber = rowNumber,
            Name = GetField(map, "name", cells),
            SKU = GetField(map, "sku", cells),
            Barcode = GetField(map, "barcode", cells),
            Rate = GetDecimal(map, "rate", cells, "Rate", errors),
            Tax = GetDecimal(map, "taxpercentage", cells, "TaxPercentage", errors, max: 100m),
            StockQty = GetQuantity(map, "openingstock", cells, "OpeningStock", errors)
        };

        if (string.IsNullOrWhiteSpace(row.Name))
            errors.Insert(0, "Name is required.");

        row.Error = errors.Count > 0 ? string.Join("; ", errors) : null;
        return row;
    }

    private static CsvRow BuildCustomerRow(int rowNumber, Dictionary<string, int> map, List<string> cells)
    {
        var errors = new List<string>();
        var row = new CsvRow
        {
            RowNumber = rowNumber,
            Name = GetField(map, "name", cells),
            GSTIN = GetField(map, "gstin", cells),
            Phone = GetField(map, "phone", cells),
            Address = GetField(map, "address", cells),
            CreditLimit = GetDecimal(map, "creditlimit", cells, "CreditLimit", errors)
        };

        if (string.IsNullOrWhiteSpace(row.Name))
            errors.Insert(0, "Name is required.");

        var gstin = row.GSTIN.Trim();
        if (gstin.Length > 0 && gstin.Length != 15)
            errors.Add($"GSTIN '{gstin}' must be exactly 15 characters.");

        row.Error = errors.Count > 0 ? string.Join("; ", errors) : null;
        return row;
    }

    /// <summary>
    /// STK-12 supplier row: Name plus Mobile / Email / GSTIN / Address / OpeningBalance.
    /// Non-empty GSTIN values are validated with <see cref="TaxCalculationEngine.IsValidGSTIN"/>.
    /// </summary>
    private static CsvRow BuildSupplierRow(int rowNumber, Dictionary<string, int> map, List<string> cells)
    {
        var errors = new List<string>();
        var row = new CsvRow
        {
            RowNumber = rowNumber,
            Name = GetField(map, "name", cells),
            Phone = GetField(map, "phone", cells),
            Email = GetField(map, "email", cells),
            GSTIN = GetField(map, "gstin", cells),
            Address = GetField(map, "address", cells),
            OpeningBalance = GetDecimal(map, "openingbalance", cells, "OpeningBalance", errors)
        };

        if (string.IsNullOrWhiteSpace(row.Name))
            errors.Insert(0, "Name is required.");

        var gstin = row.GSTIN.Trim();
        if (gstin.Length > 0 && !TaxCalculationEngine.IsValidGSTIN(gstin))
            errors.Add($"GSTIN '{gstin}' is not a valid GST number.");

        row.Error = errors.Count > 0 ? string.Join("; ", errors) : null;
        return row;
    }

    /// <summary>Normalize a header cell: trim, strip separators, lowercase.</summary>
    private static string NormalizeHeader(string header) => header.Trim()
        .Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty)
        .ToLowerInvariant();

    /// <summary>Map canonical field names to column indexes (only columns present in the file).</summary>
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

    /// <summary>Value of a mapped column, trimmed; empty when the column is absent.</summary>
    private static string GetField(Dictionary<string, int> map, string field, List<string> cells) =>
        map.TryGetValue(field, out var index) && index < cells.Count ? cells[index].Trim() : string.Empty;

    /// <summary>
    /// Parse an optional decimal column with System.Globalization InvariantCulture.
    /// Blank cells yield null; unreadable or negative values append an error to
    /// `errors` and yield null so the row is still skipped and reported.
    /// </summary>
    private static decimal? GetDecimal(Dictionary<string, int> map, string field, List<string> cells,
        string label, List<string> errors, decimal? max = null)
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
        if (max != null && value > max)
        {
            errors.Add($"{label} '{text}' must be between 0 and {max:0}.");
            return null;
        }
        return value;
    }

    /// <summary>Same contract as GetDecimal, for whole-number (stock) columns.</summary>
    private static int? GetQuantity(Dictionary<string, int> map, string field, List<string> cells,
        string label, List<string> errors)
    {
        var text = GetField(map, field, cells);
        if (text.Length == 0) return null;

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            errors.Add($"{label} '{text}' is not a valid whole number.");
            return null;
        }
        if (value < 0)
        {
            errors.Add($"{label} cannot be negative.");
            return null;
        }
        return value;
    }

    /// <summary>Build the preview-and-commit outcome: statuses, per-row errors and totals.</summary>
    private BulkImportResult BuildPreview(string entityType, List<CsvRow> rows, HashSet<string> existingKeys)
    {
        var result = new BulkImportResult { SampleRows = new List<BulkImportRow>() };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var error = row.Error;

            if (error == null)
            {
                if (IsProducts(entityType))
                {
                    var sku = row.SKU.Trim();
                    if (sku.Length > 0)
                    {
                        if (existingKeys.Contains(sku))
                            error = $"SKU '{sku}' already exists in the database.";
                        else if (!seen.Add(sku))
                            error = $"SKU '{sku}' is duplicated inside this file.";
                    }
                }
                else if (IsSuppliers(entityType))
                {
                    var key = BuildSupplierKey(row.Name);
                    if (existingKeys.Contains(key))
                        error = $"Supplier '{row.Name.Trim()}' already exists in the database.";
                    else if (!seen.Add(key))
                        error = $"Supplier '{row.Name.Trim()}' is duplicated inside this file.";
                }
                else
                {
                    var key = BuildCustomerKey(row.Name, row.Phone);
                    if (existingKeys.Contains(key))
                        error = $"Customer '{row.Name.Trim()}' with phone '{row.Phone.Trim()}' already exists in the database.";
                    else if (!seen.Add(key))
                        error = $"Customer '{row.Name.Trim()}' with the same name and phone is duplicated inside this file.";
                }
            }

            if (error != null) row.Skip = true;

            result.SampleRows.Add(new BulkImportRow
            {
                Row = row.RowNumber,
                Status = error == null ? "OK" : "Error",
                Error = error ?? string.Empty,
                Name = row.Name.Trim(),
                Details = BuildDetails(entityType, row)
            });
            if (error == null) result.Imported++;
            else { result.Failed++; result.Errors.Add($"Row {row.RowNumber}: {error}"); }
        }
        return result;
    }

    /// <summary>Dedupe key for a customer: name + phone, trimmed, case-insensitive.</summary>
    private static string BuildCustomerKey(string name, string? phone) =>
        $"{name.Trim().ToUpperInvariant()}|{phone?.Trim() ?? string.Empty}";

    /// <summary>Dedupe key for a supplier: the supplier name alone, trimmed, case-insensitive.</summary>
    private static string BuildSupplierKey(string name) => name.Trim();

    private static string BuildDetails(string entityType, CsvRow row)
    {
        if (IsProducts(entityType))
            return $"SKU={row.SKU.Trim()}, Rate={row.Rate?.ToString(CultureInfo.InvariantCulture) ?? ""}, Tax={row.Tax?.ToString(CultureInfo.InvariantCulture) ?? ""}, OpeningStock={row.StockQty?.ToString(CultureInfo.InvariantCulture) ?? ""}";
        if (IsSuppliers(entityType))
            return $"Mobile={row.Phone.Trim()}, Email={row.Email.Trim()}, GSTIN={row.GSTIN.Trim()}, OpeningBalance={row.OpeningBalance?.ToString(CultureInfo.InvariantCulture) ?? ""}";
        return $"Phone={row.Phone.Trim()}, GSTIN={row.GSTIN.Trim()}, CreditLimit={row.CreditLimit?.ToString(CultureInfo.InvariantCulture) ?? ""}";
    }

    /// <summary>Existing SKUs / customer keys / supplier names for dedupe, case-insensitive.</summary>
    private async Task<HashSet<string>> LoadExistingKeysAsync(string entityType)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (IsProducts(entityType))
        {
            var skus = await _dbContext.Products.AsNoTracking()
                .Where(p => p.SKU != null && p.SKU != "")
                .Select(p => p.SKU!)
                .ToListAsync();
            foreach (var sku in skus) keys.Add(sku.Trim());
        }
        else if (IsSuppliers(entityType))
        {
            var names = await _dbContext.Suppliers.AsNoTracking()
                .Select(s => s.Name)
                .ToListAsync();
            foreach (var name in names) keys.Add(BuildSupplierKey(name));
        }
        else
        {
            var customers = await _dbContext.Customers.AsNoTracking()
                .Select(c => new { c.Name, c.Mobile })
                .ToListAsync();
            foreach (var c in customers) keys.Add(BuildCustomerKey(c.Name, c.Mobile));
        }
        return keys;
    }

    /// <summary>A file-level failure (missing file / no header row) as a result.</summary>
    private static BulkImportResult ErrorResult(string message) => new()
    {
        SampleRows = null,
        Errors = { message },
        Failed = 1
    };

    #endregion

    #region CSV helpers

    /// <summary>Quote-aware split: handles commas inside quoted fields and doubled quotes.</summary>
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

    /// <summary>Quote a CSV field when it contains a comma, quote or newline.</summary>
    private static string EscapeCsvField(string field) =>
        field.Contains(',') || field.Contains('"') || field.Contains('\n')
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;

    #endregion

    #region Misc

    private static bool IsProducts(string entityType) =>
        string.Equals(entityType, "Products", StringComparison.OrdinalIgnoreCase);

    private static string GetDesktopPath(string fileName) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

    #endregion
}

