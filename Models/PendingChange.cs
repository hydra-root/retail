using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>
/// STK-11: one local-first change waiting for (or already delivered to) the HQ sync endpoint.
/// The tills append rows as they work and <c>OfflineQueueService.FlushAsync</c> replays them in
/// CreatedAt order, so billing, stock and receipts keep flowing while the connection does not.
/// </summary>
public class PendingChange
{
    [Key]
    public int Id { get; set; }

    /// <summary>Document class the change carries — one of <see cref="EntityTypeOptions"/>.</summary>
    [Required]
    [MaxLength(50)]
    public string EntityType { get; set; } = EntityInvoice;

    /// <summary>Create / Update / Delete — one of <see cref="ActionOptions"/>.</summary>
    [Required]
    [MaxLength(20)]
    public string Action { get; set; } = ActionCreate;

    /// <summary>Business key of the document (invoice number, payment number, SKU): stable across stores.</summary>
    [Required]
    [MaxLength(100)]
    public string EntityKey { get; set; } = string.Empty;

    /// <summary>Store that produced the change; null means global/legacy, same convention as StockTransaction.</summary>
    public int? StoreId { get; set; }

    /// <summary>The document exactly as the endpoint receives it. Free-form JSON, so TEXT.</summary>
    public string? PayloadJson { get; set; }

    /// <summary>Queued / Synced / Failed / Discarded — one of <see cref="StatusOptions"/>.</summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = StatusQueued;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set on every replay attempt, successful or not.</summary>
    public DateTime? AttemptedAt { get; set; }

    /// <summary>Set when the endpoint accepted the row.</summary>
    public DateTime? SyncedAt { get; set; }

    /// <summary>Replay attempts burned so far; the ceiling lives in <see cref="OfflineSetting.MaxAttempts"/>.</summary>
    public int Attempts { get; set; }

    /// <summary>Why the last attempt was rejected — shown in the queue grid and the detail pane.</summary>
    [MaxLength(500)]
    public string? LastError { get; set; }

    // ---- vocabularies shared by the queue service, the enqueue call sites and the screen ----

    public const string ActionCreate = "Create";
    public const string ActionUpdate = "Update";
    public const string ActionDelete = "Delete";

    public static List<string> ActionOptions { get; } = new() { ActionCreate, ActionUpdate, ActionDelete };

    public const string StatusQueued = "Queued";
    public const string StatusSynced = "Synced";
    public const string StatusFailed = "Failed";
    public const string StatusDiscarded = "Discarded";

    public static List<string> StatusOptions { get; } =
        new() { StatusQueued, StatusSynced, StatusFailed, StatusDiscarded };

    /// <summary>
    /// Document classes the queue knows how to carry. Add one here plus an EnqueueAsync call at the
    /// matching service/view model; the transport stays unchanged.
    /// </summary>
    public const string EntityInvoice = "Invoice";
    public const string EntityStockTransaction = "StockTransaction";
    public const string EntityPayment = "Payment";
    public const string EntityProduct = "Product";
    public const string EntityCustomer = "Customer";

    public static List<string> EntityTypeOptions { get; } =
        new() { EntityInvoice, EntityStockTransaction, EntityPayment, EntityProduct, EntityCustomer };

    // ---- [NotMapped] display helpers (the grid binds these instead of raw columns) ----

    /// <summary>Rows the replay can pick up again: queued, or failed and manually retried.</summary>
    [NotMapped]
    public bool IsReplayable => Status is StatusQueued or StatusFailed;

    [NotMapped]
    public string StoreLabel => StoreId == null ? "All stores" : $"Store #{StoreId}";

    [NotMapped]
    public string AttemptText => Attempts == 0 ? "not tried" : $"{Attempts} of ceiling";

    [NotMapped]
    public string LastAttemptText =>
        AttemptedAt == null ? "never" : AttemptedAt.Value.ToLocalTime().ToString("dd-MMM-yyyy HH:mm:ss");

    [NotMapped]
    public bool HasError => !string.IsNullOrWhiteSpace(LastError);

    /// <summary>One-line identity of the row, used in audit labels and messages.</summary>
    [NotMapped]
    public string RowLabel => $"{EntityType} {Action} \u2018{EntityKey}\u2019";

    [NotMapped]
    public string StatusHint => Explain(Status);

    /// <summary>Plain-language meaning of a status, shared by the grid tooltip and the screen legend.</summary>
    public static string Explain(string status) => status switch
    {
        StatusSynced => "Synced: the HQ endpoint accepted this change; it is kept for history until purged.",
        StatusFailed => "Failed: the attempt ceiling was reached, so replay skips this row until an operator retries or discards it.",
        StatusDiscarded => "Discarded: deliberately dropped from the queue; the local document is untouched.",
        _ => "Queued: waiting for the next sync run to hand it to the HQ endpoint.",
    };
}
