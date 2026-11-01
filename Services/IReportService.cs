using RetailBillingApp.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetailBillingApp.Services;

public interface IReportService
{
    Task<DashboardStats> GetDashboardStatsAsync();
    Task<List<DailySales>> GetDailySalesAsync(System.DateTime startDate, System.DateTime endDate);
    Task<ProfitLossReport> GetProfitLossReportAsync(System.DateTime startDate, System.DateTime endDate);
    Task<List<StockAudit>> GetStockAuditAsync();

    /// <summary>RPT-07: stock audit — book vs stock-ledger tie-out, period movement, latest physical count and exception flags.</summary>
    Task<StockAuditReport> GetStockAuditReportAsync(DateTime startDate, DateTime endDate);
    Task<List<Receivable>> GetReceivablesAsync();
    Task<List<Payable>> GetPayablesAsync();
    Task<GstReport> GetGstReportAsync(System.DateTime startDate, System.DateTime endDate);
    Task<string> ExportGstr1JsonAsync(DateTime startDate, DateTime endDate);
    Task<string> ExportGstr3bJsonAsync(DateTime startDate, DateTime endDate);

    Task<BalanceSheetReport> GetBalanceSheetAsync(DateTime asOfDate);

    Task<CashFlowReport> GetCashFlowAsync(DateTime startDate, DateTime endDate);

    Task<DayBookReport> GetDayBookAsync(DateTime startDate, DateTime endDate);

    /// <summary>STK-09: per-product stock valuation. method = "Weighted Average" or "FIFO".</summary>
    Task<List<StockValuationRow>> GetStockValuationAsync(string method);

    /// <summary>PAY-07: customer ledger (invoices debit, receipts + credit notes credit) with running balance.</summary>
    Task<CustomerLedgerReport> GetCustomerLedgerAsync(int customerId, DateTime startDate, DateTime endDate);

    /// <summary>RPT-10: store-wise sales, margin and ranking for the period (estimates excluded).</summary>
    Task<List<StoreComparisonRow>> GetStoreComparisonAsync(DateTime startDate, DateTime endDate);

    /// <summary>PAY-07: write the customer statement as a text file on Desktop; returns the path.</summary>
    Task<string?> PrintCustomerStatementAsync(int customerId, DateTime startDate, DateTime endDate);

    Task<LedgerReport> GetLedgerAsync(int accountId, DateTime startDate, DateTime endDate);

    Task<List<CreditAnalysisRow>> GetCreditAnalysisAsync();
}

public class DashboardStats
{
    public decimal CashInHand { get; set; }
    public decimal StockValue { get; set; }
    public decimal DailySales { get; set; }
    public decimal PendingDues { get; set; }
    public int LowStockCount { get; set; }
    public int ExpiredCount { get; set; }
}

public class DailySales
{
    public System.DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public int InvoiceCount { get; set; }
}

public class ProfitLossReport
{
    public decimal TotalRevenue { get; set; }

    /// <summary>RPT-04: Cost of goods sold (latest purchase rate per product).</summary>
    public decimal TotalCOGS { get; set; }

    public decimal TotalExpenses { get; set; }

    public decimal GrossProfit => TotalRevenue - TotalCOGS;

    public decimal NetProfit => GrossProfit - TotalExpenses;

    public decimal GrossMarginPct => TotalRevenue == 0 ? 0 : GrossProfit / TotalRevenue * 100;

    public List<MonthlySummary> MonthlySummaries { get; set; } = new();

    /// <summary>RPT-04: expenses grouped by category.</summary>
    public List<CategorySummary> ExpenseBreakdown { get; set; } = new();
}

public class MonthlySummary
{
    public string Month { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public decimal COGS { get; set; }
    public decimal Expenses { get; set; }
    public decimal Profit => Revenue - COGS - Expenses;
}

public class CategorySummary
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>STK-09: one product's stock valuation row.</summary>
public class StockValuationRow
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public decimal StockQty { get; set; }

    /// <summary>Method-dependent unit cost (WAC or FIFO cost of the units on hand).</summary>
    public decimal UnitCost { get; set; }
    public decimal StockValue => System.Math.Round(StockQty * UnitCost, 2);

    /// <summary>Which costing basis produced this row (Weighted Average / FIFO / Mixed+Latest).</summary>
    public string Basis { get; set; } = string.Empty;
}

