using RetailBillingApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface IJournalService
{
    Task<List<JournalEntry>> GetAllJournalEntriesAsync();
    Task<JournalEntry?> GetJournalEntryByIdAsync(int id);
    Task<bool> PostJournalEntryAsync(JournalEntry entry);
    bool IsBalanced(JournalEntry entry);
    Task<int> SaveChangesAsync();

    /// <summary>ACC-03: reason the last PostJournalEntryAsync failed (null when it succeeded).</summary>
    string? LastValidationError { get; }
}
