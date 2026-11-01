using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface INumberSeriesService
{
    Task<List<NumberSeries>> GetAllNumberSeriesAsync();
    ValueTask<NumberSeries?> GetNumberSeriesByIdAsync(int id);
    Task<NumberSeries?> GetNumberSeriesAsync(string docType, string financialYear, int? storeId = null);
    Task<string> GetNextNumberAsync(string docType, string financialYear, int? storeId = null);
    Task<bool> AddNumberSeriesAsync(NumberSeries series);
    Task<bool> UpdateNumberSeriesAsync(NumberSeries series);
    Task<bool> DeleteNumberSeriesAsync(int id);
    Task<int> SaveChangesAsync();

    /// <summary>INV-03: clone every ResetEachYear series into the new financial year (starting at StartNumber). Returns number of series created.</summary>
    Task<int> RollOverFinancialYearAsync(string newFinancialYear);

    /// <summary>INV-03: gap-free audit — actual issued document numbers per series vs the expected sequence.</summary>
    Task<List<NumberGapRow>> GetGapReportAsync();
}

/// <summary>INV-03: one row of the numbering gap audit.</summary>
public class NumberGapRow
{
    public string DocType { get; set; } = string.Empty;
    public string SeriesName { get; set; } = string.Empty;
    public string FinancialYear { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;

    /// <summary>How many documents were issued matching this series in the FY window.</summary>
    public int Issued { get; set; }

    public int FirstNumber { get; set; }
    public int LastNumber { get; set; }

    /// <summary>Missing sequence values, e.g. "1025, 1028 (+3 more)".</summary>
    public string Missing { get; set; } = string.Empty;
    public int MissingCount { get; set; }

    /// <summary>Detects the same serial appearing on two documents.</summary>
    public string Duplicates { get; set; } = string.Empty;

    /// <summary>OK / Gaps / Duplicates / No documents issued.</summary>
    public string Status { get; set; } = string.Empty;
}
