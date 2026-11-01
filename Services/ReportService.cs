using Microsoft.EntityFrameworkCore;
using RetailBillingApp.Data;
using RetailBillingApp.Models;
using System.IO;

namespace RetailBillingApp.Services;

public class ReportService : IReportService
{
    private readonly AppDbContext _dbContext;

    public ReportService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<DashboardStats> GetDashboardStatsAsync()
    {
        var today = System.DateTime.Today;

        var cashInvoices = await _dbContext.Invoices
            .Where(i => i.PaymentMode == "Cash")
            .Select(i => i.TotalAmount)
            .ToListAsync();

        var products = await _dbContext.Products
            .Select(p => new { p.Rate, p.StockQty, p.MinStockQty, p.ExpiryDate })
            .ToListAsync();

        var dailyInvoices = await _dbContext.Invoices
            .Where(i => i.InvoiceDate.Date == today)
            .Select(i => i.TotalAmount)
            .ToListAsync();

        var pendingSales = await _dbContext.Sales
            .Where(s => s.PaidAmount < s.Amount)
            .Select(s => new { s.Amount, s.PaidAmount })
            .ToListAsync();

        return new DashboardStats
        {
            CashInHand = cashInvoices.Sum(),
            StockValue = products.Sum(p => p.Rate * p.StockQty),
            DailySales = dailyInvoices.Sum(),
            PendingDues = pendingSales.Sum(s => s.Amount - s.PaidAmount),
            LowStockCount = products.Count(p => p.StockQty <= p.MinStockQty),
            ExpiredCount = products.Count(p => p.ExpiryDate.HasValue && p.ExpiryDate.Value < System.DateTime.Today)
        };
    }

    public async Task<List<DailySales>> GetDailySalesAsync(System.DateTime startDate, System.DateTime endDate)
    {
        var invoices = await _dbContext.Invoices
            .Where(i => i.InvoiceDate >= startDate && i.InvoiceDate <= endDate)
            .ToListAsync();

        return invoices
            .GroupBy(i => i.InvoiceDate.Date)
            .Select(g => new DailySales
            {
                Date = g.Key,
                Amount = g.Sum(i => i.TotalAmount),
                InvoiceCount = g.Count()
            })
            .OrderBy(x => x.Date).ToList();
    }

    public async Task<List<CreditAnalysisRow>> GetCreditAnalysisAsync()
    {
        var today = System.DateTime.Today;

        var customers = await _dbContext.Customers.ToListAsync();

        // Unpaid invoices per customer
        var customerInvoices = await _dbContext.Invoices
            .Select(i => new { i.Id, i.CustomerId, i.InvoiceDate, i.TotalAmount, i.PaidAmount, i.Status })
            .ToListAsync();

        // Average days to pay: payment-in allocations joined via invoice ids
        var paidInvoices = new List<(int CustomerId, DateTime InvoiceDate, DateTime PaidDate)>();
        var invoicesById = await _dbContext.Invoices
            .Select(i => new { i.Id, i.CustomerId, i.InvoiceDate })
            .ToDictionaryAsync(i => i.Id, i => (Customer: i.CustomerId, Date: i.InvoiceDate));
        var inPayments = await _dbContext.Payments
            .Where(p => p.Direction == "In")
            .Select(p => new { p.Id, p.PaymentDate })
            .ToDictionaryAsync(p => p.Id, p => p.PaymentDate);
        var allocations = await _dbContext.PaymentAllocations
            .Where(a => a.AllocationType == "Invoice" && a.InvoiceId != null)
            .Select(a => new { a.PaymentId, a.InvoiceId })
            .ToListAsync();

        foreach (var alloc in allocations)
        {
            if (invoicesById.TryGetValue(alloc.InvoiceId!.Value, out var invoice)
                && inPayments.TryGetValue(alloc.PaymentId, out var paidDate)
                && invoice.Customer.HasValue)
            {
                paidInvoices.Add((invoice.Customer.Value, invoice.Date, paidDate));
            }
        }

        var avgDaysByCustomer = paidInvoices
            .GroupBy(x => x.CustomerId)
            .ToDictionary(g => g.Key, g => g.Average(x => (x.PaidDate.Date - x.InvoiceDate.Date).Days));

        var todayDate = today.Date;

        var rows = new List<CreditAnalysisRow>();
        foreach (var c in customers)
        {
            var myInvoices = customerInvoices.Where(i => i.CustomerId == c.Id).ToList();
            var pending = myInvoices.Where(i => i.Status != "Paid").ToList();

            var used = pending.Sum(i => i.TotalAmount - i.PaidAmount);
            var overdue = pending
                .Where(i => (todayDate - i.InvoiceDate.Date).Days > c.CreditDays)
                .Sum(i => i.TotalAmount - i.PaidAmount);

            var avgDays = avgDaysByCustomer.TryGetValue(c.Id, out var avg) ? avg : 0;

            var utilization = c.CreditLimit > 0 ? used / c.CreditLimit * 100 : 0m;
            var risk = c.CreditLimit <= 0
                ? "No Limit"
                : utilization >= 100m ? "Over Limit"
                : utilization >= 75m ? "High"
                : utilization >= 50m ? "Medium"
                : overdue > 0 ? "Overdue"
                : "OK";

            rows.Add(new CreditAnalysisRow
            {
                CustomerId = c.Id,
                CustomerName = c.Name,
                Mobile = c.Mobile,
                CreditLimit = c.CreditLimit,
                CreditDays = c.CreditDays,
                Used = used,
                PendingInvoices = pending.Count,
                OverdueAmount = overdue,
                AvgDaysToPay = Math.Round(avgDays, 1),
                RiskFlag = risk
            });
        }

        return rows
            .OrderByDescending(r => r.UtilizationPct)
            .ThenByDescending(r => r.Used)
            .ToList();
    }

