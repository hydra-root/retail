using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

/// <summary>
/// STK-03: single source of truth for what happens when a stock movement would
/// push a product below zero. Every stock-moving service asks EvaluateAsync instead of
/// hard-coding its own guard, so billing, adjustments, stocktakes and transfers agree.
/// </summary>
public class StockPolicyService : IStockPolicyService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuthService _authService;
    private readonly IAuditService _auditService;

    public List<string> Modes { get; } = new() { "Block", "Warn", "Allow" };

    public StockPolicyService(AppDbContext dbContext, IAuthService authService, IAuditService auditService)
    {
        _dbContext = dbContext;
        _authService = authService;
        _auditService = auditService;
    }

    public async Task<StockPolicySetting> GetSettingsAsync()
    {
        var settings = await _dbContext.StockPolicySettings.FirstOrDefaultAsync(s => s.Id == 1);
        if (settings == null)
        {
            settings = new StockPolicySetting { Id = 1 };
            _dbContext.StockPolicySettings.Add(settings);
            await _dbContext.SaveChangesAsync();
        }
        return settings;
    }

    public async Task<(bool Success, string? Error)> SaveSettingsAsync(string negativeStockMode)
    {
        if (!Modes.Contains(negativeStockMode))
            return (false, $"Unknown negative-stock mode '{negativeStockMode}'.");

        var settings = await GetSettingsAsync();
        var previous = settings.NegativeStockMode;
        if (previous == negativeStockMode)
            return (false, $"Policy is already set to {negativeStockMode}.");

        settings.NegativeStockMode = negativeStockMode;
        settings.UpdatedAt = System.DateTime.UtcNow;
        settings.UpdatedBy = _authService.CurrentUser?.Username;

        try
        {
            _dbContext.StockPolicySettings.Update(settings);
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            return (false, "Failed to save the stock policy.");
        }

        await _auditService.RecordAsync("Update", "StockPolicy", settings.Id, "Negative stock policy",
            new { NegativeStockMode = previous }, new { settings.NegativeStockMode });

        return (true, null);
    }

    public async Task<(bool Allowed, string? Warning)> EvaluateAsync(
        string productName, decimal currentQty, decimal quantityChange, string context)
    {
        var projected = currentQty + quantityChange;
        if (projected >= 0) return (true, null);

        var settings = await GetSettingsAsync();
        var detail = $"{context}: '{productName}' would go to {projected:0.##} units " +
                     $"(on hand {currentQty:0.##}, change {quantityChange:+0.##;-0.##;0}).";

        return settings.NegativeStockMode switch
        {
            "Allow" => (true, null),
            "Warn" => (true, detail + " Allowed by policy."),
            _ => (false, detail + " Blocked by policy."),
        };
    }
}