/// <summary>
/// RPT-10: one store's performance row in the multi-store comparison.
/// Margin uses the P&L convention: COGS from the latest purchase rate per product.
/// </summary>
public class StoreComparisonRow
{
    /// <summary>Rank by sales (1 = best); 0 for stores with no sales in the period.</summary>
    public int Rank { get; set; }

    public int? StoreId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;

    public int InvoiceCount { get; set; }

    /// <summary>Revenue excluding tax (sum of invoice SubTotal, estimates excluded).</summary>
    public decimal Sales { get; set; }

    /// <summary>Share of the period's total sales across all stores, 0-100.</summary>
    public decimal SalesSharePercent { get; set; }

    public decimal COGS { get; set; }
    public decimal GrossProfit { get; set; }

    public decimal MarginPercent => Sales > 0m ? System.Math.Round(GrossProfit * 100m / Sales, 1) : 0m;

    /// <summary>TotalAmount minus PaidAmount on that store's invoices.</summary>
    public decimal Outstanding { get; set; }
}

/// <summary>PAY-07: customer ledger report with opening/closing balance.</summary>
public class CustomerLedgerReport
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public System.DateTime StartDate { get; set; }
    public System.DateTime EndDate { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public List<CustomerLedgerRow> Rows { get; set; } = new();
}

/// <summary>PAY-07: one customer ledger line.</summary>
public class CustomerLedgerRow
{
    public System.DateTime Date { get; set; }
    /// <summary>Invoice / Receipt In / Credit Note.</summary>
    public string Type { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string? Narration { get; set; }

    /// <summary>Amount increasing the customer's receivable (invoices).</summary>
    public decimal Debit { get; set; }

    /// <summary>Amount reducing the receivable (receipts, credit notes).</summary>
    public decimal Credit { get; set; }
    public decimal RunningBalance { get; set; }
}

/// <summary>
/// ACC-09: Day book - all journal entries in a date range with their lines.
/// </summary>
public class DayBookReport
{
    public System.DateTime StartDate { get; set; }
    public System.DateTime EndDate { get; set; }
    public List<DayBookEntry> Entries { get; set; } = new();
}

public class DayBookEntry
{
    public System.DateTime EntryDate { get; set; }
    public string? Narration { get; set; }
    public string? ReferenceType { get; set; }
    public string? ReferenceId { get; set; }
    public List<DayBookLine> Lines { get; set; } = new();
    public decimal Total => Lines.Sum(l => l.Debit);
}

public class DayBookLine
{
    public string AccountName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Narration { get; set; }
}

/// <summary>
/// ACC-09: Account ledger with a running balance (sign-normal by account type).
/// </summary>
public class LedgerReport
{
    public string AccountName { get; set; } = string.Empty;
    public System.DateTime StartDate { get; set; }
    public System.DateTime EndDate { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public List<LedgerRow> Rows { get; set; } = new();
}

public class LedgerRow
{
    public System.DateTime Date { get; set; }
    public string? ReferenceType { get; set; }
    public string? ReferenceId { get; set; }
    public string? Narration { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal RunningBalance { get; set; }
}

/// <summary>
/// RPT-05: Balance sheet as of a date, built from chart-of-accounts journal balances.
/// </summary>
public class BalanceSheetReport
{
    public System.DateTime AsOfDate { get; set; }

    public List<AccountBalance> Assets { get; set; } = new();
    public List<AccountBalance> Liabilities { get; set; } = new();
    public List<AccountBalance> Equity { get; set; } = new();

    public decimal TotalAssets => Assets.Sum(a => a.Balance);
    public decimal TotalLiabilities => Liabilities.Sum(a => a.Balance);
    public decimal TotalEquityAccounts => Equity.Sum(a => a.Balance);

    /// <summary>Rounded retained earnings: net income (income accounts credit-net minus expense accounts debit-net).</summary>
    public decimal RetainedEarnings { get; set; }

    public decimal TotalLiabilitiesAndEquity => TotalLiabilities + TotalEquityAccounts + RetainedEarnings;

    public decimal Difference => TotalAssets - TotalLiabilitiesAndEquity;
}

public class AccountBalance
{
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public decimal DebitTotal { get; set; }
    public decimal CreditTotal { get; set; }

