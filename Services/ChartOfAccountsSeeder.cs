using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// ACC-02: guarantee the standard chart of accounts the auto-posting engine
/// journals against. Upsert-by-Name and idempotent — safe to run on every
/// startup; existing accounts (including user-renamed/edited ones) are never
/// touched, only missing names are inserted.
/// </summary>
public static class ChartOfAccountsSeeder
{
    private static readonly (string Name, string AccountType)[] RequiredAccounts =
    [
        ("Cash", "Asset"),
        ("Bank", "Asset"),
        ("Sales", "Income"),
        ("Purchases", "Expense"),
        ("Debtors", "Asset"),
        ("Creditors", "Liability"),
        ("GST Input", "Asset"),
        ("GST Output", "Liability"),
        ("Round Off", "Income"),
        ("Write Off", "Expense"),
        ("Capital Account", "Equity")
    ];

    public static async Task SeedAsync(AppDbContext db)
    {
        var existing = await db.Accounts.AsNoTracking()
            .Select(a => a.Name)
            .ToListAsync();

        var missing = RequiredAccounts
            .Where(r => !existing.Contains(r.Name, StringComparer.Ordinal))
            .Select(r => new Account
            {
                Name = r.Name,
                AccountType = r.AccountType, // only non-nullable required field besides Name
                IsSystem = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            })
            .ToList();

        if (missing.Count == 0)
            return; // nothing to seed — no audit, no return value

        db.Accounts.AddRange(missing);
        await db.SaveChangesAsync();
    }
}
