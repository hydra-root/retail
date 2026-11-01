using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RetailBillingApp.Services;

/// <summary>
/// ACC-06: bank reconciliation implementation. Statement CSVs are imported with
/// flexible headers, then auto-matched against Cash/Bank journal lines.
/// </summary>
public class BankReconciliationService : IBankReconciliationService
{
    private readonly AppDbContext _dbContext;

    public BankReconciliationService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<(int Imported, List<string> Warnings)> ImportCsvAsync(string filePath)
    {
        var warnings = new List<string>();
        var lines = File.ReadAllLines(filePath, Encoding.UTF8);
        if (lines.Length < 2)
            throw new InvalidOperationException("The CSV has no data rows.");

        var header = SplitCsvLine(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
        int dateCol; int descCol; int refCol; int amountCol; int debitCol; int creditCol;

        dateCol = FindColumn(header, "date", "value date", "txn date", "transaction date", "posting date");
        descCol = FindColumn(header, "description", "narration", "details", "particulars", "remarks");
        refCol = FindColumn(header, "ref", "reference", "ref no", "reference no", "cheque", "chq", "cheque no");
        amountCol = FindColumn(header, "amount", "value");
        debitCol = FindColumn(header, "withdrawal", "debit", "dr", "paid out", "money out");
        creditCol = FindColumn(header, "deposit", "credit", "cr", "paid in", "money in");

        if (dateCol < 0)
            throw new InvalidOperationException("Could not find a Date column (tried: Date / Value Date / Txn Date).");
        if (amountCol < 0 && debitCol < 0 && creditCol < 0)
            throw new InvalidOperationException("Could not find an Amount column (tried: Amount, or Withdrawal + Deposit).");

        var imported = new List<BankStatementLine>();

        for (int i = 1; i < lines.Length; i++)
        {
            var raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var cells = SplitCsvLine(raw);
            var valueDate = ParseDate(cells.Count > dateCol ? cells[dateCol] : "", out var dateOk);
            if (!dateOk)
            {
                warnings.Add($"Row {i + 1}: unrecognised date '{(cells.Count > dateCol ? cells[dateCol] : "")}' - skipped.");
                continue;
            }

            decimal amount = 0m;
            if (amountCol >= 0)
            {
                amount = ParseAmount(cells.Count > amountCol ? cells[amountCol] : "");
            }
            else
            {
                var debit = debitCol >= 0 ? ParseAmount(cells.Count > debitCol ? cells[debitCol] : "") : 0m;
                var credit = creditCol >= 0 ? ParseAmount(cells.Count > creditCol ? cells[creditCol] : "") : 0m;
                if (debit == 0m && credit == 0m) continue; // blank money columns
                // Debit (withdrawal) is money out → negative; credit (deposit) is money in
                amount = credit - debit;
            }

            imported.Add(new BankStatementLine
            {
                ValueDate = valueDate,
                Description = descCol >= 0 && descCol < cells.Count ? Truncate(cells[descCol], 500) : string.Empty,
                ReferenceNo = refCol >= 0 && refCol < cells.Count ? Truncate(cells[refCol], 100) : null,
                Amount = amount
            });
        }

        if (imported.Count == 0)
            throw new InvalidOperationException("No valid rows found in the CSV.");

        _dbContext.BankStatementLines.AddRange(imported);
        await _dbContext.SaveChangesAsync();
        return (imported.Count, warnings);
    }

    public async Task<List<BankStatementLine>> GetLinesAsync(string statusFilter)
    {
        var query = _dbContext.BankStatementLines.AsNoTracking().AsQueryable();
        if (string.Equals(statusFilter, "Unmatched", StringComparison.OrdinalIgnoreCase))
            query = query.Where(l => l.Status == "Unmatched");
        else if (string.Equals(statusFilter, "Reconciled", StringComparison.OrdinalIgnoreCase))
            query = query.Where(l => l.Status == "Reconciled");

        return await query.OrderByDescending(l => l.ValueDate).ThenByDescending(l => l.Id).ToListAsync();
    }

    public async Task<int> AutoMatchAsync()
    {
        var unmatched = await _dbContext.BankStatementLines
            .Where(l => l.Status == "Unmatched")
            .ToListAsync();
        if (unmatched.Count == 0) return 0;

        // Journal lines on cash/bank accounts. Journal debit = money in, credit = money out.
        var candidates = await _dbContext.JournalLines
            .Include(l => l.Account)
            .Include(l => l.JournalEntry)
            .Where(l => l.Account!.Name.ToLower().Contains("cash")
                     || l.Account!.Name.ToLower().Contains("bank"))
            .Select(l => new
            {
                LineId = l.Id,
                EntryId = l.JournalEntryId,
                EntryDate = l.JournalEntry!.EntryDate,
                Narration = l.JournalEntry!.Narration,
                ReferenceType = l.JournalEntry!.ReferenceType,
                ReferenceId = l.JournalEntry!.ReferenceId,
                AccountName = l.Account!.Name,
                Net = l.Debit - l.Credit
            })
            .ToListAsync();

        if (candidates.Count == 0) return 0;

        // Journal lines already pointed at by earlier matches must not be reused
        var usedLineIds = new HashSet<int>();
        var matchedRefs = await _dbContext.BankStatementLines
            .Where(l => l.Status == "Reconciled" && l.MatchedRef != null)
            .Select(l => l.MatchedRef!)
            .ToListAsync();
        foreach (var m in Regex.Matches(string.Join(" ", matchedRefs), @"line (\d+)")
                     .Cast<Match>())
        {
            if (int.TryParse(m.Groups[1].Value, out var id)) usedLineIds.Add(id);
        }

        int matchedCount = 0;
        foreach (var st in unmatched)
        {
            var best = candidates
                .Where(c => !usedLineIds.Contains(c.LineId)
                            && Math.Abs(c.Net - st.Amount) <= 0.01m
                            && Math.Abs((c.EntryDate.Date - st.ValueDate.Date).TotalDays) <= 5)
                .OrderBy(c => Math.Abs((c.EntryDate.Date - st.ValueDate.Date).TotalDays))
                .FirstOrDefault();

            if (best == null) continue;

            var label = string.IsNullOrWhiteSpace(best.Narration)
                ? $"{best.AccountName} a/c ({best.ReferenceType ?? "Journal"} {best.ReferenceId ?? best.EntryId.ToString()})"
                : Truncate(best.Narration, 80);

            st.MatchedType = "Auto";
            st.MatchedRef = $"JE {best.EntryId} line {best.LineId}: {label}";
            st.Status = "Reconciled";
            usedLineIds.Add(best.LineId);
            matchedCount++;
        }

        if (matchedCount > 0) await _dbContext.SaveChangesAsync();
        return matchedCount;
    }

    public async Task<bool> MarkReconciledAsync(int lineId)
    {
        var line = await _dbContext.BankStatementLines.FirstOrDefaultAsync(l => l.Id == lineId);
        if (line == null) return false;

        line.Status = "Reconciled";
        line.MatchedType = "Manual";
        if (string.IsNullOrWhiteSpace(line.MatchedRef))
            line.MatchedRef = "Manually reconciled (no journal link)";
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ClearMatchAsync(int lineId)
    {
        var line = await _dbContext.BankStatementLines.FirstOrDefaultAsync(l => l.Id == lineId);
        if (line == null) return false;

        line.Status = "Unmatched";
        line.MatchedType = "Unmatched";
        line.MatchedRef = null;
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<BankReconciliationSummary> GetSummaryAsync()
    {
        var lines = await _dbContext.BankStatementLines.AsNoTracking().ToListAsync();
        var summary = new BankReconciliationSummary
        {
            TotalLines = lines.Count,
            ReconciledLines = lines.Count(l => l.Status == "Reconciled"),
            TotalStatementAmount = lines.Sum(l => l.Amount),
            MatchedAmount = lines.Where(l => l.Status == "Reconciled").Sum(l => l.Amount)
        };
        return summary;
    }

    // --- CSV helpers ---------------------------------------------------

    /// <summary>Splits one CSV line honoring quoted fields with embedded commas.</summary>
    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString().Trim());
        return result;
    }

    private static int FindColumn(List<string> header, params string[] names)
    {
        foreach (var name in names)
        {
            var idx = header.FindIndex(h => h == name);
            if (idx >= 0) return idx;
        }
        // partial match fallback (e.g. "reference_number")
        foreach (var name in names)
        {
            var idx = header.FindIndex(h => h.Replace("_", " ").Replace("-", " ").Contains(name));
            if (idx >= 0) return idx;
        }
        return -1;
    }

    private static readonly string[] DateFormats =
    [
        "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "yyyy/MM/dd", "dd-MM-yyyy",
        "dd-MMM-yyyy", "d-MMM-yyyy", "dd MMM yyyy", "MM/dd/yyyy"
    ];

    private static DateTime ParseDate(string text, out bool ok)
    {
        text = text.Trim().Trim('"');
        foreach (var format in DateFormats)
        {
            if (DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                ok = true;
                return parsed;
            }
        }
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var generic))
        {
            ok = true;
            return generic;
        }
        ok = false;
        return DateTime.MinValue;
    }

    private static decimal ParseAmount(string text)
    {
        text = text.Trim().Trim('"').Replace(",", "").Replace("₹", "").Replace("Rs", "")
                   .Replace("$", "").Trim();
        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            // accounting style negatives: (1,234.56)
            var inner = text[1..^1];
            return decimal.TryParse(inner, NumberStyles.Any, CultureInfo.InvariantCulture, out var neg)
                ? -neg : 0m;
        }
        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            return value;
        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var localized))
            return localized;
        return 0m;
    }

    private static string Truncate(string text, int max) =>
        string.IsNullOrWhiteSpace(text) ? string.Empty
            : text.Length <= max ? text : text[..max];
}
