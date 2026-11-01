using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.Text.RegularExpressions;

namespace RetailBillingApp.Services;

/// <summary>
/// PAY-10: cash &amp; bank book implementation. Cash/bank accounts are those
/// whose name contains "Cash" or "Bank".
/// </summary>
public class CashBookService : ICashBookService
{
    private readonly AppDbContext _dbContext;

    public CashBookService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<List<Account>> GetCashBankAccountsAsync()
    {
        var all = await _dbContext.Accounts.AsNoTracking()
            .Where(a => a.IsActive)
            .ToListAsync();
        return all.Where(a => Regex.IsMatch(a.Name, @"cash|bank", RegexOptions.IgnoreCase))
            .OrderBy(a => a.Name)
            .ToList();
    }

    /// <summary>Journal lines on cash/bank accounts, with their entries.</summary>
    private async Task<List<(JournalLine Line, string AccountName)>> GetCashBankLinesAsync()
    {
        var lines = await _dbContext.JournalLines
            .Include(l => l.JournalEntry)
            .Include(l => l.Account)
            .Where(l => l.Account!.Name.ToLower().Contains("cash")
                     || l.Account!.Name.ToLower().Contains("bank"))
            .ToListAsync();
        return lines.Select(l => (l, l.Account!.Name)).ToList();
    }

    public async Task<CashBookReport> GetCashBookAsync(int? accountId, DateTime startDate, DateTime endDate)
    {
        var accounts = accountId.HasValue
            ? await _dbContext.Accounts.AsNoTracking()
                .Where(a => a.Id == accountId.Value).ToListAsync()
            : await GetCashBankAccountsAsync();

        var accountIds = accounts.Select(a => a.Id).ToHashSet();
        var accountNameById = accounts.ToDictionary(a => a.Id, a => a.Name);

        var report = new CashBookReport();
        var endExclusive = endDate.Date.AddDays(1);

        // Fetch every journal line touching these accounts (before and in period)
        var allLines = (await GetCashBankLinesAsync())
            .Where(x => accountIds.Contains(x.Line.AccountId))
            .OrderBy(x => x.Line.JournalEntry!.EntryDate)
            .ThenBy(x => x.Line.Id)
            .ToList();

        report.OpeningBalance = accounts.Sum(a => a.OpeningBalance)
            + allLines.Where(x => x.Line.JournalEntry!.EntryDate.Date < startDate.Date)
                .Sum(x => x.Line.Debit - x.Line.Credit);

        var running = report.OpeningBalance;

        // Entries with 2+ cash/bank lines are internal transfers (contra)
        var transferEntryIds = allLines
            .GroupBy(x => x.Line.JournalEntryId)
            .Where(g => g.Count(x => x.Line.Debit > 0 || x.Line.Credit > 0) >= 2
                        && g.Any(x => x.Line.Credit > 0)
                        && g.Any(x => x.Line.Debit > 0))
            .Select(g => g.Key)
            .ToHashSet();

        foreach (var x in allLines.Where(x =>
            x.Line.JournalEntry!.EntryDate.Date >= startDate.Date
            && x.Line.JournalEntry!.EntryDate.Date < endExclusive))
        {
            var line = x.Line;
            var entry = line.JournalEntry!;
            var isTransfer = transferEntryIds.Contains(line.JournalEntryId);
            var reference = $"{entry.ReferenceType}#{entry.ReferenceId}".Replace("#", " ");

            if (isTransfer)
            {
                if (accountId == null)
                {
                    // Combined book: contra transfers cancel - one descriptive row, zero net effect
                    var fromName = allLines
                        .Where(o => o.Line.JournalEntryId == line.JournalEntryId && o.Line.Credit > 0)
                        .Select(o => o.AccountName).FirstOrDefault() ?? "?";
                    var toName = allLines
                        .Where(o => o.Line.JournalEntryId == line.JournalEntryId && o.Line.Debit > 0)
                        .Select(o => o.AccountName).FirstOrDefault() ?? "?";
                    if (line.Credit > 0) continue; // emit once, on the debit (target) line

                    var transferAmount = line.Debit;
                    var contraNarration =
                        $"{transferAmount:N2} moved {fromName} to {toName} (no net effect on combined balance)";
                    if (line.Debit > 0)
                    {
                        report.Rows.Add(new CashBookRow
                        {
                            Date = entry.EntryDate.Date,
                            AccountName = $"{fromName} → {toName}",
                            Type = "Transfer (Contra)",
                            Reference = reference,
                            Narration = contraNarration,
                            In = 0m,
                            Out = 0m,
                            RunningBalance = running
                        });
                    }
                    continue;
                }

                // Per-account book: transfers are real money movements
                var type = line.Debit > 0 ? "Transfer (Contra In)" : "Transfer (Contra Out)";
                var amount = line.Debit > 0 ? line.Debit : line.Credit;
                running += line.Debit - line.Credit;
                report.TotalIn += line.Debit;
                report.TotalOut += line.Credit;
                report.Rows.Add(new CashBookRow
                {
                    Date = entry.EntryDate.Date,
                    AccountName = x.AccountName,
                    Type = type,
                    Reference = reference,
                    Narration = entry.Narration,
                    In = line.Debit,
                    Out = line.Credit,
                    RunningBalance = running
                });
                continue;
            }

            // Ordinary receipt / payment
            running += line.Debit - line.Credit;
            report.TotalIn += line.Debit;
            report.TotalOut += line.Credit;
            report.Rows.Add(new CashBookRow
            {
                Date = entry.EntryDate.Date,
                AccountName = x.AccountName,
                Type = line.Debit > 0 ? "Receipt" : "Payment",
                Reference = reference,
                Narration = entry.Narration,
                In = line.Debit,
                Out = line.Credit,
                RunningBalance = running
            });
        }

        return report;
    }

    public async Task<(bool Success, string? Error)> TransferAsync(int fromAccountId, int toAccountId,
        decimal amount, DateTime date, string? narration)
    {
        if (amount <= 0) return (false, "Transfer amount must be greater than zero.");
        if (fromAccountId == toAccountId) return (false, "Source and destination accounts must be different.");

        var accounts = await _dbContext.Accounts
            .Where(a => a.Id == fromAccountId || a.Id == toAccountId)
            .ToListAsync();
        var from = accounts.FirstOrDefault(a => a.Id == fromAccountId);
        var to = accounts.FirstOrDefault(a => a.Id == toAccountId);
        if (from == null || to == null) return (false, "Both accounts must exist.");

        var entry = new JournalEntry
        {
            EntryDate = date,
            Narration = string.IsNullOrWhiteSpace(narration)
                ? $"Cash transfer {from.Name} to {to.Name}"
                : narration,
            ReferenceType = "Transfer",
            ReferenceId = "#"
        };
        entry.Lines.Add(new JournalLine { AccountId = toAccountId, Debit = amount, Credit = 0m, Narration = $"Transfer in from {from.Name}" });
        entry.Lines.Add(new JournalLine { AccountId = fromAccountId, Debit = 0m, Credit = amount, Narration = $"Transfer out to {to.Name}" });

        try
        {
            _dbContext.JournalEntries.Add(entry);
            await _dbContext.SaveChangesAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
