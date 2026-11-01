namespace RetailBillingApp.Services;

/// <summary>
/// SEC-07: data integrity checks (integrity_check, orphan detection, WAL checkpoint, VACUUM).
/// </summary>
public interface IIntegrityService
{
    /// <summary>Runs the full integrity check and returns one result row per check.</summary>
    Task<List<IntegrityResultRow>> RunIntegrityCheckAsync();

    /// <summary>Rebuilds the database (VACUUM) and refreshes query planner statistics (ANALYZE).</summary>
    Task<List<IntegrityResultRow>> VacuumAsync();
}

/// <summary>One line of the integrity report. Status is "OK", "Problem" or "Action taken".</summary>
public class IntegrityResultRow
{
    public string Check { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}
