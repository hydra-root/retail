using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

public class Reminder
{
    [Key]
    public int Id { get; set; }

    public int? CustomerId { get; set; }

    [MaxLength(200)]
    public string? CustomerName { get; set; }

    [MaxLength(100)]
    public string? InvoiceNumber { get; set; }

    public DateTime DateSent { get; set; } = DateTime.UtcNow;

    [MaxLength(50)]
    public string Status { get; set; } = "Pending";

    [ForeignKey("CustomerId")]
    public Customer? Customer { get; set; }
}
