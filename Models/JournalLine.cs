using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class JournalLine
{
    [Key]
    public int Id { get; set; }

    public int JournalEntryId { get; set; }

    public int AccountId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Debit { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Credit { get; set; }

    [MaxLength(500)]
    public string? Narration { get; set; }

    [ForeignKey("JournalEntryId")]
    public JournalEntry? JournalEntry { get; set; }

    [ForeignKey("AccountId")]
    public Account? Account { get; set; }
}