    /// <summary>Sign-normal: assets positive debit; liabilities/equity positive credit.</summary>
    public decimal Balance { get; set; }
}

/// <summary>
/// RPT-06: Cash flow statement built from journal movements on Cash and Bank accounts.
/// </summary>
public class CashFlowReport
{
    public System.DateTime StartDate { get; set; }
    public System.DateTime EndDate { get; set; }

    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance => OpeningBalance + NetCashFlow;
    public decimal NetCashFlow => Rows.Sum(r => r.Inflow - r.Outflow);

    /// <summary>One row per source (Sale, Purchase, Payment, Expense, ...).</summary>
    public List<CashFlowRow> Rows { get; set; } = new();
}

public class CashFlowRow
{
    public string Source { get; set; } = string.Empty;
    public decimal Inflow { get; set; }
    public decimal Outflow { get; set; }
    public decimal Net => Inflow - Outflow;
}

/// <summary>
/// RPT-11: Credit analysis per customer - limit usage, overdue exposure, average days to pay.
/// </summary>
public class CreditAnalysisRow
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    public decimal CreditLimit { get; set; }
    public int CreditDays { get; set; }

    /// <summary>Outstanding on unpaid/partially paid invoices.</summary>
    public decimal Used { get; set; }

    public decimal Available => CreditLimit > 0 ? Math.Max(0, CreditLimit - Used) : 0;

    public decimal UtilizationPct => CreditLimit > 0 ? Used / CreditLimit * 100 : 0;

    public int PendingInvoices { get; set; }

    /// <summary>Overdue portion: outstanding on invoices past their credit days.</summary>
    public decimal OverdueAmount { get; set; }

    public double AvgDaysToPay { get; set; }

    public string RiskFlag { get; set; } = "OK";
}

public class StockAudit
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int StockQty { get; set; }
    public int MinStockQty { get; set; }
    public bool IsLowStock => StockQty <= MinStockQty;
    public string? Batch { get; set; }
    public System.DateTime? ExpiryDate { get; set; }
    public bool IsExpired => ExpiryDate.HasValue && ExpiryDate.Value < System.DateTime.Today;
}

/// <summary>
/// RPT-07: one product on the stock audit sheet. The book quantity is the master
/// figure, the ledger quantity is what all recorded stock movements add up to, and
/// any gap between the two is an unexplained movement the auditor has to chase.
/// </summary>
public class StockAuditRow
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    public decimal BookQty { get; set; }
    public decimal OpeningQty { get; set; }
    public decimal QtyIn { get; set; }
    public decimal QtyOut { get; set; }
    public decimal ClosingQty { get; set; }

    /// <summary>Sum of every movement ever recorded for the product.</summary>
    public decimal LedgerQty { get; set; }

    /// <summary>Book minus ledger: non-zero means stock moved without a ledger entry (or vice versa).</summary>
    public decimal LedgerVariance { get; set; }

    /// <summary>Latest completed physical count, if the product was ever counted.</summary>
    public int? LastCountedQty { get; set; }
    public System.DateTime? LastCountDate { get; set; }

    /// <summary>Book minus last count; only meaningful when nothing moved after the count.</summary>
    public decimal CountVariance { get; set; }

    /// <summary>True when the count differs from the book and no movement explains the gap.</summary>
    public bool HasUnpostedCount { get; set; }

    public System.DateTime? LastMovementDate { get; set; }
    public int MovementCountInPeriod { get; set; }

    public decimal Rate { get; set; }
    public decimal StockValue { get; set; }

    /// <summary>Value of the book-vs-ledger variance at the product rate.</summary>
    public decimal VarianceValue { get; set; }

    public int MinStockQty { get; set; }
    public System.DateTime? ExpiryDate { get; set; }

    /// <summary>Audit findings for this product, most serious first. Empty = clean.</summary>
    public List<string> Exceptions { get; set; } = new();

    public bool HasException => Exceptions.Count > 0;
    public string ExceptionText => HasException ? string.Join(", ", Exceptions) : "OK";
}

/// <summary>RPT-07: stock audit sheet with its summary cards.</summary>
public class StockAuditReport
{
    public List<StockAuditRow> Rows { get; set; } = new();

    public System.DateTime FromDate { get; set; }
    public System.DateTime ToDate { get; set; }

    public int ProductCount { get; set; }
    public decimal TotalStockValue { get; set; }
    public int MismatchCount { get; set; }
    public decimal MismatchValue { get; set; }
    public int NegativeStockCount { get; set; }
    public int BelowMinimumCount { get; set; }
    public int ExpiredCount { get; set; }
    public int NoMovementCount { get; set; }
    public decimal NoMovementValue { get; set; }
    public int UnpostedCountCount { get; set; }
    public int CleanCount { get; set; }
    public System.DateTime? LastCountDate { get; set; }
}

