using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

/// <summary>
/// STK-03: the negative-stock policy shared by every stock-moving path
/// (billing, estimate conversion, adjustment, stocktake, transfer, credit/debit notes).
/// </summary>
public interface IStockPolicyService
{
    /// <summary>Policy modes accepted by SaveSettingsAsync.</summary>
    List<string> Modes { get; }

    Task<StockPolicySetting> GetSettingsAsync();

    Task<(bool Success, string? Error)> SaveSettingsAsync(string negativeStockMode);

    /// <summary>
    /// Decides whether a movement of <paramref name="quantityChange"/> units on
    /// <paramref name="currentQty"/> may proceed under the configured policy.
    /// Allowed=false only in Block mode; Warn mode lets it through with a message.
    /// </summary>
    Task<(bool Allowed, string? Warning)> EvaluateAsync(
        string productName, decimal currentQty, decimal quantityChange, string context);
}