    public async Task<CashFlowReport> GetCashFlowAsync(System.DateTime startDate, System.DateTime endDate)
    {
        var cashAccountNames = new[] { "Cash", "Bank" };

        var startExclusive = startDate.Date;
        var endExclusive = endDate.Date.AddDays(1);

        var lines = await _dbContext.JournalLines
            .Include(l => l.Account)
            .Include(l => l.JournalEntry)
            .Where(l => l.Account != null && cashAccountNames.Contains(l.Account!.Name))
            .ToListAsync();

        var report = new CashFlowReport { StartDate = startDate, EndDate = endDate };

        // Opening balance: all cash/bank movements strictly before the start date
        decimal openingDebit = 0m, openingCredit = 0m;
        foreach (var l in lines.Where(l => l.JournalEntry!.EntryDate < startExclusive))
        {
            openingDebit += l.Debit;
            openingCredit += l.Credit;
        }
        report.OpeningBalance = openingDebit - openingCredit;

        // Movements within the period, grouped by source
        var periodLines = lines.Where(l =>
            l.JournalEntry!.EntryDate >= startExclusive && l.JournalEntry.EntryDate < endExclusive);

        foreach (var group in periodLines
                     .GroupBy(l => l.JournalEntry!.ReferenceType ?? "Other"))
        {
            var inflow = group.Sum(l => l.Debit);   // money into cash/bank
            var outflow = group.Sum(l => l.Credit); // money out of cash/bank
            report.Rows.Add(new CashFlowRow
            {
                Source = group.Key,
                Inflow = inflow,
                Outflow = outflow
            });
        }

        report.Rows = report.Rows.OrderBy(r => r.Source).ToList();
        return report;
    }

    /// <summary>
    /// STK-09: value on-hand stock from purchase-cost layers.
    /// "Weighted Average": unit cost = total purchase value / total purchased qty.
    /// "FIFO": consume oldest layers by the stock already shipped, value what remains in layers;
    /// any on-hand quantity without a cost layer (e.g. opening stock entered by adjustment)
    /// falls back to the product's weighted average and is flagged "Mixed+Latest".
    /// </summary>
    public async Task<List<StockValuationRow>> GetStockValuationAsync(string method)
    {
        var products = await _dbContext.Products.AsNoTracking()
            .Select(p => new { p.Id, p.Name, p.SKU, p.StockQty })
            .ToListAsync();

        // Purchase cost layers, oldest first
        var receipts = await _dbContext.PurchaseItems
            .Join(_dbContext.Purchases,
                i => i.PurchaseId, p => p.Id,
                (i, p) => new { ItemId = i.Id, i.ProductId, i.Quantity, i.Rate, p.PurchaseDate })
            .OrderBy(x => x.PurchaseDate).ThenBy(x => x.ItemId)
            .ToListAsync();

        var result = new List<StockValuationRow>();
        var isFifo = string.Equals(method, "FIFO", System.StringComparison.OrdinalIgnoreCase);

        foreach (var grp in receipts.GroupBy(r => r.ProductId))
        {
            var product = products.FirstOrDefault(p => p.Id == grp.Key);
            if (product == null) continue;

            var purchasedQty = grp.Sum(r => (decimal)r.Quantity);
            var purchasedValue = grp.Sum(r => (decimal)r.Quantity * r.Rate);
            if (purchasedQty <= 0) continue;
            var wac = purchasedValue / purchasedQty;

            var onHand = product.StockQty;
            decimal unitCost;
            var basis = isFifo ? "FIFO" : "Weighted Average";

            if (!isFifo)
            {
                unitCost = wac;
            }
            else
            {
                // Consume the oldest layers by the stock that has already left the building
                var shipped = purchasedQty - onHand;
                if (shipped < 0) shipped = 0;

                var remainingLayers = new List<(decimal Qty, decimal Rate)>();
                var toShip = shipped;
                foreach (var layer in grp)
                {
                    var qty = (decimal)layer.Quantity;
                    if (toShip >= qty)
                    {
                        toShip -= qty;
                        continue;
                    }
                    remainingLayers.Add((qty - toShip, layer.Rate));
                    toShip = 0;
                }

                var layeredQty = remainingLayers.Sum(l => l.Qty);
                var layeredValue = remainingLayers.Sum(l => l.Qty * l.Rate);

                // On-hand with no cost layer (opening stock / returns) → weighted average fallback
                var unlayered = onHand - layeredQty;
                if (unlayered > 0m)
                {
                    layeredQty += unlayered;
                    layeredValue += unlayered * wac;
                    basis = "Mixed+Fallback";
                }

                unitCost = layeredQty > 0m ? layeredValue / layeredQty : wac;
            }

            result.Add(new StockValuationRow
            {
                ProductId = product.Id,
                ProductName = product.Name,
                SKU = product.SKU ?? string.Empty,
                StockQty = onHand,
                UnitCost = System.Math.Round(unitCost, 2),
                Basis = basis
            });
        }

        // Products never purchased: no cost basis at all → value at zero cost, flagged
        foreach (var product in products.Where(p => !result.Any(r => r.ProductId == p.Id) && p.StockQty > 0))
        {
            result.Add(new StockValuationRow
            {
                ProductId = product.Id,
                ProductName = product.Name,
                SKU = product.SKU ?? string.Empty,
                StockQty = product.StockQty,
                UnitCost = 0m,
                Basis = "No Purchases"
            });
        }

        return result.OrderBy(r => r.ProductName).ToList();
    }

    /// <summary>
    /// PAY-07: customer ledger. Invoices (non-estimate) are debits; receipts paid to the
    /// customer's account and credit notes are credits; running receivable balance.
    /// </summary>
    public async Task<CustomerLedgerReport> GetCustomerLedgerAsync(int customerId, System.DateTime startDate, System.DateTime endDate)
    {
        var customer = await _dbContext.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId);
        var endExclusive = endDate.Date.AddDays(1);
        var report = new CustomerLedgerReport
        {
            CustomerId = customerId,
            CustomerName = customer?.Name ?? "Unknown",
            StartDate = startDate,
            EndDate = endDate
        };

