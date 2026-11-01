using RetailBillingApp.Models;

namespace RetailBillingApp.Services;

public interface IAutoPostingService
{
    Task<bool> PostSaleAsync(int saleId);
    Task<bool> PostPurchaseAsync(int purchaseId);
    Task<bool> PostPaymentAsync(int accountId, decimal amount, bool isDebit, string narration);

    /// <summary>ACC-02: post a register invoice (estimates post nothing).</summary>
    Task<bool> PostInvoiceAsync(int invoiceId);

    /// <summary>ACC-02: post a party-based payment/receipt (Direction In/Out).</summary>
    Task<bool> PostPartyPaymentAsync(int paymentId);

    /// <summary>ACC-02: post a POS receipt (part payment recorded against a Sale).</summary>
    Task<bool> PostSaleReceiptAsync(int saleId, decimal amount);
}
