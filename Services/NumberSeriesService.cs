using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace RetailBillingApp.Services;

public class NumberSeriesService : INumberSeriesService
{
    private readonly AppDbContext _dbContext;

    public NumberSeriesService(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<List<NumberSeries>> GetAllNumberSeriesAsync() => _dbContext.NumberSeries.ToListAsync();

    public ValueTask<NumberSeries?> GetNumberSeriesByIdAsync(int id) => _dbContext.NumberSeries.FindAsync(id);

    public Task<NumberSeries?> GetNumberSeriesAsync(string docType, string financialYear, int? storeId = null) =>
        _dbContext.NumberSeries
            .FirstOrDefaultAsync(n => n.DocType == docType && n.FinancialYear == financialYear && (n.StoreId == storeId || n.StoreId == null));

    public async Task<string> GetNextNumberAsync(string docType, string financialYear, int? storeId = null)
    {
        var series = await _dbContext.NumberSeries
            .FirstOrDefaultAsync(n => n.DocType == docType && n.FinancialYear == financialYear && (n.StoreId == storeId || n.StoreId == null));

        if (series == null)
        {
            // INV-03: clone the most recent matching series (keeps prefix/suffix
            // customization and continuous numbering across financial years)
            var template = await _dbContext.NumberSeries
                .Where(n => n.DocType == docType && n.IsActive && (n.StoreId == storeId || n.StoreId == null))
                .OrderBy(n => n.StoreId == storeId ? 0 : 1)
                .ThenByDescending(n => n.FinancialYear)
                .FirstOrDefaultAsync();

            if (template != null)
            {
                series = new NumberSeries
                {
                    Name = template.Name,
                    Prefix = template.Prefix,
                    Suffix = template.Suffix,
                    StartNumber = template.StartNumber,
                    CurrentNumber = template.ResetEachYear ? template.StartNumber : template.CurrentNumber,
                    FinancialYear = financialYear,
                    DocType = template.DocType,
                    StoreId = template.StoreId,
                    ResetEachYear = template.ResetEachYear,
                    IsActive = true
                };
                _dbContext.NumberSeries.Add(series);
                await _dbContext.SaveChangesAsync();
            }
            else
            {
                // Auto-create a default series
                series = new NumberSeries
                {
                    Name = docType,
                    Prefix = docType.Substring(0, 3).ToUpper(),
                    Suffix = string.Empty,
                    StartNumber = 1,
                    CurrentNumber = 1,
                    FinancialYear = financialYear,
                    DocType = docType,
                    StoreId = storeId,
                    ResetEachYear = true,
                    IsActive = true
                };
                _dbContext.NumberSeries.Add(series);
                await _dbContext.SaveChangesAsync();
            }
        }

        var nextNumber = $"{series.Prefix}{series.CurrentNumber:D4}{series.Suffix}";
        series.CurrentNumber++;
        await _dbContext.SaveChangesAsync();
        return nextNumber;
    }

    public async Task<bool> AddNumberSeriesAsync(NumberSeries series)
    {
        try
        {
            _dbContext.NumberSeries.Add(series);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateNumberSeriesAsync(NumberSeries series)
    {
        try
        {
            _dbContext.NumberSeries.Update(series);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteNumberSeriesAsync(int id)
    {
        try
        {
            var series = await _dbContext.NumberSeries.FindAsync(id);
            if (series == null) return false;
            _dbContext.NumberSeries.Remove(series);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();

    /// <summary>
    /// INV-03: bring the numbering forward into a new financial year — every
    /// active ResetEachYear series of a different FY is cloned into the new FY,
    /// restarting at its StartNumber. Existing series in the FY are untouched.
    /// </summary>
    public async Task<int> RollOverFinancialYearAsync(string newFinancialYear)
    {
        if (string.IsNullOrWhiteSpace(newFinancialYear))
            throw new ArgumentException("Financial year is required.", nameof(newFinancialYear));

        var sources = await _dbContext.NumberSeries
            .Where(n => n.IsActive && n.ResetEachYear && n.FinancialYear != newFinancialYear)
            .ToListAsync();

        int created = 0;
        var seen = new HashSet<(string DocType, int? StoreId)>();
        foreach (var src in sources)
        {
            if (!seen.Add((src.DocType, src.StoreId))) continue;

            var exists = await _dbContext.NumberSeries.AnyAsync(n =>
                n.DocType == src.DocType && n.FinancialYear == newFinancialYear && n.StoreId == src.StoreId);
            if (exists) continue;

            _dbContext.NumberSeries.Add(new NumberSeries
            {
                Name = src.Name,
                Prefix = src.Prefix,
                Suffix = src.Suffix,
                StartNumber = src.StartNumber,
                CurrentNumber = src.StartNumber,
                FinancialYear = newFinancialYear,
                DocType = src.DocType,
                StoreId = src.StoreId,
                ResetEachYear = src.ResetEachYear,
                IsActive = true
            });
            created++;
        }

        await _dbContext.SaveChangesAsync();
        return created;
    }

    /// <summary>
    /// INV-03: gap-free audit. For every series, the documents issued within the
    /// series' FY window matching its prefix/suffix are compared against the
    /// expected sequence; missing serials, duplicate serials and empty series
    /// are reported.
    /// </summary>
    public async Task<List<NumberGapRow>> GetGapReportAsync()
    {
        var seriesList = await _dbContext.NumberSeries.AsNoTracking().ToListAsync();

        // Issued numbers grouped by document type
        var invoiceDocs = await _dbContext.Invoices
            .Select(i => new { Number = i.InvoiceNumber, Date = i.InvoiceDate, i.IsEstimate })
            .ToListAsync();
        var purchaseDocs = await _dbContext.Purchases
            .Select(p => new { Number = p.PurchaseNumber, Date = p.PurchaseDate })
            .ToListAsync();
        var creditNoteDocs = await _dbContext.CreditNotes
            .Select(c => new { Number = c.CreditNoteNumber, Date = c.CreditNoteDate })
            .ToListAsync();

        var docsByType = new Dictionary<string, List<(string Number, DateTime Date)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in invoiceDocs)
        {
            var key = i.IsEstimate ? "Estimate" : "Invoice";
            if (!docsByType.TryGetValue(key, out var list)) docsByType[key] = list = new();
            list.Add((i.Number, i.Date));
        }
        docsByType["Purchase"] = purchaseDocs.Select(p => (p.Number, p.Date)).ToList();
        docsByType["CreditNote"] = creditNoteDocs.Select(c => (c.Number, c.Date)).ToList();

        var report = new List<NumberGapRow>();
        foreach (var series in seriesList)
        {
            if (!TryGetFinancialYearWindow(series.FinancialYear, out var windowStart, out var windowEnd))
            {
                // unparseable FY - audit whole history
                windowStart = DateTime.MinValue;
                windowEnd = DateTime.MaxValue;
            }

            docsByType.TryGetValue(series.DocType, out var docs);
            docs ??= new List<(string, DateTime)>();

            var serials = new List<int>();
            foreach (var doc in docs)
            {
                if (doc.Date < windowStart || doc.Date > windowEnd) continue;
                if (TryExtractSerial(doc.Number, series.Prefix, series.Suffix, out var serial))
                    serials.Add(serial);
            }

            var row = new NumberGapRow
            {
                DocType = series.DocType,
                SeriesName = series.Name,
                FinancialYear = series.FinancialYear,
                Prefix = series.Prefix,
                Suffix = series.Suffix,
                Issued = serials.Count
            };

            if (serials.Count == 0)
            {
                row.Status = "No documents issued";
                report.Add(row);
                continue;
            }

            var ordered = serials.OrderBy(s => s).ToList();
            row.FirstNumber = ordered.First();
            row.LastNumber = ordered.Last();

            var unique = ordered.Distinct().ToList();
            if (unique.Count != ordered.Count)
            {
                var dupes = ordered.GroupBy(s => s).Where(g => g.Count() > 1)
                    .Select(g => $"{g.Key} ×{g.Count()}");
                row.Duplicates = string.Join(", ", dupes);
            }

            var expected = new HashSet<int>(unique);
            var missing = new List<int>();
            for (var serial = Math.Min(series.StartNumber, row.FirstNumber); serial <= row.LastNumber; serial++)
                if (!expected.Contains(serial)) missing.Add(serial);

            row.MissingCount = missing.Count;
            if (missing.Count > 6)
                row.Missing = string.Join(", ", missing.Take(6)) + $" (+{missing.Count - 6} more)";
            else
                row.Missing = string.Join(", ", missing);

            row.Status = row.MissingCount > 0 || row.Duplicates.Length > 0 ? "Gaps/Duplicates" : "OK";
            report.Add(row);
        }

        return report
            .OrderBy(r => r.DocType)
            .ThenByDescending(r => r.FinancialYear)
            .ToList();
    }

    /// <summary>INV-03: "2026-27" → window 1-Apr-2026 … 31-Mar-2027.</summary>
    private static bool TryGetFinancialYearWindow(string financialYear, out DateTime start, out DateTime end)
    {
        start = end = DateTime.MinValue;
        var match = Regex.Match(financialYear, @"(\d{4})\s*-\s*(\d{2}|\d{4})");
        if (!match.Success) return false;

        if (!int.TryParse(match.Groups[1].Value, out var startYear)) return false;
        var endPart = match.Groups[2].Value;
        var endYear = endPart.Length == 2 ? int.Parse($"{startYear / 100}{endPart}") : int.Parse(endPart);

        start = new DateTime(startYear, 4, 1);
        end = new DateTime(endYear, 3, 31);
        return true;
    }

    /// <summary>INV-03: pull the integer serial out of a formatted document number (prefix + digits + suffix).</summary>
    private static bool TryExtractSerial(string? number, string prefix, string suffix, out int serial)
    {
        serial = 0;
        if (string.IsNullOrEmpty(number)) return false;
        if (!number.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var mid = number.Length > prefix.Length ? number[prefix.Length..] : string.Empty;
        if (!string.IsNullOrEmpty(suffix))
        {
            if (mid.Length <= suffix.Length || !mid.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
            mid = mid[..^suffix.Length];
        }
        return int.TryParse(mid, out serial);
    }
}