        // Opening balance: everything before the start date
        var openInvoices = await _dbContext.Invoices
            .Where(i => i.CustomerId == customerId && !i.IsEstimate && i.InvoiceDate < startDate.Date)
            .Select(i => new { i.TotalAmount, i.PaidAmount, i.WriteOffAmount })
            .ToListAsync();
        var openReceipts = await _dbContext.Payments
            .Where(p => p.PartyType == "Customer" && p.PartyId == customerId
                        && p.Direction == "In" && p.PaymentDate < startDate.Date)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;
        var openCreditNotes = await _dbContext.CreditNotes
            .Where(c => c.CustomerId == customerId && c.Type == "Credit" && c.Status != "Cancelled"
                        && c.CreditNoteDate < startDate.Date)
            .SumAsync(c => (decimal?)c.TotalAmount) ?? 0m;

        report.OpeningBalance = openInvoices.Sum(i => i.TotalAmount - i.PaidAmount - i.WriteOffAmount)
                                - openReceipts - openCreditNotes;

        var running = report.OpeningBalance;

        // In-period rows: merge three sources chronologically
        var invoices = await _dbContext.Invoices
            .Where(i => i.CustomerId == customerId && !i.IsEstimate
                        && i.InvoiceDate >= startDate.Date && i.InvoiceDate < endExclusive)
            .Select(i => new { i.InvoiceNumber, Date = i.InvoiceDate, i.TotalAmount, i.Notes })
            .ToListAsync();

        var receipts = await _dbContext.Payments
            .Include(p => p.Allocations)
            .Where(p => p.PartyType == "Customer" && p.PartyId == customerId
                        && p.Direction == "In" && p.PaymentDate >= startDate.Date && p.PaymentDate < endExclusive)
            .ToListAsync();

        var creditNotes = await _dbContext.CreditNotes
            .Where(c => c.CustomerId == customerId && c.Type == "Credit" && c.Status != "Cancelled"
                        && c.CreditNoteDate >= startDate.Date && c.CreditNoteDate < endExclusive)
            .Select(c => new { c.CreditNoteNumber, c.CreditNoteDate, c.TotalAmount, c.Reason })
            .ToListAsync();

        var rows = new List<(DateTime Date, string Type, string Ref, string? Narration, decimal Debit, decimal Credit)>();
        rows.AddRange(invoices.Select(i => (i.Date.Date, "Invoice", i.InvoiceNumber, i.Notes, i.TotalAmount, 0m)));
        rows.AddRange(receipts.Select(p => (p.PaymentDate.Date,
            "Receipt In",
            p.PaymentNumber,
            p.Allocations.Count > 0
                ? string.Join(", ", p.Allocations.Select(a => a.InvoiceNumber ?? "(Advance)"))
                : p.Reference,
            0m, p.Amount)));
        rows.AddRange(creditNotes.Select(c => (c.CreditNoteDate.Date, "Credit Note", c.CreditNoteNumber, c.Reason, 0m, c.TotalAmount)));

        foreach (var row in rows.OrderBy(r => r.Date))
        {
            running += row.Item5 - row.Item6;
            report.Rows.Add(new CustomerLedgerRow
            {
                Date = row.Date,
                Type = row.Type,
                Reference = row.Ref,
                Narration = row.Narration,
                Debit = row.Item5,
                Credit = row.Item6,
                RunningBalance = running
            });
        }