public class Receivable
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal OutstandingAmount { get; set; }
    public int PendingInvoices { get; set; }

    /// <summary>ACC-05: outstanding with age ≤ 30 days.</summary>
    public decimal Current { get; set; }
    public decimal Days31To60 { get; set; }
    public decimal Days61To90 { get; set; }
    public decimal Days90Plus { get; set; }
    public System.DateTime OldestInvoiceDate { get; set; }
}

public class Payable
{
    public int Id { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int PendingBills { get; set; }
    public System.DateTime DueDate { get; set; }

    /// <summary>RPT-09: outstanding with age ≤ 30 days.</summary>
    public decimal Current { get; set; }

    public decimal Days31To60 { get; set; }
    public decimal Days61To90 { get; set; }
    public decimal Days90Plus { get; set; }
}

public class GstReport
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal TotalSales { get; set; }
    public decimal TotalTax { get; set; }
    public int InvoiceCount { get; set; }

    /// <summary>GSTR-1 Table 4 (B2B) - invoices with customer GSTIN.</summary>
    public List<GstB2bInvoice> B2b { get; set; } = new();

    /// <summary>GSTR-1 Table 5/7 (B2C) - invoices without customer GSTIN (incl. Bill of Supply).</summary>
    public List<GstB2cInvoice> B2c { get; set; } = new();

    /// <summary>GSTR-1 Table 9B (CDNR) - credit notes against invoices.</summary>
    public List<GstCreditNote> CreditNotes { get; set; } = new();

    /// <summary>GSTR-1 Table 12 (HSN summary) - items grouped by HSN code.</summary>
    public List<GstHsnSummary> HsnSummary { get; set; } = new();

    // ---- GSTR-3B outward supplies (Table 3.1) ----
    public decimal OutwardTaxableValue { get; set; }
    public decimal OutwardCGST { get; set; }
    public decimal OutwardSGST { get; set; }
    public decimal OutwardIGST { get; set; }
    public decimal OutwardCess { get; set; }

    /// <summary>GSTR-3B Table 4 - input tax credit from purchase bills.</summary>
    public decimal InputTaxCredit { get; set; }

    /// <summary>Tax payable after deducting ITC and credit notes.</summary>
    public decimal NetTaxPayable => Math.Max(0, OutwardCGST + OutwardSGST + OutwardIGST + OutwardCess - InputTaxCredit - CreditNotes.Sum(c => c.TaxValue));
}

public class GstB2bInvoice
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerGstin { get; set; } = string.Empty;
    public decimal TaxableValue { get; set; }
    public decimal InvoiceValue { get; set; }
    public decimal TaxValue { get; set; }
    public decimal Cgst { get; set; }
    public decimal Sgst { get; set; }
    public decimal Igst { get; set; }
    public string PlaceOfSupply { get; set; } = string.Empty;
    public string ReverseCharge { get; set; } = "N";
    public string InvoiceType { get; set; } = "Regular";
}

public class GstB2cInvoice
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TaxableValue { get; set; }
    public decimal InvoiceValue { get; set; }
    public decimal TaxValue { get; set; }
    public decimal Cgst { get; set; }
    public decimal Sgst { get; set; }
    public decimal Igst { get; set; }
    public string PlaceOfSupply { get; set; } = string.Empty;
    /// <summary>True when the document is a Bill of Supply (nil-rated).</summary>
    public bool IsBillOfSupply { get; set; }
}

public class GstCreditNote
{
    public string CreditNoteNumber { get; set; } = string.Empty;
    public DateTime CreditNoteDate { get; set; }
    public string OriginalInvoiceNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerGstin { get; set; } = string.Empty;
    public decimal TaxableValue { get; set; }
    public decimal CreditNoteValue { get; set; }
    public decimal TaxValue { get; set; }
    public string Type { get; set; } = "Credit";
    public string Reason { get; set; } = string.Empty;
}

public class GstHsnSummary
{
    public string HsnCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Uqc { get; set; }
    public decimal TotalValue { get; set; }
    public decimal TaxableValue { get; set; }
    public decimal TotalTax { get; set; }
    public decimal Cgst { get; set; }
    public decimal Sgst { get; set; }
    public decimal Igst { get; set; }
}
