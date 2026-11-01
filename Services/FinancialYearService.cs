using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public class FinancialYearService : IFinancialYearService
{
    private readonly AppDbContext _dbContext;
    private readonly INumberSeriesService _numberSeriesService;
    private readonly IJournalService _journalService;

    public FinancialYearService(AppDbContext dbContext, INumberSeriesService numberSeriesService,
        IJournalService journalService)
    {
        _dbContext = dbContext;
        _numberSeriesService = numberSeriesService;
        _journalService = journalService;
    }

    public Task<List<FinancialYear>> GetAllFinancialYearsAsync() =>
        _dbContext.FinancialYears.OrderBy(f => f.StartDate).ToListAsync();

    public Task<FinancialYear?> GetFinancialYearByIdAsync(int id) =>
        _dbContext.FinancialYears.FirstOrDefaultAsync(f => f.Id == id);

    public Task<FinancialYear?> GetCurrentFinancialYearAsync() =>
        _dbContext.FinancialYears.FirstOrDefaultAsync(f => f.IsActive);

    public async Task<bool> AddFinancialYearAsync(FinancialYear fy)
    {
        try
        {
            // If this is the active year, deactivate others
            if (fy.IsActive)
            {
                var activeYears = await _dbContext.FinancialYears
                    .Where(f => f.IsActive)
                    .ToListAsync();
                foreach (var ay in activeYears)
                {
                    ay.IsActive = false;
                    _dbContext.FinancialYears.Update(ay);
                }
            }

            _dbContext.FinancialYears.Add(fy);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateFinancialYearAsync(FinancialYear fy)
    {
        try
        {
            _dbContext.FinancialYears.Update(fy);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteFinancialYearAsync(int id)
    {
        try
        {
            var fy = await _dbContext.FinancialYears.FindAsync(id);
            if (fy == null) return false;
            if (fy.IsActive) return false; // Cannot delete active year
            _dbContext.FinancialYears.Remove(fy);
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> SetActiveYearAsync(int id)
    {
        try
        {
            var all = await _dbContext.FinancialYears.ToListAsync();
            foreach (var fy in all)
            {
                fy.IsActive = (fy.Id == id);
                _dbContext.FinancialYears.Update(fy);
            }
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> LockYearAsync(int id, bool lockStatus)
    {
        try
        {
            var fy = await _dbContext.FinancialYears.FindAsync(id);
            if (fy == null) return false;
            fy.IsLocked = lockStatus;
            await _dbContext.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> IsPeriodLockedAsync(DateTime date)
    {
        var fy = await _dbContext.FinancialYears
            .FirstOrDefaultAsync(f => f.StartDate <= date && f.EndDate >= date);
        return fy != null && fy.IsLocked;
    }

    /// <summary>
    /// ACC-07: close out the current year — create the next FY (starts the day
    /// after the current one ends), make it the active year, and roll every
    /// repeating number series into it so numbering continues gap-free.
    /// </summary>
    public async Task<(bool Success, string? Error, string? Message)> CarryForwardAsync(int currentYearId)
    {
        var current = await _dbContext.FinancialYears.FirstOrDefaultAsync(f => f.Id == currentYearId);
        if (current == null) return (false, "Financial year not found.", null);

        var nextStart = current.EndDate.Date.AddDays(1);
        var nextEnd = nextStart.AddYears(1).AddDays(-1);

        var nextName = $"{nextStart.Year}-{(nextStart.Year + 1) % 100:D2}";

        // One FY per start date
        var exists = await _dbContext.FinancialYears
            .AnyAsync(f => f.StartDate == nextStart || f.Name == nextName);
        if (exists)
        {
            var existing = await _dbContext.FinancialYears
                .FirstAsync(f => f.Name == nextName);
            // Just activate and roll numbering
            await SetActiveYearAsync(existing.Id);
            var rolledAgain = await _numberSeriesService.RollOverFinancialYearAsync(existing.Name);
            return (true, null,
                $"FY {existing.Name} already existed - reactivated it and rolled {rolledAgain} number series forward.");
        }

        var next = new FinancialYear
        {
            Name = nextName,
            StartDate = nextStart,
            EndDate = nextEnd,
            IsActive = true,
            IsLocked = false
        };

        var all = await _dbContext.FinancialYears.ToListAsync();
        foreach (var fy in all)
        {
            fy.IsActive = false;
            _dbContext.FinancialYears.Update(fy);
        }
        _dbContext.FinancialYears.Add(next);
        await _dbContext.SaveChangesAsync();

        var rolled = await _numberSeriesService.RollOverFinancialYearAsync(nextName);

        // ACC-07: seed the new year's opening balances from the old year's closing balances.
        // The opening balance stored on each Account row is the app's single opening-balance
        // model (OpeningBalanceService writes there too), so carry-forward advances it in place.
        var (carried, carryError) = await CarryOpeningBalancesForwardAsync(current);

        // ACC-07: close the old year's P&L revenue into the capital account.
        var pnlNote = await CloseRevenueIntoCapitalAsync(current);

        var message =
            $"Created FY {nextName} ({nextStart:dd-MMM-yyyy} to {nextEnd:dd-MMM-yyyy}), made it active, and rolled {rolled} number series into it. " +
            (carryError ?? $"Carried {carried} account balance(s) forward as opening balances of {nextName}. ") +
            pnlNote;

        return (true, null, message.Trim());
    }

    /// <summary>
    /// ACC-07: for every account, advance the stored opening balance to the old FY's closing
    /// balance: current opening + net movement (sum of Debit − Credit over JournalLines whose
    /// entry date falls inside the old FY window). Date comparisons are on EntryDate.Date.
    /// Never throws — a failure is reported in the error slot so the carry-forward itself still succeeds.
    /// </summary>
    private async Task<(int Carried, string? Error)> CarryOpeningBalancesForwardAsync(FinancialYear oldFy)
    {
        try
        {
            var endExclusive = oldFy.EndDate.Date.AddDays(1);
            var movements = await _dbContext.JournalEntries.AsNoTracking()
                .Where(je => je.EntryDate >= oldFy.StartDate.Date && je.EntryDate < endExclusive)
                .SelectMany(je => je.Lines)
                .GroupBy(l => l.AccountId)
                .Select(g => new { AccountId = g.Key, Net = g.Sum(l => l.Debit) - g.Sum(l => l.Credit) })
                .ToListAsync();

            var netByAccount = movements.ToDictionary(m => m.AccountId, m => m.Net);
            var accounts = await _dbContext.Accounts.ToListAsync();

            var carried = 0;
            foreach (var account in accounts)
            {
                if (!netByAccount.TryGetValue(account.Id, out var net) || net == 0m)
                    continue;
                account.OpeningBalance = account.OpeningBalance + net;
                carried++;
            }

            await _dbContext.SaveChangesAsync();
            return (carried, null);
        }
        catch (Exception ex)
        {
            return (0, $"Opening-balance carry-forward skipped: {ex.Message} ");
        }
    }

    /// <summary>
    /// ACC-07: post a PeriodClose entry dated the old FY's end date that closes the year's
    /// revenue balance (credit base: sum of Credits − Debits over "Sales" accounts) into the
    /// "Capital Account". PostJournalEntryAsync refuses postings into a locked FY — that refusal
    /// is reported in the result message, never as a carry-forward failure.
    /// </summary>
    private async Task<string> CloseRevenueIntoCapitalAsync(FinancialYear oldFy)
    {
        try
        {
            var endExclusive = oldFy.EndDate.Date.AddDays(1);
            var revenueBalance = await _dbContext.JournalLines.AsNoTracking()
                .Where(l => l.JournalEntry!.EntryDate >= oldFy.StartDate.Date
                            && l.JournalEntry.EntryDate < endExclusive
                            && ((l.Account != null && l.Account.Name == "Sales")
                                || (l.Account!.Group != null && l.Account.Group.Name == "Sales")))
                .SumAsync(l => (decimal?)(l.Credit - l.Debit));

            var revenue = Math.Round(revenueBalance ?? 0m, 2);
            if (revenue == 0m)
                return "No revenue balance to close in the old year.";

            var sales = await _dbContext.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.IsActive
                    && (a.Name == "Sales" || (a.Group != null && a.Group.Name == "Sales")));
            if (sales == null)
                return "P&L close skipped: no 'Sales' account was found in the chart of accounts.";

            // Equity accounts resolve to the capital account regardless of the exact system name.
            var capital = await _dbContext.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Name == "Capital Account" || a.AccountType == "Equity");
            if (capital == null)
                return "P&L close skipped: no capital (Equity) account was found in the chart of accounts.";

            var entry = new JournalEntry
            {
                EntryDate = oldFy.EndDate.Date,
                ReferenceType = "PeriodClose",
                ReferenceId = oldFy.Id.ToString(),
                Narration = $"P&L close: FY {oldFy.Name} revenue carried to {capital.Name}",
                Lines = revenue > 0m
                    ? new List<JournalLine>
                    {
                        new() { AccountId = sales.Id, Debit = revenue, Narration = "Revenue closed to capital" },
                        new() { AccountId = capital.Id, Credit = revenue, Narration = $"FY {oldFy.Name} revenue close" },
                    }
                    : new List<JournalLine>
                    {
                        new() { AccountId = capital.Id, Debit = -revenue, Narration = $"FY {oldFy.Name} revenue close (net loss)" },
                        new() { AccountId = sales.Id, Credit = -revenue, Narration = "Negative revenue closed to capital" },
                    },
            };

            var posted = await _journalService.PostJournalEntryAsync(entry);
            if (!posted)
                return $"P&L close skipped: {_journalService.LastValidationError ?? "the journal entry was refused."}";

            return $"Closed ₹{revenue:N2} of FY {oldFy.Name} revenue into {capital.Name} (posting dated {oldFy.EndDate:dd-MMM-yyyy}).";
        }
        catch (Exception ex)
        {
            return $"P&L close skipped: {ex.Message}";
        }
    }

    public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
}
