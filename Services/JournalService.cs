using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class JournalService : IJournalService
{
    private readonly AppDbContext _dbContext;

    public JournalService(AppDbContext dbContext) => _dbContext = dbContext;

    public string? LastValidationError { get; private set; }

    public Task<List<JournalEntry>> GetAllJournalEntriesAsync() =>
        _dbContext.JournalEntries
            .Include(j => j.Lines)
            .ThenInclude(l => l.Account)
            .OrderByDescending(j => j.EntryDate)
            .ToListAsync();

    public Task<JournalEntry?> GetJournalEntryByIdAsync(int id) =>
        _dbContext.JournalEntries
            .Include(j => j.Lines)
            .ThenInclude(l => l.Account)
            .FirstOrDefaultAsync(j => j.Id == id);

    public bool IsBalanced(JournalEntry entry)
    {
        var totalDebit = entry.Lines.Sum(l => l.Debit);
        var totalCredit = entry.Lines.Sum(l => l.Credit);
        return totalDebit == totalCredit;
    }

    /// <summary>
    /// ACC-03: full manual-entry validation. Every line must carry exactly one
    /// of debit/credit, reference an existing account, and the entry must have
    /// at least two lines and balance to a tolerance of 0.01.
    /// </summary>
    public async Task<(bool Valid, string? Error)> ValidateEntryAsync(JournalEntry entry)
    {
        if (entry.Lines == null || entry.Lines.Count < 2)
            return (false, "A journal entry needs at least two lines (double-entry).");

        var accountIds = (await _dbContext.Accounts.Select(a => a.Id).ToListAsync()).ToHashSet();

        for (int i = 0; i < entry.Lines.Count; i++)
        {
            var line = entry.Lines[i];
            if (line.Debit > 0m && line.Credit > 0m)
                return (false, $"Line {i + 1}: a line cannot carry both a debit and a credit.");
            if (line.Debit == 0m && line.Credit == 0m)
                return (false, $"Line {i + 1}: enter an amount (debit or credit).");
            if (!accountIds.Contains(line.AccountId))
                return (false, $"Line {i + 1}: select a valid account.");
            if (line.Debit < 0m || line.Credit < 0m)
                return (false, $"Line {i + 1}: amounts cannot be negative.");
        }

        var totalDebit = entry.Lines.Sum(l => l.Debit);
        var totalCredit = entry.Lines.Sum(l => l.Credit);
        if (System.Math.Abs(totalDebit - totalCredit) > 0.01m)
            return (false, $"Journal is not balanced: debits {totalDebit:N2} vs credits {totalCredit:N2}.");

        return (true, null);
    }

    public async Task<bool> PostJournalEntryAsync(JournalEntry entry)
    {
        var (valid, error) = await ValidateEntryAsync(entry);
        if (!valid)
        {
            LastValidationError = error;
            return false;
        }

        // ACC-07: block postings into locked financial periods
        if (entry.EntryDate == default)
        {
            entry.EntryDate = System.DateTime.UtcNow;
        }
        var lockedFy = await _dbContext.FinancialYears.AsNoTracking()
            .FirstOrDefaultAsync(f => f.IsLocked
                && f.StartDate <= entry.EntryDate.Date && f.EndDate >= entry.EntryDate.Date);
        if (lockedFy != null)
        {
            LastValidationError = $"Financial period {lockedFy.Name} is locked - posting dated into it is not allowed. Dated {entry.EntryDate:dd-MMM-yyyy}.";
            return false;
        }

        try
        {
            // Manual entries keep the date the user entered (set just above when default)
            if (string.IsNullOrWhiteSpace(entry.ReferenceType))
                entry.ReferenceType = "Manual";

            _dbContext.JournalEntries.Add(entry);
            await _dbContext.SaveChangesAsync();
            LastValidationError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastValidationError = ex.Message;
            return false;
        }
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
}
