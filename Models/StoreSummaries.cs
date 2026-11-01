using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RetailBillingApp.Models;

/// <summary>Store-wise sales summary for the central multi-store dashboard (USR-02).</summary>
public class StoreSalesSummary
{
    public int? StoreId { get; set; }

    [MaxLength(200)]
    public string StoreName { get; set; } = string.Empty; // "Unassigned" when StoreId is null

    public int InvoiceCount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalSales { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalTax { get; set; }

    public DateTime? LastInvoiceDate { get; set; }
}

/// <summary>Stock on hand for one product in one store (USR-03 store-wise stock).</summary>
public class StoreStockSummary
{
    public int ProductId { get; set; }

    [MaxLength(200)]
    public string ProductName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? SKU { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal QuantityOnHand { get; set; }
}
