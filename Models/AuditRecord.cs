using System.ComponentModel.DataAnnotations;

namespace RetailBillingApp.Models;

/// <summary>
/// ACC-10 Audit trail: who changed what, with before/after property values.
/// Complements the USR-07 activity log by recording field-level diffs.
/// </summary>
public class AuditRecord
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    /// <summary>Create, Update, Delete, Convert, WriteOff, ...</summary>
    [Required]
    [MaxLength(50)]
    public string Action { get; set; } = string.Empty;

    /// <summary>Entity class name, e.g. Product, Customer, Invoice, Payment.</summary>
    [Required]
    [MaxLength(100)]
    public string EntityName { get; set; } = string.Empty;

    public int EntityId { get; set; }

    /// <summary>Human label of the record, e.g. invoice number or product name at time of change.</summary>
    [MaxLength(200)]
    public string? EntityLabel { get; set; }

    /// <summary>JSON of changed properties: { "PropertyName": { "before": ..., "after": ... }, ... } — empty on create.</summary>
    public string? ChangesJson { get; set; }

    /// <summary>Full serialized snapshot after the change (null on delete).</summary>
    public string? SnapshotJson { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
