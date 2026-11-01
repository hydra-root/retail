using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using System.Data.Common;

namespace RetailBillingApp.Services;

/// <summary>
/// SEC-07: data integrity checks. Uses PRAGMA integrity_check, counts orphan
/// rows (never deletes them), flags overpaid invoices and allocation mismatches,
/// then — if the structure is sound — truncates the WAL. VACUUM rebuilds the
/// database file and ANALYZE refreshes planner statistics.
/// </summary>
public class IntegrityService : IIntegrityService
{
    private readonly AppDbContext _dbContext;
    private const string OrphanNote = "No rows were deleted; review before any manual fix.";

    public IntegrityService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<List<IntegrityResultRow>> RunIntegrityCheckAsync()
    {
        var results = new List<IntegrityResultRow>();
        var connection = _dbContext.Database.GetDbConnection();

        try
        {
            // a) Structural integrity of the database file itself
            var check = await ScalarAsync(connection, "PRAGMA integrity_check;");
            if (string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new IntegrityResultRow
                {
                    Check = "Database structure (PRAGMA integrity_check)",
                    Status = "OK",
                    Detail = "No corruption detected."
                });

                // f) On success only: flush and truncate the WAL, report freed bytes
                var pageSize =
                    (int)ToLong(await ScalarAsync(connection, "PRAGMA page_size;"));

                // Returns one row: busy | log pages | checkpointed pages
                var checkpointParts = await FirstRowAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
                var freedBytes = checkpointParts.Length >= 3 && long.TryParse(checkpointParts[2], out var checkpointed)
                    ? checkpointed * pageSize
                    : 0L;

                results.Add(new IntegrityResultRow
                {
                    Check = "Quick action",
                    Status = "Action taken",
                    Detail = $"WAL checkpoint (TRUNCATE) completed; {freedBytes / 1024.0:F1} KB flushed back into the database."
                });
            }
            else
            {
                results.Add(new IntegrityResultRow
                {
                    Check = "Database structure (PRAGMA integrity_check)",
                    Status = "Problem",
                    Detail = $"Corruption detected: {check}. Restore from a backup (Backup & Restore screen) before further use."
                });
            }

            // b) Orphan InvoiceItems whose product no longer exists (count only, never delete)
            var orphanInvoiceItems = (int)ToLong(await ScalarAsync(connection,
                "SELECT COUNT(*) FROM InvoiceItems ii " +
                "WHERE ii.ProductId IS NOT NULL " +
                "AND NOT EXISTS (SELECT 1 FROM Products p WHERE p.Id = ii.ProductId);"));
            results.Add(new IntegrityResultRow
            {
                Check = "Orphan invoice lines",
                Status = orphanInvoiceItems > 0 ? "Problem" : "OK",
                Detail = orphanInvoiceItems > 0
                    ? $"{orphanInvoiceItems:N0} invoice line(s) reference a deleted product (kept as text for billing history). {OrphanNote}"
                    : "Every invoice line points to an existing product."
            });

            // c) Orphan JournalLines (missing entry OR missing account)
            var orphanJournalLines = (int)ToLong(await ScalarAsync(connection,
                "SELECT COUNT(*) FROM JournalLines jl " +
                "WHERE NOT EXISTS (SELECT 1 FROM JournalEntries je WHERE je.Id = jl.JournalEntryId) " +
                "OR NOT EXISTS (SELECT 1 FROM Accounts a WHERE a.Id = jl.AccountId);"));
            results.Add(new IntegrityResultRow
            {
                Check = "Orphan journal lines",
                Status = orphanJournalLines > 0 ? "Problem" : "OK",
                Detail = orphanJournalLines > 0
                    ? $"{orphanJournalLines:N0} journal line(s) have no parent entry or no account; they may distort ledger balances. {OrphanNote}"
                    : "Every journal line points to an existing entry and account."
            });

            // d) Overpaid invoices: PaidAmount > TotalAmount
            var overpaid = (int)ToLong(await ScalarAsync(connection,
                "SELECT COUNT(*) FROM Invoices i WHERE i.PaidAmount > i.TotalAmount;"));
            results.Add(new IntegrityResultRow
            {
                Check = "Overpaid invoices",
                Status = overpaid > 0 ? "Problem" : "OK",
                Detail = overpaid > 0
                    ? $"overpaid invoices: {overpaid:N0} — amount received exceeds the invoice total."
                    : "No invoice has been paid more than its total."
            });

            // e) Informational: PaymentAllocation totals vs invoice PaidAmount
            var allocationMismatch = (int)ToLong(await ScalarAsync(connection,
                "SELECT COUNT(*) FROM Invoices i " +
                "WHERE COALESCE((SELECT SUM(pa.AllocatedAmount) FROM PaymentAllocations pa " +
                "                WHERE pa.InvoiceId = i.Id), 0) <> i.PaidAmount;"));
            results.Add(new IntegrityResultRow
            {
                Check = "Payment allocations vs PaidAmount",
                Status = allocationMismatch > 0 ? "Problem" : "OK",
                Detail = allocationMismatch > 0
                    ? $"{allocationMismatch:N0} invoice(s) where allocated payments do not sum to the recorded PaidAmount (informational)."
                    : "Allocated payments match the recorded PaidAmount on every invoice."
            });
        }
        catch (Exception ex)
        {
            results.Add(new IntegrityResultRow
            {
                Check = "Integrity check",
                Status = "Problem",
                Detail = $"Check failed: {ex.Message}"
            });
        }

        return results;
    }

    public async Task<List<IntegrityResultRow>> VacuumAsync()
    {
        var results = new List<IntegrityResultRow>();
        var connection = _dbContext.Database.GetDbConnection();

        try
        {
            var sizeBefore = await GetDatabaseBytesAsync(connection);

            await ExecAsync(connection, "VACUUM;");

            var sizeAfter = await GetDatabaseBytesAsync(connection);
            results.Add(new IntegrityResultRow
            {
                Check = "VACUUM",
                Status = "Action taken",
                Detail = $"Database rebuilt and all rows re-inserted; file size {sizeBefore / 1024.0:F1} KB → {sizeAfter / 1024.0:F1} KB."
            });

            await ExecAsync(connection, "ANALYZE;");
            results.Add(new IntegrityResultRow
            {
                Check = "ANALYZE",
                Status = "Action taken",
                Detail = "Table statistics refreshed for faster queries."
            });
        }
        catch (Exception ex)
        {
            results.Add(new IntegrityResultRow
            {
                Check = "VACUUM",
                Status = "Problem",
                Detail = $"VACUUM/ANALYZE failed: {ex.Message}"
            });
        }

        return results;
    }

    // ---- helpers ----

    private static async Task<string> ScalarAsync(DbConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var scalar = await cmd.ExecuteScalarAsync();
        return scalar?.ToString() ?? "";
    }

    /// <summary>Returns the values of the first row returned (PRAGMA results).</summary>
    private static async Task<string[]> FirstRowAsync(DbConnection connection, string sql)
    {
        var values = Array.Empty<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            values = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
                values[i] = reader.IsDBNull(i) ? "0" : reader.GetValue(i).ToString() ?? "0";
        }
        return values;
    }

    private static async Task ExecAsync(DbConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static long ToLong(string? value) =>
        long.TryParse(value, out var n) ? n : 0L;

    private static async Task<long> GetDatabaseBytesAsync(DbConnection connection)
    {
        var page =
            ToLong(await ScalarAsync(connection, "PRAGMA page_size;"));
        var count =
            ToLong(await ScalarAsync(connection, "PRAGMA page_count;"));
        return page * count;
    }
}
