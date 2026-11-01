using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace RetailBillingApp.Models;

public class JournalEntry
{
    [Key]
    public int Id { get; set; }

    public DateTime EntryDate { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Narration { get; set; }

    [MaxLength(100)]
    public string? ReferenceType { get; set; } // Invoice, Purchase, Payment, etc.

    [MaxLength(100)]
    public string? ReferenceId { get; set; }

    public List<JournalLine> Lines { get; set; } = new();
}