        report.ClosingBalance = running;
        return report;
    }

    /// <summary>PAY-07: formatted customer statement written to Desktop.</summary>
    public async Task<string?> PrintCustomerStatementAsync(int customerId, System.DateTime startDate, System.DateTime endDate)
    {
        var report = await GetCustomerLedgerAsync(customerId, startDate, endDate);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("==========================================");
        sb.AppendLine("        CUSTOMER STATEMENT OF ACCOUNT");
        sb.AppendLine("==========================================");
        sb.AppendLine($"Customer : {report.CustomerName}");
        sb.AppendLine($"Period   : {report.StartDate:dd-MMM-yyyy} to {report.EndDate:dd-MMM-yyyy}");
        sb.AppendLine($"Generated: {System.DateTime.Now:dd-MMM-yyyy HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"Opening Balance: {report.OpeningBalance:C}");
        sb.AppendLine();
        sb.AppendLine($"{"Date",-12}{"Type",-13}{"Reference",-20}{"Debit",12}{"Credit",12}{"Balance",12}");
        sb.AppendLine(new string('-', 81));
        foreach (var row in report.Rows)
            sb.AppendLine($"{row.Date:dd-MMM-yyyy}".PadRight(12)
                          + row.Type.PadRight(13)
                          + row.Reference.PadRight(20)
                          + row.Debit.ToString("N2").PadLeft(12)
                          + row.Credit.ToString("N2").PadLeft(12)
                          + row.RunningBalance.ToString("N2").PadLeft(12));
        sb.AppendLine(new string('-', 81));
        sb.AppendLine($"Closing Balance: {report.ClosingBalance:C}");
        sb.AppendLine();
        sb.AppendLine("An amount due in Balance is payable within the agreed credit period.");

        var safeName = string.Join("_", report.CustomerName.Split(Path.GetInvalidFileNameChars()));
        var path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            $"Statement_{safeName}_{report.StartDate:yyyyMMdd}_{report.EndDate:yyyyMMdd}.txt");
        await System.IO.File.WriteAllTextAsync(path, sb.ToString());

        return path;
    }

    public async Task<DayBookReport> GetDayBookAsync(System.DateTime startDate, System.DateTime endDate)
    {
        var endExclusive = endDate.Date.AddDays(1);

        var entries = await _dbContext.JournalEntries
            .Include(e => e.Lines)
            .ThenInclude(l => l.Account)
            .Where(e => e.EntryDate >= startDate.Date && e.EntryDate < endExclusive)
            .OrderBy(e => e.EntryDate)
            .ToListAsync();

        var report = new DayBookReport { StartDate = startDate, EndDate = endDate };

        foreach (var entry in entries)
        {
            report.Entries.Add(new DayBookEntry
            {
                EntryDate = entry.EntryDate,
                Narration = entry.Narration,
                ReferenceType = entry.ReferenceType,
                ReferenceId = entry.ReferenceId,
                Lines = entry.Lines.Select(l => new DayBookLine
                {
                    AccountName = l.Account?.Name ?? "Unknown",
                    Debit = l.Debit,
                    Credit = l.Credit,
                    Narration = l.Narration
                }).ToList()
            });
        }

        return report;
    }

    public async Task<LedgerReport> GetLedgerAsync(int accountId, System.DateTime startDate, System.DateTime endDate)
    {
        var endExclusive = endDate.Date.AddDays(1);

        var account = await _dbContext.Accounts.FindAsync(accountId);
        var lines = await _dbContext.JournalLines
            .Include(l => l.JournalEntry)
            .Include(l => l.Account)
            .Where(l => l.AccountId == accountId
                        && l.JournalEntry!.EntryDate >= startDate.Date
                        && l.JournalEntry.EntryDate < endExclusive)
            .OrderBy(l => l.JournalEntry!.EntryDate).ThenBy(l => l.Id)
            .ToListAsync();

        var openingLines = await _dbContext.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.AccountId == accountId && l.JournalEntry!.EntryDate < startDate.Date)
            .ToListAsync();

        // Credit-normal accounts display inverted running balance
        var sign = account != null
            && (account.AccountType is "Liability" or "Equity" or "Income")
            ? -1m : 1m;

        var report = new LedgerReport
        {
            AccountName = account?.Name ?? "Unknown Account",
            StartDate = startDate,
            EndDate = endDate,
            OpeningBalance = sign * (openingLines.Sum(l => l.Debit) - openingLines.Sum(l => l.Credit))
            };

        var running = report.OpeningBalance;
        foreach (var l in lines)
        {
            running += sign * (l.Debit - l.Credit);
            report.Rows.Add(new LedgerRow
            {
                Date = l.JournalEntry!.EntryDate,
                ReferenceType = l.JournalEntry.ReferenceType,
                ReferenceId = l.JournalEntry.ReferenceId,
                Narration = l.Narration ?? l.JournalEntry.Narration,
                Debit = l.Debit,
                Credit = l.Credit,
                RunningBalance = running
            });
        }
        report.ClosingBalance = running;

        return report;
    }

    public async Task<BalanceSheetReport> GetBalanceSheetAsync(System.DateTime asOfDate)
    {
        var endOfDay = asOfDate.Date.AddDays(1);
        var lines = await _dbContext.JournalLines
            .Include(l => l.Account)
            .Include(l => l.JournalEntry)
            .Where(l => l.JournalEntry!.EntryDate < endOfDay)
            .ToListAsync();

        var report = new BalanceSheetReport { AsOfDate = asOfDate };
        var incomeNetCredit = 0m; // credit - debit on income accounts
        var expenseNetDebit = 0m; // debit - credit on expense accounts

        foreach (var group in lines.Where(l => l.Account != null).GroupBy(l => l.AccountId))
        {
            var account = group.First().Account!;
            var debit = group.Sum(l => l.Debit);
            var credit = group.Sum(l => l.Credit);
            var openingSign = account.OpeningBalance >= 0 ? account.OpeningBalance : 0m;

            var ab = new AccountBalance
            {
                AccountId = account.Id,
                AccountName = account.Name,
                AccountType = account.AccountType,
                DebitTotal = debit + openingSign,
                CreditTotal = credit
            };

            switch (account.AccountType)
            {
                case "Asset":
                    ab.Balance = ab.DebitTotal - ab.CreditTotal;
                    report.Assets.Add(ab);
                    break;
                case "Liability":
                    ab.Balance = ab.CreditTotal - ab.DebitTotal;
                    report.Liabilities.Add(ab);
                    break;
                case "Equity":
                    ab.Balance = ab.CreditTotal - ab.DebitTotal;
                    report.Equity.Add(ab);
                    break;
                case "Income":
                    incomeNetCredit += credit - debit;
                    break;
                case "Expense":
                    expenseNetDebit += debit - credit;
                    break;
            }
        }

        report.RetainedEarnings = incomeNetCredit - expenseNetDebit;

        report.Assets = report.Assets.OrderByDescending(a => a.Balance).ToList();
        report.Liabilities = report.Liabilities.OrderByDescending(a => a.Balance).ToList();
        report.Equity = report.Equity.OrderByDescending(a => a.Balance).ToList();

        return report;
    }

    /// <summary>
    /// RPT-10: multi-store comparison - sales (excl. tax), COGS via latest
    /// purchase rate, gross profit, margin, sales share, outstanding and rank.
    /// Estimates excluded; stores with no sales are listed ranked 0.
    /// </summary>
    public async Task<List<StoreComparisonRow>> GetStoreComparisonAsync(System.DateTime startDate, System.DateTime endDate)
    {
        var stores = await _dbContext.Stores.AsNoTracking().ToListAsync();

        var invoices = await _dbContext.Invoices
            .Include(i => i.Items)
            .Where(i => !i.IsEstimate && i.InvoiceDate >= startDate.Date && i.InvoiceDate <= endDate.Date)
            .ToListAsync();

        // COGS basis: latest purchase rate per product (same convention as P&L)
        var purchaseRates = await _dbContext.PurchaseItems
            .Where(pi => pi.ProductId != null)
            .Select(pi => new { pi.ProductId, pi.Rate })
            .ToListAsync();
        var costMap = purchaseRates
            .GroupBy(x => x.ProductId!.Value)
            .ToDictionary(g => g.Key, g => g.Last().Rate);

        var result = new List<StoreComparisonRow>();

        // Group invoices by store (null StoreId = unassigned/main store)
        foreach (var group in invoices.GroupBy(i => i.StoreId))
        {
            var store = stores.FirstOrDefault(s => s.Id == group.Key);
            var sales = group.Sum(i => i.SubTotal);
            var cogs = group.SelectMany(i => i.Items)
                .Where(it => it.ProductId.HasValue && costMap.ContainsKey(it.ProductId.Value))
                .Sum(it => costMap[it.ProductId.Value] * it.Quantity);

            result.Add(new StoreComparisonRow
            {
                StoreId = group.Key,
                StoreName = store?.Name ?? "Main Store (unassigned)",
                Location = store?.Location ?? string.Empty,
                InvoiceCount = group.Count(),
                Sales = sales,
                COGS = System.Math.Round(cogs, 2),
                GrossProfit = System.Math.Round(sales - cogs, 2),
                Outstanding = group.Sum(i => i.TotalAmount - i.PaidAmount)
            });
        }

        // Stores with no sales still appear for completeness
        foreach (var store in stores.Where(s => !result.Any(r => r.StoreId == s.Id)))
        {
            result.Add(new StoreComparisonRow
            {
                StoreId = store.Id,
                StoreName = store.Name,
                Location = store.Location ?? string.Empty,
                InvoiceCount = 0
            });
        }

        var totalSales = result.Sum(r => r.Sales);
        var rank = 1;
        foreach (var row in result.OrderByDescending(r => r.Sales))
        {
            row.SalesSharePercent = totalSales > 0m
                ? System.Math.Round(row.Sales * 100m / totalSales, 1)
                : 0m;
            if (row.Sales > 0m) row.Rank = rank++;
        }

        return result
            .OrderByDescending(r => r.Sales)
            .ThenBy(r => r.StoreName)
            .ToList();
    }

    public async Task<ProfitLossReport> GetProfitLossReportAsync(System.DateTime startDate, System.DateTime endDate)
    {
        var invoices = await _dbContext.Invoices
            .Include(i => i.Items)
            .Where(i => i.InvoiceDate >= startDate && i.InvoiceDate <= endDate)
            .ToListAsync();

        // Revenue excluding tax = SubTotal per invoice
        var revenue = invoices.Sum(i => i.SubTotal);

        // COGS via latest purchase rate per product (fallback: no cost recorded → 0)
        var purchaseRates = await _dbContext.PurchaseItems
            .Where(pi => pi.ProductId != null)
            .Select(pi => new { pi.ProductId, pi.Rate })
            .ToListAsync();
        var costMap = purchaseRates
            .GroupBy(x => x.ProductId!.Value)
            .ToDictionary(g => g.Key, g => g.Last().Rate);

        var cogs = 0m;
        foreach (var invoice in invoices)
            foreach (var item in invoice.Items)
                if (item.ProductId.HasValue
                    && costMap.TryGetValue(item.ProductId.Value, out var cost))
                {
                    cogs += cost * item.Quantity;
                }

        var expenseRows = await _dbContext.Expenses
            .Where(e => e.ExpenseDate >= startDate && e.ExpenseDate <= endDate)
            .Select(e => new { e.Category, e.Amount, e.ExpenseDate })
            .ToListAsync();

        var expenses = expenseRows.Sum(e => e.Amount);

        // Monthly summaries
        var monthly = invoices
            .GroupBy(i => new { i.InvoiceDate.Year, i.InvoiceDate.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Revenue = g.Sum(i => i.SubTotal),
                COGS = g.SelectMany(i => i.Items).Sum(it =>
                    it.ProductId.HasValue && costMap.TryGetValue(it.ProductId.Value, out var c)
                        ? c * it.Quantity : 0m)
            })
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToList();

        var monthlySummaries = new List<MonthlySummary>();
        foreach (var m in monthly)
        {
            var monthExpenses = expenseRows
                .Where(e => e.ExpenseDate.Year == m.Year && e.ExpenseDate.Month == m.Month)
                .Sum(e => e.Amount);
            monthlySummaries.Add(new MonthlySummary
            {
                Month = $"{m.Year}-{m.Month:00}",
                Revenue = m.Revenue,
                COGS = m.COGS,
                Expenses = monthExpenses
            });
        }

        var breakdown = expenseRows
            .GroupBy(e => e.Category)
            .Select(g => new CategorySummary { Category = g.Key, Amount = g.Sum(e => e.Amount) })
            .OrderByDescending(x => x.Amount)
            .ToList();

        return new ProfitLossReport
        {
            TotalRevenue = revenue,
            TotalCOGS = cogs,
            TotalExpenses = expenses,
            MonthlySummaries = monthlySummaries,
            ExpenseBreakdown = breakdown
        };
    }

    public Task<List<StockAudit>> GetStockAuditAsync() =>
        _dbContext.Products.Select(p => new StockAudit
        {
            ProductId = p.Id,
            ProductName = p.Name,
            StockQty = p.StockQty,
            MinStockQty = p.MinStockQty,
            Batch = p.Batch,
            ExpiryDate = p.ExpiryDate
        }).ToListAsync();

    public async Task<StockAuditReport> GetStockAuditReportAsync(System.DateTime startDate, System.DateTime endDate)
    {
        // RPT-07: audit the book stock against the stock ledger and the last physical count.
        // Book qty is the master figure; the ledger is every movement we were told about. The
        // difference is stock that exists without a movement (opening typed on the master, or a
        // document that never posted) - which is exactly what a stock audit is meant to catch.
        var from = startDate.Date;
        var to = endDate.Date;
        var today = System.DateTime.Today;

        var products = await _dbContext.Products
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.SKU,
                p.Category,
                p.StockQty,
                p.MinStockQty,
                p.Rate,
                p.ExpiryDate
            })
            .ToListAsync();

        var movements = await _dbContext.StockTransactions
            .Select(t => new { t.ProductId, t.TransactionDate, t.QuantityChange })
            .ToListAsync();

        var movementsByProduct = movements
            .GroupBy(m => m.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var noMovements = movements.Where(_ => false).ToList();

        // Latest completed count per product - an open stocktake is not evidence yet.
        var countDates = await _dbContext.StockTakes
            .Where(t => t.Status == "Completed")
            .Select(t => new { t.Id, t.StockTakeDate })
            .ToListAsync();
        var completedTakeIds = countDates.ToDictionary(t => t.Id, t => t.StockTakeDate);

        var countedItems = await _dbContext.StockTakeItems
            .Where(i => i.CountedQty != null)
            .Select(i => new { i.ProductId, i.CountedQty, i.StockTakeId })
            .ToListAsync();

        var lastCountByProduct = new Dictionary<int, (int CountedQty, System.DateTime CountDate)>();
        foreach (var item in countedItems)
        {
            if (!completedTakeIds.TryGetValue(item.StockTakeId, out var countDate)) continue;
            if (countDate > to) continue;
            if (lastCountByProduct.TryGetValue(item.ProductId, out var existing) && existing.CountDate >= countDate) continue;
            lastCountByProduct[item.ProductId] = (item.CountedQty!.Value, countDate);
        }

        var rows = new List<StockAuditRow>();

        foreach (var p in products)
        {
            var bookQty = (decimal)p.StockQty;
            var productMoves = movementsByProduct.TryGetValue(p.Id, out var m) ? m : noMovements;

            var row = new StockAuditRow
            {
                ProductId = p.Id,
                ProductName = p.Name,
                SKU = p.SKU ?? string.Empty,
                Category = p.Category ?? string.Empty,
                BookQty = bookQty,
                MinStockQty = p.MinStockQty,
                ExpiryDate = p.ExpiryDate,
                Rate = p.Rate,
                StockValue = bookQty * p.Rate
            };

            var afterPeriod = productMoves.Where(x => x.TransactionDate > to).Sum(x => x.QuantityChange);
            var inPeriodMoves = productMoves.Where(x => x.TransactionDate >= from && x.TransactionDate <= to).ToList();
            var inPeriod = inPeriodMoves.Sum(x => x.QuantityChange);

            row.ClosingQty = bookQty - afterPeriod;
            row.OpeningQty = row.ClosingQty - inPeriod;
            row.QtyIn = inPeriodMoves.Where(x => x.QuantityChange > 0).Sum(x => x.QuantityChange);
            row.QtyOut = -inPeriodMoves.Where(x => x.QuantityChange < 0).Sum(x => x.QuantityChange);
            row.MovementCountInPeriod = inPeriodMoves.Count;
            row.LastMovementDate = productMoves.Count > 0 ? productMoves.Max(x => x.TransactionDate) : null;

            row.LedgerQty = productMoves.Sum(x => x.QuantityChange);
            row.LedgerVariance = bookQty - row.LedgerQty;
            row.VarianceValue = row.LedgerVariance * p.Rate;

            if (lastCountByProduct.TryGetValue(p.Id, out var count))
            {
                row.LastCountedQty = count.CountedQty;
                row.LastCountDate = count.CountDate;
                row.CountVariance = bookQty - count.CountedQty;

                // The count only still explains the book if nothing moved afterwards -
                // otherwise the movement is the reason for the difference.
                var movedSinceCount = productMoves.Any(x => x.TransactionDate > count.CountDate);
                row.HasUnpostedCount = !movedSinceCount && System.Math.Abs(row.CountVariance) > 0.001m;
            }

            if (row.BookQty < 0) row.Exceptions.Add("Negative Stock");
            if (System.Math.Abs(row.LedgerVariance) > 0.001m) row.Exceptions.Add("Unrecorded Stock");
            if (row.HasUnpostedCount) row.Exceptions.Add("Unposted Count");
            if (row.ExpiryDate.HasValue && row.ExpiryDate.Value < today) row.Exceptions.Add("Expired Batch");
            if (p.MinStockQty > 0 && row.BookQty <= p.MinStockQty) row.Exceptions.Add("Below Minimum");
            if (row.MovementCountInPeriod == 0 && row.BookQty != 0) row.Exceptions.Add("No Movement");

            rows.Add(row);
        }

        var result = rows.OrderByDescending(r => System.Math.Abs(r.VarianceValue)).ThenBy(r => r.ProductName).ToList();

        return new StockAuditReport
        {
            Rows = result,
            FromDate = from,
            ToDate = to,
            ProductCount = result.Count,
            TotalStockValue = result.Sum(r => r.StockValue),
            MismatchCount = result.Count(r => r.Exceptions.Contains("Unrecorded Stock")),
            MismatchValue = result.Sum(r => r.VarianceValue),
            NegativeStockCount = result.Count(r => r.Exceptions.Contains("Negative Stock")),
            BelowMinimumCount = result.Count(r => r.Exceptions.Contains("Below Minimum")),
            ExpiredCount = result.Count(r => r.Exceptions.Contains("Expired Batch")),
            NoMovementCount = result.Count(r => r.Exceptions.Contains("No Movement")),
            NoMovementValue = result.Where(r => r.Exceptions.Contains("No Movement")).Sum(r => r.StockValue),
            UnpostedCountCount = result.Count(r => r.HasUnpostedCount),
            CleanCount = result.Count(r => !r.HasException),
            LastCountDate = lastCountByProduct.Count > 0 ? lastCountByProduct.Values.Max(c => c.CountDate) : null
        };
    }

    public async Task<List<Receivable>> GetReceivablesAsync()
    {
        // ACC-05: customer receivables from unpaid invoices, with ageing buckets.
        // Estimates are excluded - they are not yet enforceable receivables.
        var invoices = await _dbContext.Invoices
            .Where(i => i.Status != "Paid" && i.TotalAmount - i.PaidAmount - i.WriteOffAmount > 0 && !i.IsEstimate)
            .Select(i => new { i.CustomerId, i.CustomerName, i.TotalAmount, i.PaidAmount, i.WriteOffAmount, i.InvoiceDate })
            .ToListAsync();

        var today = System.DateTime.Today;
        var receivables = new List<Receivable>();

        foreach (var g in invoices.GroupBy(i => new { i.CustomerId, i.CustomerName }))
        {
            var receivable = new Receivable
            {
                CustomerId = g.Key.CustomerId ?? 0,
                CustomerName = g.Key.CustomerName ?? "Unknown",
                OutstandingAmount = g.Sum(i => i.TotalAmount - i.PaidAmount - i.WriteOffAmount),
                PendingInvoices = g.Count(),
                OldestInvoiceDate = g.Min(i => i.InvoiceDate)
            };

            foreach (var i in g)
            {
                var outstanding = i.TotalAmount - i.PaidAmount - i.WriteOffAmount;
                var ageDays = (today - i.InvoiceDate.Date).Days;
                if (ageDays <= 30) receivable.Current += outstanding;
                else if (ageDays <= 60) receivable.Days31To60 += outstanding;
                else if (ageDays <= 90) receivable.Days61To90 += outstanding;
                else receivable.Days90Plus += outstanding;
            }

            receivables.Add(receivable);
        }

        return receivables.OrderByDescending(r => r.OutstandingAmount).ToList();
    }

    public async Task<List<Payable>> GetPayablesAsync()
    {
        // RPT-09: real supplier payables from unpaid purchase bills, with ageing buckets
        var purchases = await _dbContext.Purchases
            .Where(p => p.Status != "Paid" && p.TotalAmount - p.PaidAmount > 0)
            .Select(p => new { p.SupplierId, p.SupplierName, p.TotalAmount, p.PaidAmount, p.PurchaseDate })
            .ToListAsync();

        var today = System.DateTime.Today;
        var payables = new List<Payable>();

        foreach (var g in purchases
                     .GroupBy(p => new { p.SupplierId, p.SupplierName }))
        {
            var payable = new Payable
            {
                Id = g.Key.SupplierId ?? 0,
                SupplierName = g.Key.SupplierName ?? "Unknown Supplier",
                PendingBills = g.Count(),
                Amount = g.Sum(p => p.TotalAmount - p.PaidAmount),
                DueDate = g.Min(p => p.PurchaseDate)
            };

            foreach (var p in g)
            {
                var outstanding = p.TotalAmount - p.PaidAmount;
                var ageDays = (today - p.PurchaseDate.Date).Days;
                if (ageDays <= 30) payable.Current += outstanding;
                else if (ageDays <= 60) payable.Days31To60 += outstanding;
                else if (ageDays <= 90) payable.Days61To90 += outstanding;
                else payable.Days90Plus += outstanding;
            }

            payables.Add(payable);
        }

        return payables.OrderByDescending(p => p.Amount).ToList();
    }

    public async Task<GstReport> GetGstReportAsync(System.DateTime startDate, System.DateTime endDate)
    {
        var endExclusive = endDate.Date.AddDays(1);

        // Estimates are not taxable supplies; Bill of Supply invoices carry no tax
        var invoices = await _dbContext.Invoices
            .Include(i => i.Items)
            .Where(i => i.InvoiceDate >= startDate.Date && i.InvoiceDate < endExclusive && !i.IsEstimate)
            .ToListAsync();

        // CDNR: non-cancelled credit notes issued in the period
        var creditNotes = await _dbContext.CreditNotes
            .Where(c => c.CreditNoteDate >= startDate.Date && c.CreditNoteDate < endExclusive
                        && c.Status != "Cancelled" && c.Type == "Credit")
            .ToListAsync();

        // ITC source: purchase bills in the period
        var purchases = await _dbContext.Purchases
            .Where(p => p.PurchaseDate >= startDate.Date && p.PurchaseDate < endExclusive)
            .ToListAsync();

        var report = new GstReport { StartDate = startDate, EndDate = endDate };

        foreach (var invoice in invoices)
        {
            var taxable = invoice.SubTotal;
            if (invoice.CustomerGSTIN is { Length: > 0 })
            {
                var b2b = new GstB2bInvoice
                {
                    InvoiceNumber = invoice.InvoiceNumber,
                    InvoiceDate = invoice.InvoiceDate,
                    CustomerName = invoice.CustomerName ?? string.Empty,
                    CustomerGstin = invoice.CustomerGSTIN,
                    TaxableValue = taxable,
                    InvoiceValue = invoice.TotalAmount,
                    TaxValue = invoice.TaxAmount,
                    Cgst = invoice.CGST,
                    Sgst = invoice.SGST,
                    Igst = invoice.IGST,
                    PlaceOfSupply = invoice.PlaceOfSupply ?? string.Empty,
                    InvoiceType = invoice.IsBillOfSupply ? "Bill of Supply" : "Regular"
                };
                report.B2b.Add(b2b);
            }
            else
            {
                report.B2c.Add(new GstB2cInvoice
                {
                    InvoiceNumber = invoice.InvoiceNumber,
                    InvoiceDate = invoice.InvoiceDate,
                    CustomerName = invoice.CustomerName ?? string.Empty,
                    TaxableValue = taxable,
                    InvoiceValue = invoice.TotalAmount,
                    TaxValue = invoice.TaxAmount,
                    Cgst = invoice.CGST,
                    Sgst = invoice.SGST,
                    Igst = invoice.IGST,
                    PlaceOfSupply = invoice.PlaceOfSupply ?? string.Empty,
                    IsBillOfSupply = invoice.IsBillOfSupply
                });
            }

            // HSN-level grouping (Table 12)
            foreach (var item in invoice.Items)
            {
                var hsn = item.HSNCode ?? string.Empty;
                var hsnRow = report.HsnSummary.FirstOrDefault(h => h.HsnCode == hsn);
                if (hsnRow == null)
                {
                    hsnRow = new GstHsnSummary { HsnCode = hsn, Description = item.ProductName };
                    report.HsnSummary.Add(hsnRow);
                }
                hsnRow.Uqc += item.Quantity;
                hsnRow.TaxableValue += item.Amount;
                hsnRow.TotalTax += item.TaxAmount;
                hsnRow.Cgst += item.CGST;
                hsnRow.Sgst += item.SGST;
                hsnRow.Igst += item.IGST;
                hsnRow.TotalValue += item.Total;
            }

            // GSTR-3B Table 3.1(a) outward taxable supplies
            report.OutwardTaxableValue += taxable;
            report.OutwardCGST += invoice.CGST;
            report.OutwardSGST += invoice.SGST;
            report.OutwardIGST += invoice.IGST;
            report.OutwardCess += invoice.Cess;
        }

        foreach (var cn in creditNotes)
        {
            var original = cn.OriginalInvoiceId.HasValue
                ? invoices.FirstOrDefault(i => i.Id == cn.OriginalInvoiceId.Value)
                : null;

            report.CreditNotes.Add(new GstCreditNote
            {
                CreditNoteNumber = cn.CreditNoteNumber,
                CreditNoteDate = cn.CreditNoteDate,
                OriginalInvoiceNumber = cn.OriginalInvoiceNumber ?? original?.InvoiceNumber ?? string.Empty,
                CustomerName = cn.CustomerName ?? string.Empty,
                CustomerGstin = original?.CustomerGSTIN ?? string.Empty,
                TaxableValue = cn.SubTotal,
                CreditNoteValue = cn.TotalAmount,
                TaxValue = cn.TaxAmount,
                Type = cn.Type,
                Reason = cn.Reason
            });
        }

        report.TotalSales = report.OutwardTaxableValue;
        report.TotalTax = report.OutwardCGST + report.OutwardSGST + report.OutwardIGST + report.OutwardCess;
        report.InvoiceCount = invoices.Count;
        report.InputTaxCredit = purchases.Sum(p => p.TaxAmount);

        return report;
    }

    /// <summary>RPT-03: serialise GSTR-1 (B2B/B2C/CDNR/HSN) to the GSTO portal JSON shape and save to Desktop.</summary>
    public async Task<string> ExportGstr1JsonAsync(DateTime startDate, DateTime endDate)
    {
        var report = await GetGstReportAsync(startDate, endDate);

        var payload = new
        {
            version = "1.0",
            gstin = "29ABCDE1234F1Z5",
            fp = $"{startDate:yyyy-MM}",
            @from = startDate.ToString("yyyy-MM-dd"),
            to = endDate.ToString("yyyy-MM-dd"),
            b2b = report.B2b.Select(i => new
            {
                inv = i.InvoiceNumber,
                idt = i.InvoiceDate.ToString("yyyy-MM-dd"),
                val = i.InvoiceValue,
                pos = i.PlaceOfSupply,
                rchrg = i.ReverseCharge,
                inv_typ = i.InvoiceType,
                cts = i.CustomerGstin,
                itms = new[] { new { num = 1, itm_det = new { txval = i.TaxableValue, rt = 0, camt = i.Cgst, samt = i.Sgst, iamt = i.Igst } } }
            }),
            b2c = report.B2c.Select(i => new
            {
                inv = i.InvoiceNumber,
                idt = i.InvoiceDate.ToString("yyyy-MM-dd"),
                val = i.InvoiceValue,
                pos = i.PlaceOfSupply,
                bos = i.IsBillOfSupply ? "Y" : "N",
                itms = new[] { new { num = 1, itm_det = new { txval = i.TaxableValue, camt = i.Cgst, samt = i.Sgst, iamt = i.Igst } } }
            }),
            cdnr = report.CreditNotes.Select(c => new
            {
                nt_num = c.CreditNoteNumber,
                nt_dt = c.CreditNoteDate.ToString("yyyy-MM-dd"),
                oin_num = c.OriginalInvoiceNumber,
                val = c.CreditNoteValue,
                cts = c.CustomerGstin,
                reason = c.Reason,
                itms = new[] { new { num = 1, itm_det = new { txval = c.TaxableValue, camt = 0m, samt = 0m } } }
            }),
            hsn = report.HsnSummary.Select(h => new
            {
                hsn_sc = h.HsnCode,
                desc = h.Description,
                uqc = h.Uqc,
                txval = h.TaxableValue,
                camt = h.Cgst,
                samt = h.Sgst,
                iamt = h.Igst
            })
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        var path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            $"GSTR1_{startDate:yyyyMM}_{endDate:yyyyMM}.json");
        await System.IO.File.WriteAllTextAsync(path, json);

        return json;
    }

    /// <summary>RPT-03: serialise the GSTR-3B summary to JSON and save to Desktop.</summary>
    public async Task<string> ExportGstr3bJsonAsync(DateTime startDate, DateTime endDate)
    {
        var report = await GetGstReportAsync(startDate, endDate);

        var payload = new
        {
            version = "1.0",
            gstin = "29ABCDE1234F1Z5",
            fp = $"{startDate:yyyy-MM}",
            table3_1_outward = new
            {
                taxable_value = report.OutwardTaxableValue,
                cgst = report.OutwardCGST,
                sgst = report.OutwardSGST,
                igst = report.OutwardIGST,
                cess = report.OutwardCess
            },
            table4_itc = new { total = report.InputTaxCredit },
            table6_1_payment = new
            {
                total_tax_charged = report.TotalTax,
                net_tax_payable = report.NetTaxPayable
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        var path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            $"GSTR3B_{startDate:yyyyMM}_{endDate:yyyyMM}.json");
        await System.IO.File.WriteAllTextAsync(path, json);

        return json;
    }
}
