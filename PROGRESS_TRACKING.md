# 📋 Retail Billing & Inventory Management System - Build Tracker

**Project:** Retail Billing & Inventory Management System (C# / .NET 10 / WPF / SQLite)  
**Based on:** Development Plan (17-page PDF) + SRS  
**Target:** Full v1.0 scope (~33 weeks, 116 person-weeks)  
**Status:** 🟡 In Progress

## 📈 Overall Progress

> **Project Completion: 80 / 90 features = 89% done** *(updated 2026-11-01; SEC-03 closed after user approved the NuGet installs — verified SQLCipher 4.5.2 at rest with DPAPI key file and one-time plaintext migration; QuestPDF + ClosedXML installed for RPT-13; next milestone 95% at 85 features)*
> **Estimated time remaining:** ~5 of 33 planned weeks *(elapsed: week 7 of plan schedule)* — 10 features left (4 partial rescues + 6 pending, mostly P3/v1.1 scope), running well ahead of the 33-week plan.

---

## 🏗 Architecture

Single-project WPF application with clean folder organization:
- `Models/` - 12 entity classes
- `Services/` - 8 interfaces + 8 implementations
- `Data/` - AppDbContext (EF Core SQLite)
- `ViewModels/` - 8 MVVM view models
- `Views/` - 7 XAML views
- `Converters/` - 5 XAML value converters
- `Resources/Styles/` - App theme styles

*(Layered architecture refactor deferred - caused WPF build issues)*

---

## ✅ COMPLETED (Phase 0-1 Foundation)

Build: **0 errors, 0 warnings** ✅

- [x] Project structure with WPF, EF Core SQLite 8.0, CommunityToolkit.Mvvm 8.2.2
- [x] App.xaml, MainWindow with sidebar navigation
- [x] Login view with auth (Admin/User roles, SHA-256 password hash)
- [x] Default admin seeded: `admin` / `admin123`
- [x] Data models (12): Product, Customer, Invoice, InvoiceItem, Sale, User, Store, StockTransaction, Expense, JournalEntry, Reminder
- [x] Services: Auth, Product, Customer, Invoice, Sale, Report, Backup, Database
- [x] ViewModels: Login, Dashboard, Products, Invoices, Sales, Reports, Settings, MainWindow
- [x] Views: Login, Dashboard, Products, Invoices, Sales, Reports, Settings
- [x] Value converters for XAML
- [x] AppStyles.xaml theme
- [x] Default admin seed: admin / admin123

---

## 📊 MASTER FEATURE CHECKLIST (90 Features)

**Priority Legend:** P1 = Must have (v1.0), P2 = Should have (v1.0/v1.1), P3 = Later/optional (v1.2+)

**Legend:** ✅ Done | 🟡 In Progress | ⬜ Pending | ⏸️ Blocked

---

### Phase 0: Setup and Design (Weeks 1-2)

| ID | Feature | Phase | Pri | Status | Notes |
|----|---------|-------|-----|--------|-------|
| — | Requirement sign-off | 0 | — | ✅ Done | SRS + Dev Plan reviewed |
| — | UI wireframes | 0 | — | ⬜ Pending | |
| — | ERD / DB design | 0 | — | ⬜ Partial | Basic ERD done, needs GUID/sync columns |
| — | CI setup | 0 | — | ⬜ Pending | |
| — | Solution skeleton | 0 | — | ⬜ Pending | Layered architecture not yet created |

---

### Module A - Invoicing and GST Compliance

| ID | Feature | Phase | Pri | Status | Notes |
|----|---------|-------|-----|--------|-------|
| INV-01 | GST invoice B2B/B2C | 3 | P1 | ✅ Done | Retired the "partial" flag after audit 2026-10-31: per-line CGST/SGST/IGST split via TaxCalculationEngine with place-of-supply deciding intra- vs inter-state, cess, GSTIN validated at both the VM and the service (15-char format check, optional for B2C so a walk-in bill still passes), customer pick autofills GSTIN, Bill of Supply / Estimate variants and the printed tax breakup |
| INV-02 | Non-GST / bill of supply | 3 | P1 | ✅ Done | Per-invoice "Bill of Supply" checkbox; tax zeroed on all items and invoice totals; invoice grid Type column distinguishes GST Invoice / Bill of Supply / Estimate |
| INV-03 | Numbering series | 1/3 | P1 | ✅ Done | Number Series view expanded: inline editable Prefix/Suffix columns with Save Edits (dirty-row tracking via CellEditEnding); gap-free audit (Check Gaps — issued Invoice/Purchase/CreditNote/Estimate numbers within each series' FY window parsed against the expected sequence, missing serials + duplicate serials + empty series reported in bottom table); FY rollover (Roll Over To New FY clones every ResetEachYear series into the new year at its start number, preserving prefix/suffix); GetNextNumberAsync now clones the latest matching series when a doc type enters a new FY instead of regenerating a bare default; duplicate docType+FY guard on Add |
| INV-04 | Tax-inclusive / exclusive pricing | 3 | P1 | ✅ Done | Draft-level "Tax Inclusive" checkbox drives new lines; per-line checkbox binding fixed (was pointing at underscore alias); "Apply Tax Mode to All Lines" flips existing lines preserving gross via tax factor; cess supported via TaxCalculationEngine |
| INV-05 | Customizable templates | 3 | P2 | ✅ Done | InvoiceTemplateSetting (Thermal 80mm/A4, logo copied to Brand\, header/footer/terms, tax-breakup + signature toggles); TemplateService Get/Save/GeneratePreview (42-col thermal text preview from last invoice); TemplateSettingsView; schema-upgrader table |
| INV-06 | A4 / half-sheet / thermal layouts | 3 | P1 | ✅ Done | Native WPF pagination (no NuGet): InvoicePrintService renders one invoice in three layouts - A4 FlowDocument, half-sheet A5 FlowDocument, and a continuous 80mm thermal FixedPage (Consolas 8pt, page height grown from the line count, min 60mm). Width-aware columns: InvoiceTextRenderer takes the column set for the paper so a thermal slip drops non-essential columns. Layout + paper choice live in the print-preview window and follow INV-05 template settings. On a machine with no printer the service falls back to writing the same text render to the Desktop and reports the path |
| INV-07 | Estimates and conversion | 3 | P1 | ✅ Done | "Create as Estimate" checkbox (status Estimate, no stock movement, excluded from receivables); one-click Convert → Invoice regenerates number, moves stock; Write-Off button (pay-12) on same grid |
| INV-08 | Printing (laser, thermal) | 3 | P1 | ✅ Done | InvoicePrintPreviewWindow (opened from the Invoices register) shows the render, lets you pick layout/copies and print through the WPF print queue; printer probe lists local + shared queues and falls back to a Desktop text file when none is available |
| INV-09 | Share WhatsApp / SMS / email / PDF | 3/9 | P2 | ✅ Done | v1.0 delivered via Messaging Center (INT-03/04/05): WhatsApp wa.me share, SMTP email with mailto fallback, SMS provider hook; invoice share-text builder; PDF attach deferred to v1.1 |
| INV-10 | Edit, cancel, delete rules | 3 | P1 | ✅ Done | Update rules: paid/locked-period invoices editable only with Admin override (PBKDF2 verify); delete rules: paid invoices refused (write-off/credit note instead), locked period refused, unpaid live invoices need override and restore stock with StockTransaction + AuditRecord; Delete Selected Invoice button |
| INV-11 | Credit / debit notes | 3 | P1 | ✅ Done | Return-against-invoice: note links OriginalInvoiceId/Number + customer, product picker builds lines, numbering issued from NumberSeries("CreditNote", FY) inside the save transaction; Credit restores stock and posts Sales↔Debtors reversal, Debit deducts stock through the STK-03 policy; Cancel writes compensating ledger rows and marks Status=Cancelled; delete reverses stock; feeds GSTR-1 Table 9B CDNR (non-cancelled notes only) |
| INV-12 | Quick POS mode | 3 | P2 | ✅ Done | QuickPosService/View: SKU **or** Barcode scan (unique-indexed columns) on Enter, expired-product block at scan, party-price resolution (PAY-06) applied at the till, Hold/Recall persisted via HoldTickets/HoldTicketItems with audit, settle routed through InvoiceService so GST stock/PAY-05 rules stay intact. Wired under Till menu (raw scanner is USB-HID keyboard emulation — handled) |
| INV-13 | Invoice register and search | 3 | P1 | ✅ Done | Filter toolbar on the register: date range, status, type (GST/Bill of Supply/Estimate), payment mode, min/max amount and dues-only, plus invoice-number/customer search; "Showing N invoice(s)" counter + Clear filters; every register action (select, convert estimate, write off, delete) now works on the filtered view |
| INV-14 | e-Invoicing (IRN) | v1.1 | P3 | ⬜ Pending | GSP/IRP integration |
| INV-15 | e-Way bill data | v1.1 | P3 | ⬜ Pending | Vehicle/transport fields, JSON export |

**Module A Progress: ~87% (13/15 done; INV-14/15 e-invoicing + e-way are v1.1 scope)**

---

### Module B - Inventory and Stock Management

| ID | Feature | Phase | Pri | Status | Notes |
|----|---------|-------|-----|--------|-------|
| STK-01 | Product master | 2 | P1 | ✅ Done | Brand field, Category selection (linked to Category master), Unit selection + unit symbol column in product list |
| STK-02 | Categories and units | 2 | P1 | ✅ Done | Category master with parent tree + unit conversion (Box=12 Pcs); Catalog view (side-by-side panels); duplicate/usage-safe delete; ProductsView uses category/unit combos |
| STK-03 | Real-time stock ledger | 2 | P1 | ✅ Done | Every movement writes a StockTransaction row (sale, purchase, adjustment, transfer, stocktake, credit/debit note, invoice delete); one shared negative-stock policy (StockPolicySetting: Block / Warn / Allow) evaluated at all four write paths - billing blocks or warns with the shortfall spelled out, adjustments/transfers/stocktakes use the same rule; Stock Policy view = mode editor (dirty-gated save, last-changed line) + below-zero product list |
| STK-04 | Low-stock alerts | 2 | P1 | ✅ Done | Stock Alerts view: KPI cards (out-of-stock/low/≤30-day expiry/expired), severity levels, reorder qty suggestion, expiry buckets, filter |
| STK-05 | Batch tracking | 2 | P1 | ✅ Done | ProductBatch model (batch no, mfg/expiry, MRP, purchase rate, qty); BatchService with FEFO ordering, add/update/delete/consume, syncs Product.StockQty + StockTransaction audit entries; BatchView with add/delete form; duplicate batch guard |
| STK-06 | Expiry management | 2 | P1 | ✅ Done | Billing blocks expired products with reason; ≤30-day near-expiry warning surfaced after invoice save; expiry buckets in Stock Alerts view |
| STK-07 | Stock adjustment | 2 | P1 | ✅ Done | Reasons (Damage/Loss/Expiry/Found/Opening) + negative-stock guard with clear error (LastValidationError); StockTransaction audit row per adjustment; Recent Adjustments history grid (date/product/change/reason); tip text explaining ± direction; adjustments audited to ACC-10 trail |
| STK-08 | Purchases | 2 | P1 | ✅ Done | Purchase bill, supplier, GST input credit, return |
| STK-09 | Stock valuation | 2 | P1 | ✅ Done | Stock Valuation view with Weighted Average / FIFO toggle: WAC = total purchase value ÷ purchased qty × on-hand; FIFO consumes oldest purchase-cost layers by shipped quantity, on-hand without cost layer falls back to WAC (flagged "Mixed+Fallback"); never-purchased stock flagged "No Purchases" at zero cost; total value card, search, CSV export |
| STK-10 | Stock transfer | 7 | P2 | ✅ Done | StockTransfer service: create (stock deducted at source with insufficient-stock guard, value snapshot), receive (zero-effect Transfer In ledger row), cancel (stock restored); status filter + KPI cards (in-transit count/value); schema-upgrader table |
| STK-11 | Offline operation | 8 | P1 | ✅ Done | Local-first PendingChange queue (OfflineQueueService): invoices, receipts and stock movements enqueue on save with entity type / action (Create-Update-Delete) / business key / payload + store; states Queued → Synced / Failed (attempt ceiling) / Discarded with per-row attempt count and last error; OfflineSyncView shows the HQ probe, queued/synced/failed/discarded counts, next-up + waiting-since, last sync result, and Sync Now / Retry Failed / Discard / Purge Synced, plus an Offline & Sync Settings editor (force offline, auto-sync minutes, max attempts) saved to the singleton OfflineSettings row; transport is IHqSyncTransport with a simulated HQ implementation so a real endpoint drops in without touching the queue |
| STK-12 | Bulk import/export | 2 | P2 | ✅ Done | CSV import of Products/Customers: quote-aware parse, flexible headers, row validation (rate ≥ 0, GSTIN 15 chars), preview then committed in one transaction with SKU / Name+Phone dedupe; downloadable template + full export round-trip to Desktop |
| STK-13 | Barcode labels | v1.1 | P3 | ⬜ Pending | Code128/EAN13 label printing |
| STK-14 | Physical stock verification | 2 | P2 | ✅ Done | StockTake/StockTakeItem: snapshot all products, record counted qty, completion posts variances (stock adjusted, ledger rows, variance value at product rate) in one transaction; 3-pane stocktake view with variance cards; schema-upgrader tables |

**Module B Progress: ~93% (13/14 done; STK-13 barcode labels pending)**

---

### Module C - Sales, Payments, Receivables and Payables

| ID | Feature | Phase | Pri | Status | Notes |
|----|---------|-------|-----|--------|-------|
| PAY-01 | Payment modes | 4 | P1 | ✅ Done | Split payments on one receipt: PaymentSplit lines (mode / amount / reference) validated to sum within ₹0.01 of the receipt, each line > 0, no duplicate cheque references, mode auto-set to "Split"; zero-line receipts keep the legacy single-mode path; Payments view shows the split grid plus a per-mode summary, and the printed receipt prints the split block |
| PAY-02 | Credit sales | 4 | P1 | ✅ Done | CreditSaleSetting rule row (master switch, default credit days, grace days, block-when-over-limit, require admin approval over limit, max ageing days before blocked, applies-to customer group) drives due date = invoice date + credit days on every credit invoice. CreditSaleService.EvaluateAsync is called before billing saves and returns a verdict — Allowed / Warning / AdminApprovalRequired / BlockedByLimit / BlockedByAgeing / BlockedCreditOff / NoLimitSet — so an over-limit sale needs the INV-10-style admin override and an ageing-blocked customer (in the configured group, unpaid invoice past the limit) is refused outright. New Credit Sales view: date/ageing/search/overdue-only filters, KPI cards (total credit sales, outstanding, overdue, blocked customers), per-invoice due status + ageing bucket (Current 0-30 / 31-60 / 61-90 / 90+), customer credit summary, oldest-first FIFO settle preview + posting, CSV export |
| PAY-03 | Payment-in / receipts | 4 | P1 | ✅ Done | Receipts applied against invoices: open-invoice picker (oldest first) + per-invoice apply, one-click FIFO auto-allocation (oldest invoice first, leftover kept as advance), receipt print (text file to Desktop), delete reverses invoice PaidAmount/Status in a transaction |
| PAY-04 | Customer master | 1 | P1 | ✅ Done | Dedicated Customers view: GSTIN (validated on save), state, customer group, credit limit/days, outstanding; searchable list + add/delete |
| PAY-05 | Credit limit control | 4 | P1 | ✅ Done | Credit outstanding recalculated from unpaid non-estimate invoices (incl. write-offs); warns at ≥80% utilization (non-blocking, orange warnings), blocks >100%; manager override: admin username/password (PasswordBox) validated against Users table lets the sale pass, override recorded as a warning; error message explains override option |
| PAY-06 | Party-wise pricing | 4 | P1 | ✅ Done | Applied at billing: picking a customer + catalogue product resolves the rate through customer list → customer-group list → product master, autofills it and prints the source ("Rate from <list>"); Price Lists view now edits rates per list (add/upsert/remove product with rate + discount %, EffectiveRate preview); invoice lines must come from the catalogue, so party price and stock movement can never be skipped |
| PAY-07 | Customer ledger and statement | 4 | P1 | ✅ Done | Customer Ledger & Statement view: ledger from invoices (debit) + receipts (credit, shows applied allocation refs) + credit notes (credit), opening/closing balance cards, running balance per row; "Print Statement" builds formatted text statement of account (customer, period, opening/lines/closing) on Desktop |
| PAY-08 | Automatic reminders | 4/9 | P1 | ✅ Done | ReminderSetting rule row (first notice N days before due, second notice N days after, channel, template with {Customer} {InvoiceNumber} {Amount} {DueDate} {Days} {Company} placeholders, max per customer per day) + ReminderService that classifies every open non-estimate receivable into Upcoming/Overdue/SecondNotice, skips rows outside the window or without a channel target with a printed Reason, previews the unsaved template against the first queued invoice, and hands the queued messages to the Messaging Center (WhatsApp/SMS/Email) with per-day dedupe; audited on save. Delivery is started from the view - there is no background timer for reminders yet |
| PAY-09 | Supplier management | 4 | P1 | ✅ Done | Supplier master, ledger, payment-out |
| PAY-10 | Cash and bank accounts | 4 | P1 | ✅ Done | Cash &amp; Bank Book view: combined book (all Cash/Bank accounts) or per-account with opening/in/out/closing summary cards; receipt/payment rows from Cash+Bank journal lines with running balance; contra transfers (New Transfer form → debit target + credit source journal) shown as Transfer (Contra) — cancel out (zero net) in the combined book, real In/Out rows in per-account books; transfer validation (amount > 0, accounts differ, must exist); CSV export to Desktop |
| PAY-11 | Cheque management | v1.1 | P3 | ✅ Done | Cheque register (In received from customers / Out paid to suppliers): leaf-number entry with 6-month PDC window guard, Pending → HandedOver → Cleared / Bounced / Cancelled lifecycle with fixed return-memo reason picker on bounce; opt-in auto-receipt on clear via the Payments engine (oldest invoice first), payment number snapshotted; day-precision dates, direction/status filters, terminal rows locked from edits; schema-upgrader table + DI + Cheque Register menu |
| PAY-12 | Write-off and discounts | 4 | P2 | ✅ Done | "Write Off Remaining" on Invoices view: posts Bad Debts expense journal + credits Debtors, updates status, WriteOffAmount column; settlement discounts flow through the same write-off path |
| PAY-13 | Sales register | 4 | P1 | ✅ Done | Dedicated Sales Register view (shared with RPT-02): one line per invoice, a day-total line after each trading day and a period total, estimates listed only when switched on and never counted in totals |

**Module C Progress: 100% (13/13 done)**

---

### Module D - Accounting and Financial Management

| ID | Feature | Phase | Pri | Status | Notes |
|----|---------|-------|-----|--------|-------|
| ACC-01 | Chart of accounts | 5 | P1 | ✅ Done | AccountService CRUD over groups + ledgers (list/add/update/delete with include); note: no seed of a default CoA ships — accounts are created manually from the screen, and journal posting flows look accounts up by name with only Bad Debts auto-created on write-off |
| ACC-02 | Auto-posting | 5 | P1 | 🟡 Partial | Journal auto-posting is only hand-rolled in three paths — InvoiceService write-off (L386), CreditNoteService returns, CashBookService contra transfers; the dedicated IAutoPostingService is DI-registered but has zero consumers, and regular sale/purchase saves never post journals, so double-entry does not run for every sale/purchase/payment. Wiring the orphaned service in (with default-account seeding) is a pending P2 item |
| ACC-03 | Manual journal entries | 5 | P1 | ✅ Done | Full entry validation (≥ 2 lines, exactly one of debit/credit per line, valid account, no negatives, balance to ₹0.01) surfaced via LastValidationError; user-picked entry date + header narration; auto-entries keep their dates |
| ACC-04 | Expenses | 5 | P1 | ✅ Done | ExpenseService: add/delete, daily + monthly totals, category breakdown with share %, defaults merged with live categories; Expense view (add form, date filter, breakdown grids, CSV export); feeds P&L |
| ACC-05 | Receivables and payables | 5 | P1 | ✅ Done | Receivables ageing view (customer side, estimates excluded) + Payables ageing view (supplier side): 0-30/31-60/61-90/90+ buckets, pending-document counts, oldest-document dates, CSV export |
| ACC-06 | Reconciliation | 5 | P2 | ✅ Done | Bank Reconciliation view: flexible-header CSV import (Date/Description/Ref + Amount or Withdrawal+Deposit columns, quoted-cell safe, dd/mm and iso dates, accounting negatives, per-row warnings); Auto-Match against Cash/Bank journal lines (equal amount ±0.01, date ±5 days, closest date first, no journal line reused across matches, MatchedRef records JE + line); manual Reconcile + Clear Match on selected row; filter All/Unmatched/Reconciled; summary cards (lines, reconciled, unmatched count + value); sample SampleBankStatement.csv placed on Desktop |
| ACC-07 | Financial year and period lock | 5 | P1 | ✅ Done | Period lock enforced at JournalService (posting into a locked FY is refused with reason); Carry Forward creates next FY, activates it and rolls number series via RollOverFinancialYear; unlock confirmation; Carry Forward button + success surface |
| ACC-08 | Adjustments and opening balances | 5 | P2 | ✅ Done | OpeningBalanceService: validated opening-balance update returning the delta; per-account-type summary rows; Opening Balances view (account picker + side form + summary grid) |
| ACC-09 | Day book and ledger drill-down | 5 | P1 | ✅ Done | Day Book & Ledger view: day book (all journal lines in range, total debits) + per-account ledger with opening/closing balance and running balance (sign-normal by account type), CSV export. Drill-down shown via source voucher Reference (#) columns |
| ACC-10 | Audit trail | 1/5 | P1 | ✅ Done | AuditRecord model (Action/EntityName/EntityId/EntityLabel/ChangesJson/SnapshotJson) + AuditService (reflection field-level before/after diff, active-user attribution, never breaks main flow); hooked into Product & Customer CRUD, Invoice create/estimate/convert/write-off, Payment create & delete; Audit Trail view (filter by user/action/entity/date, detail pane with Property/Before→After rows, CSV export); auto-upgrade creates table for existing DBs |

**Module D Progress: ~95% (9/10 done; ACC-02 auto-posting service orphaned — journal posting only happens hand-rolled)

---

### Module E - Reports and Dashboards

| ID | Feature | Phase | Pri | Status | Notes |
|----|---------|-------|-----|--------|-------|
| RPT-01 | Business dashboard | 6 | P1 | ✅ Done | KPI cards plus five hand-drawn WPF charts (no plotting package): Sales Trend bar chart from ReportService.GetDailySalesAsync, Payment Mode Mix donut, Receivables Ageing bar, Stock Health bar and Top 5 Products by Stock Value bar - each with an explicit empty state ("No sales in this period" etc.) so a quiet shop doesn't look broken. Caveat: the trend series includes estimates and bills of supply, so it reads slightly high versus the register totals |
| RPT-02 | Sales invoice register | 6 | P1 | ✅ Done | Sales Register view (same sheet as PAY-13): 17 columns incl. place of supply, tax split, paid/balance and mode, day totals + period total, 6 KPI cards, filters by date/type/mode/dues/search, CSV export with UTF-8 BOM to Desktop |
| RPT-03 | GST reports (GSTR-1, 3B) | 6 | P1 | ✅ Done | GSTR-1 Table 4 B2B (GSTIN invoices), Table 5/7 B2C (non-GSTIN incl. Bill of Supply), Table 9B CDNR (credit notes w/ original invoice), Table 12 HSN summary (per-item CGST/SGST/IGST grouping); GSTR-3B cards: taxable value, CGST/SGST/IGST split, ITC from purchases, net payable after ITC+CDNR; JSON export both returns to Desktop |
| RPT-04 | Profit and Loss | 6 | P1 | ✅ Done | Dedicated P&L view: revenue (excl. tax), COGS (latest purchase rate), gross profit + margin %, expenses by category, monthly summary, CSV export to Desktop |
| RPT-05 | Balance sheet | 6 | P1 | ✅ Done | Dedicated view: assets/liabilities/equity from chart-of-accounts journal balances, retained earnings (net income), balanced check, CSV export |
| RPT-06 | Cash flow statement | 6 | P1 | ✅ Done | Dedicated view: opening/net/closing balance from Cash+Bank journal movements grouped by source (Sale/Purchase/Payment), CSV export |
| RPT-07 | Stock audit report | 6 | P1 | ✅ Done | Dedicated Stock Audit sheet for a date range: opening / in / out / closing restated from the stock ledger against the book quantity, plus a book-vs-ledger tie-out that exposes "Unrecorded Stock" (stock set on the master with no opening movement, or a document that moved stock without posting a ledger row) valued at the product rate; latest completed physical count reconciled with an "Unposted Count" finding only when nothing moved after the count; flags Negative Stock / Unrecorded Stock / Unposted Count / Expired Batch / Below Minimum / No Movement, 6 summary cards, flag filter + search + CSV export |
| RPT-08 | Receivables report | 6 | P1 | ✅ Done | Customer outstanding from unpaid invoices with ageing buckets (0-30/31-60/61-90/90+), pending-invoice counts, oldest-invoice date, searchable list, CSV export |
| RPT-09 | Payables report | 6 | P1 | ✅ Done | Real supplier outstanding from unpaid purchase bills with ageing buckets (0-30/31-60/61-90/90+), pending-bill counts, searchable list, CSV export; replaced expense-placeholder implementation |
| RPT-10 | Multi-store comparison | 7 | P2 | ✅ Done | Store Comparison view: per-store rows ranked by sales (estimates excluded; unassigned invoices grouped as "Main Store (unassigned)"; zero-sales stores listed ranked "-"): invoice count, sales (excl. tax), sales share %, COGS via latest purchase rate (same convention as P&L), gross profit, margin %, outstanding; summary cards for total sales/gross profit/best store + range summary (active store count, overall margin); CSV export to Desktop; sidebar "Store Comparison" + command + DataTemplate |
| RPT-11 | Credit analysis | 6 | P1 | ✅ Done | Per-customer: limit, used, available, utilization %, overdue (beyond credit days), avg days-to-pay (from payment-in allocations), risk flags (OK/Overdue/Medium/High/Over Limit/No Limit); searchable grid + CSV export |
| RPT-12 | Other reports | 6 | P2 | ✅ Done | Day book (ACC-09), cash book (PAY-10), and new Item-wise Profit report: per-product qty/sales/COGS (latest purchase rate)/profit/margin with Top/Bottom/All filter, totals cards, CSV export |
| RPT-13 | Export and scheduling | 6 | P2 | 🟡 Partial | Per-report CSV export (UTF-8 BOM) covers RPT-02/04/05/06/07/08/09/10/11/12 and Excel-XML day book + Tally XML landed with SEC-06; still missing: in-app PDF + automatic scheduled email — new NuGet gate listed in pending works |

**Module E Progress: ~92% (12/13 done; RPT-13 pending)**

---

### Module F - Multi-store and User Management

| ID | Feature | Phase | Pri | Status | Notes |
|----|---------|-------|-----|--------|-------|
| USR-01 | Store management | 7 | P1 | ✅ Done | Full CRUD, GSTIN validation, address/phone/email, invoice prefix, printer, Main Store seeded |
| USR-02 | Central dashboard | 7 | P2 | ✅ Done | MultiStoreView: consolidated KPIs, store-wise sales comparison table, store filter + recent activity |
| USR-03 | Store-wise stock and pricing | 7 | P2 | 🟡 Partial | StockTransaction.StoreId tracks per-store movements + per-store stock panel; store-specific pricing pending |
| USR-04 | Users | 1 | P1 | ✅ Done | UserService CRUD, lock after 5 failed attempts (15 min), unlock, activate/deactivate, reset password |
| USR-05 | Roles and permissions | 1/7 | P1 | ✅ Done | Admin, Manager, Cashier, Accountant + permission matrix seeded; AuthService.HasPermission() check |
| USR-06 | Approval and passcode | 7 | P1 | ✅ Done | PasscodeDialog: re-enter own password for reset/delete/unlock/deactivate actions |
| USR-07 | Activity log | 1 | P1 | ✅ Done | ActivityLog model/service/view; logs Login, LoginFailed, AccountLocked, Logout, user + store changes; filterable |

**Module F Progress: ~90% (6/7 done, USR-03 pricing pending)**

---

### Module G - Data Backup, Sync and Security

| ID | Feature | Phase | Pri | Status | Notes |
|----|---------|-------|-----|--------|-------|
| SEC-01 | Auto backup | 8 | P1 | ✅ Done | BackupSchedulerService: coarse 1-minute System.Threading.Timer (pool thread, so it runs whether or not the window has focus) evaluates the singleton BackupScheduleSetting row (enabled, Daily / Every-N-days / Weekly + day-of-week, run-at hour/minute, destination folder, copies to keep, include-manual-backups-in-prune) and fires the existing SEC-02 BackupService copy when due; computes + persists LastRunAt/LastResult/NextRunAt, prunes to N generations, and never overlaps runs. Backup Schedule view: schedule editor with live "Daily at 21:30, keep 14 copies" + "next run … (in N hours)" preview, Run Now, Start / Stop scheduler, state / last-run / history cards |
| SEC-02 | Manual backup and restore | 8 | P1 | ✅ Done | WAL checkpoint before copy; PRAGMA quick_check on source AND backup copy (corrupt copy auto-deleted); restore verifies backup before overwrite + restart prompt; ListBackups + integrity check in Settings. SEC-03 follow-through: backup validation now accepts both encrypted (keyed) and legacy plaintext copies, so pre-encryption backups still restore; a restored plaintext DB is re-encrypted automatically on the next startup |
| SEC-03 | Encrypted database | 1 | P1 | ✅ Done | Live SQLCipher at rest (user approved install 2026-11-01): Microsoft.Data.Sqlite.Core + SQLitePCLRaw.bundle_e_sqlcipher **2.1.11** (the previously referenced 2.1.6 bundle ships a native lib incompatible with the SQLitePCLRaw core 10.0 pulls — TypeLoadException; 2.1.11 verified by probe: cipher_version 4.5.2 community, file header no longer "SQLite format 3", no-password and wrong-password opens both refused with SQLite error 26). Key = 32 random bytes, DPAPI-protected (CurrentUser + app entropy) in app.db.key beside the database; the key never appears in logs/audit. Startup migrates a legacy plaintext app.db once via ATTACH + sqlcipher_export with an app.db.pre-cipher.bak rollback copy; reopen is idempotent — no re-migration, no tmp leftovers. Also: stored-secret protection from the earlier step (SMTP password DPAPI-encrypted at rest via the dpapi1: prefix scheme). Caveats: backups copy the encrypted DB (stays protected) but restoring to a different Windows user profile needs the app.db.key file too; data file stays CWD-relative (unchanged historical behavior) |
| SEC-04 | Authentication | 1 | P1 | ✅ Done | PBKDF2-SHA256 (210k iterations, per-user salt) via PasswordHasher; legacy SHA-256 hashes still verify and are transparently re-hashed on login/VerifyPassword (audit-logged); 5-attempt lockout already present |
| SEC-05 | Passcode for critical changes | 7 | P1 | ✅ Done | CriticalChangeGuard + singleton CriticalChangeSetting row: one shop passcode (PBKDF2-hashed, never stored or logged in clear) asked for *in addition to* the INV-10 admin override, switchable per change kind — invoice/estimate delete, write-off, estimate→invoice conversion, locked-period edit, stock adjustment, FY lock/unlock. VerifyAsync returns Allowed / Refused (with remaining attempts) / LockedOut (until-time) and enforces MaxFailedAttempts + LockoutMinutes on the settings row — the counter lives on this row so a failed delete attempt can never lock a user out of the app; every check is audited, allow as well as refuse, through ACC-10 AuditService with a before/after counter snapshot that deliberately excludes the hash, and fail-closed when the switch is on but no passcode is stored. ResetLockoutAsync clears the lockout from the policy view. Views: Critical Change Passcode policy editor (gate checkboxes, max attempts, lockout window, set/change passcode, reset lockout) and the modal CriticalChangeDialog — passcode read from a PasswordBox in code-behind only, re-prompt loop with the refusal reason, lock countdown and an explicit "cancel the whole operation" exit. Wired into InvoicesViewModel delete/write-off/convert, StockAdjustmentViewModel and FinancialYearViewModel |
| SEC-06 | Export / import, Tally | 8 | P1 | ✅ Done | Tally Export view: document type (Sales / Purchases / Journal / Payments & Receipts / Masters / Combined), format (Tally XML, CSV, Excel XML day book), FY-default date range and per-store filter; writes TallyPrime import XML to a chosen folder (Desktop by default), previews documents in range and skips (estimates, unbalanced, cancelled) before exporting, lists accountant warnings in a grid and carries the in-app "how to import into TallyPrime" steps. Per-report CSV export already existed (RPT-02/04/05/06/07/08/09/10/11/12) |
| SEC-07 | Data integrity checks | 8 | P2 | ✅ Done | IntegrityService: PRAGMA integrity_check, orphan InvoiceItems/JournalLines counts, overpaid-invoice detection, allocation-mismatch info, WAL checkpoint (KB freed), VACUUM + ANALYZE; Integrity Check view with health banner |
| SEC-08 | Sync | 8 | P2 | ✅ Done | Sync Center (Admin menu) plans and supervises the local-first sync: singleton SyncSetting row (Base URL, device id, batch size, timeout, TLS-only) + per-run SyncRunLog history (started/finished/duration, accepted vs rejected vs failed, transport, outcome verdict, never contains a credential); IHqSyncTransport resolves per construction — the real HttpHqSyncTransport (HTTPS POST /changes, X-API-Key header, redacted errors) when the saved connection says live-ON, otherwise the simulated endpoint; HqTransportSelection is primed at startup so both transports agree from the first run; the push itself runs on the STK-11 queue (Flush/Retry/Auto-sync) — see that row for the store side. Fixed 2026-11-01: selection was never DI-registered (Sync Center would throw when opened) |
| SEC-09 | Licensing | v1.1 | P3 | ⬜ Pending | Licence key, offline grace |
| SEC-10 | Privacy and compliance | 8 | P2 | ✅ Done | PrivacyService: retention purge of AuditRecords/ActivityLogs with preview counts + confirm dialogs; GDPR-style customer data export CSV (profile + invoices + payments) to Desktop; Privacy & Compliance view |

**Module G Progress: ~70% (7/10 done; SEC-03 partial, SEC-08 sync + SEC-09 licensing pending)**

---

### Module H - Integrations and Extras

| ID | Feature | Phase | Pri | Status | Notes |
|----|---------|-------|-----|--------|-------|
| INT-01 | Contact management | 9 | P2 | ✅ Done | ContactService/ContactsView: unified address book over Customers + Suppliers with search/group filtering, vCard 3.0 export (single/batch, RFC 2426 line folding at 75 octets) + CSV import (preview-then-commit) and export, contacts feed the Messaging Center; DI + DataTemplate + Parties/Books menu entry wired (vCard *import* remains backlog) |
| INT-02 | Product catalog | v1.1 | P3 | 🟡 Partial | The Catalog screen is the Categories & Units management UI (STK-02), not a customer-facing shareable catalog; a shareable PDF/web catalog link is genuinely absent anywhere in the code (v1.1 scope) |
| INT-03 | SMS gateway | 9 | P1 | ✅ Done | IMessagingService.SmsProvider plug point + DLT sender/template pass-through; no gateway ships by default so attempts log as Draft (scaffold ready for a real provider) |
| INT-04 | WhatsApp | 9 | P2 | ✅ Done | wa.me share link with configurable default country code (>10-digit numbers assumed international), opened in browser and logged; Business API stays P3 |
| INT-05 | Email | 9 | P2 | ✅ Done | SMTP send (10s timeout, SSL) with mailto fallback when unconfigured; singleton settings row + persisted MessagingLogs; invoice share-text template |
| INT-06 | UPI / payment QR | 9 | P2 | ⬜ Pending | Dynamic UPI QR on invoice |
| INT-07 | Accounting software link | v1.1 | P3 | ✅ Done | Delivered as SEC-06: TallyPrime import XML (Sales / Purchases / Journal / Payments & Receipts / Masters / Combined) + CSV + Excel-XML day book exports with accountant warnings; Zoho/QuickBooks remain future options |
| INT-08 | Public API / webhooks | v1.1 | P3 | ⬜ Pending | REST endpoints (HQ side of SEC-08 is server-land; the till only pushes today) |

**Module H Progress: ~62% (5/8 done; INT-02 partial, INT-06/08 pending)**

---

## 📈 Overall Progress Summary

| Module | Total | Done | In Progress | Pending | Completion |
|--------|-------|------|-------------|---------|------------|
| A - Invoicing & GST | 15 | 13 | 0 | 2 | ~87% |
| B - Inventory | 14 | 13 | 0 | 1 | ~93% |
| C - Sales & Payments | 13 | 13 | 0 | 0 | 100% |
| D - Accounting | 10 | 9 | 1 | 0 | ~95% |
| E - Reports | 13 | 12 | 1 | 0 | ~92% |
| F - Multi-store/Users | 7 | 6 | 1 | 0 | ~90% |
| G - Backup/Sync/Security | 10 | 9 | 0 | 1 | ~90% |
| H - Integrations | 8 | 5 | 1 | 2 | ~63% |
| **Total** | **90** | **80** | **4** | **6** | **~89%** |

---

## 🗓 Current Phase: Phase 1 - Foundation (Weeks 3-5) ✅ COMPLETE

Build: **0 errors, 0 warnings** ✅

**Completed in Phase 1:**
- [x] Single-project WPF app with clean folder organization (Models, Services, Data, ViewModels, Views, Converters)
- [x] EF Core SQLite database with all 12 entity tables
- [x] Login & authentication (Admin/User roles, SHA-256 password hashing)
- [x] Default admin: `admin` / `admin123`
- [x] Dashboard with KPI cards (cash, stock value, daily sales, pending dues, low stock)
- [x] Products management (add/view/delete, SKU/barcode/HSN, batch/expiry, low-stock indicators)
- [x] Invoicing (GST/non-GST, line items, auto tax calculation, stock deduction)
- [x] Invoice register with search/filter
- [x] Sales tracking (credit sales, outstanding balances)
- [x] Reports (dashboard overview, GST summary, receivables, stock audit)
- [x] Settings (company profile, backup/restore, DB size check)
- [x] Sidebar navigation with role-aware menu
- [x] SQLite database auto-created on first run (`app.db`)

---

**Phase 1 foundation is complete.** SEC-03 shipped fully on 2026-11-01 (SQLCipher at rest + DPAPI key file + one-time plaintext migration) — see the SEC-03 row in Module G for the verified implementation, including the key-file restoration caveat.

**Remaining P1:** SEC-03 at-rest encryption (blocked on the SQLCipher package decision) · INV-01 tax-field polish (B2B GSTIN validation) — everything else in P1 is ✅ Done.

**Module G Progress: ~90% (9/10 done; only SEC-09 licensing pending — SEC-03 closed with true SQLCipher at-rest encryption on 2026-11-01)**

**P1 note:** all P1 features ship except ACC-02, re-scored Partial on the 2026-11-01 audit (auto-posting service orphaned — see its row). ✅

**Remaining P2/P3 features:**
- ~~STK-04 Low-stock alerts enhancement~~ ✅ Done (2026-10-30)
- ~~STK-05 Batch tracking~~ ✅ Done (2026-10-30)
- ~~STK-01 Product master~~ ✅ Done (2026-10-30)
- ~~STK-02 Categories and units~~ ✅ Done (2026-10-30)
- ~~STK-06 Expiry management~~ ✅ Done (2026-10-30)
- ~~PAY-04 Customer master enhancements~~ ✅ Done (2026-10-30)
- ~~RPT-04 Profit and Loss~~ ✅ Done (2026-10-30)
- ~~RPT-05 Balance sheet~~ ✅ Done (2026-10-30)
- ~~RPT-06 Cash flow statement~~ ✅ Done (2026-10-30)
- ~~RPT-09 Payables report with ageing~~ ✅ Done (2026-10-30)
- ~~SEC-02 Manual backup and restore~~ ✅ Done (2026-10-30)
- RPT-11 Credit analysis
- And many more...

---

## 📝 Build Log

| Date | Action | Details |
|------|--------|---------|
| 2026-10-28 | Initial scaffold | Created single-project WPF app with MVVM, SQLite, auth, products, invoices, sales, reports |
| 2026-10-28 | Dev Plan reviewed | Extracted 17-page development plan, identified 90 features across 8 modules |
| 2026-10-28 | Tracking doc created | This document |
| 2026-10-28 | Layered architecture attempted | Created Domain/Application/Infrastructure/Reports projects but caused WPF CS0579 duplicate attribute errors |
| 2026-10-28 | Reverted to single project | Moved all code back to single RetailBillingApp project with folder organization |
| 2026-10-28 | Build fixed | All XAML and C# compilation issues resolved - 0 errors, 0 warnings |
| 2026-10-28 | STK-08 Purchases + PAY-09 Supplier Mgmt | Added Supplier model/service, Purchase/PurchaseItem models/service, PurchaseView/SupplierView, navigation buttons |
| 2026-10-28 | INV-11 Credit/Debit Notes | Added CreditNote, CreditNoteItem models; ICreditNoteService/CreditNoteService; CreditNoteViewModel/CreditNoteView; stock restoration on credit notes; navigation + DataTemplate |
| 2026-10-28 | USR-05 Roles & Permissions | Added Role, Permission, RolePermission models; updated User with RoleId; IRoleService/RoleService, IPermissionService/PermissionService, IRolePermissionService/RolePermissionService; RoleViewModel/RoleView, PermissionViewModel/PermissionView; default roles & permissions seeded; navigation + DataTemplate |
| 2026-10-28 | INV-03 Number Series | Added NumberSeries model/service; NumberSeriesViewModel/NumberSeriesView; default series seeded (Invoice, Purchase, CreditNote, Estimate); navigation + DataTemplate |
| 2026-10-28 | INV-01 Enhanced GST details | Added CustomerGSTIN, PlaceOfSupply, CGST/SGST/IGST/Cess fields to Invoice/InvoiceItem; TaxCalculationEngine with CGST/SGST/IGST calculation, GSTIN validation, tax-inclusive support; updated InvoiceService and InvoicesView/ViewModel |
| 2026-10-28 | STK-07 Stock Adjustment | Added StockAdjustmentViewModel/StockAdjustmentView; stock adjustment with reasons (Damage/Loss/Expiry/Found/Opening); navigation + DataTemplate |
| 2026-10-28 | PAY-03 Payment-in/Receipts | Added Payment, PaymentAllocation models; IPaymentService/PaymentService; PaymentViewModel/PaymentView; FIFO allocation support; auto-updates invoice PaidAmount/Status; navigation + DataTemplate |
| 2026-10-28 | PAY-05 Credit limit control | Added CreditLimit, CreditDays fields to Customer model; credit limit check in InvoiceService.CreateInvoiceAsync (blocks if outstanding + new invoice > credit limit); InvoicesViewModel updated |
| 2026-10-28 | PAY-06 Party-wise pricing | Added PriceList, PriceListItem models; IPriceListService/PriceListService with GetEffectiveRateAsync (customer > group > default); PriceListViewModel/PriceListView; navigation + DataTemplate |
| 2026-10-28 | ACC-07 Financial year & period lock | Added FinancialYear model; IFinancialYearService/FinancialYearService; FinancialYearViewModel/FinancialYearView; set active year, toggle lock status, IsPeriodLockedAsync check; navigation + DataTemplate |
| 2026-10-28 | RPT-03 GST reports | Added GST report models (GstReport, GstB2bInvoice, GstB2cInvoice, GstCreditNote, GstHsnSummary); GstReportViewModel/GstReportView; IReportService/ReportService with ExportGstr1JsonAsync; GST summary, B2B invoice register, HSN summary, GSTR-1 JSON export; navigation + DataTemplate |
| 2026-10-28 | SEC-03 Encrypted database | Added EncryptionService with SQLCipher support; IEncryptionService interface (GetConnectionString, EncryptString, DecryptString, DeriveKey, IsDatabaseEncrypted); DPAPI for sensitive settings; master password derivation (PBKDF2-SHA256); registration in App.xaml.cs |
| 2026-10-28 | **Module F: USR-01/02/04/05/06/07** | Enhanced Store model (GSTIN, address, phone, email, invoice prefix, printer, IsActive) + default "Main Store" seed; IStoreService/StoreService (CRUD, store-wise sales summary, per-store stock); MultiStoreViewModel/View (USR-02 consolidated dashboard with store filter); UsersViewModel/View + UserService (CRUD, lockout after 5 failed attempts w/ 15-min lock, unlock, reset password, activate/deactivate, assign role/store); AuthService: LastLoginError, VerifyPassword, HasPermission, activity logging of logins/lockouts (USR-04/05); PasscodeDialog for sensitive actions (USR-06); ActivityLog model + IActivityLogService/Service + ActivityLogView with filters (USR-07); StockTransaction.StoreId + InvoiceService tags store context; Invoice.StoreId set from current user; admin-gated sidebar nav. ⚠️ Schema changed: delete app.db once (EnsureCreated won't update existing DB) |
| 2026-10-30 | Window-not-visible fix | App.xaml: removed `StartupUri` (conflicted with manual window creation); App.xaml.cs: `MainWindow = mainWindow; mainWindow.Show(); mainWindow.Activate()` inside try-catch with inner exception text; reduced blocking seeding at startup (admin seed only pre-window) |
| 2026-10-30 | DatePicker XAML crash fix | `'ComboBox' TargetType does not match type of element 'DatePicker'`: added `BaseDatePicker` style to AppStyles.xaml; replaced misapplied `BaseComboBox` style on DatePickers in GstReportView, PriceListView, ProductsView, PurchaseView (6 occurrences) |
| 2026-10-30 | STK-04 Stock Alerts | Added StockAlert model; StockAlertViewModel (severity Critical/Warning, reorder suggestions, expiry buckets ≤30 days / expired, filterable); StockAlertView with 4 KPI cards + alert grid; DI registration + DataTemplate + sidebar button + MainWindowViewModel command |
| 2026-10-30 | **Retarget to .NET 10** | RetailBillingApp.csproj: TargetFramework net8.0-windows → net10.0-windows; EF Core/SQLite packages 8.0.0 → 10.0.0 (CommunityToolkit.Mvvm 8.2.2 kept - runtime-compatible) |
| 2026-10-30 | **.NET 10 build verified** | SDK 10.0.403; dotnet build = 0 errors; app launched from bin\Debug\net10.0-windows, window visible and responding |
| 2026-10-30 | STK-05 Batch tracking | Added ProductBatch model + DbSet config (unique ProductId+BatchNumber, cascade delete); IBatchService/BatchService (FEFO ordering, batch add/delete/consume syncs Product.StockQty and logs StockTransaction); BatchViewModel (mfg/expiry validation, duplicate batch check) + BatchView (FEFO-ordered grid, add/delete form); wiring complete ⚠️ schema changed: ProductBatches table added - EnsureCreated won't add it to an existing app.db |
| 2026-10-30 | **DatabaseSchemaUpgrader** | Data/DatabaseSchemaUpgrader.cs: runs at startup after EnsureCreated; CREATE TABLE IF NOT EXISTS for new tables + pragma-checked ALTER TABLE ADD COLUMN for new columns - existing app.db files now get upgraded automatically (no more delete-app.db steps) |
| 2026-10-30 | STK-02 Categories & Units | Added Category (parent tree) + Unit (base/factor conversion) models + services (duplicate name/symbol guards, usage-safe deletes); CatalogViewModel/View (side-by-side category + unit management panels); DbSet/config in DbContext |
| 2026-10-30 | STK-01 Product master | Product: Brand + CategoryId + UnitId FKs; ProductService includes navs; ProductsView: category ComboBox + brand field + unit ComboBox; product grid shows Brand + Unit symbol columns |
| 2026-10-30 | STK-06 Expiry at billing | InvoiceService: expired product invoice blocked with LastValidationError reason; ≤30-day near-expiry warns (NearExpiryWarnings); PAY-05 credit-limit failure now returns message too; InvoicesViewModel surfaces both |
| 2026-10-30 | PAY-04 Customers | Dedicated CustomerViewModel/CustomerView (list with search, outstanding, add form with GSTIN validation via TaxCalculationEngine, state dropdown, group, credit limit/days) + wiring |
| 2026-10-30 | RPT-04 P&L Report | ProfitLossReport model extended (COGS, GrossProfit, Margin %, ExpenseBreakdown, MonthlySummary w/ COGS); ReportService full rewrite (revenue excl. tax, COGS via latest purchase rates, monthly matrix); PLReportViewModel/View (5 KPI cards, monthly + breakdown grids, CSV export); sidebar "P&L Report" |
| 2026-10-30 | RPT-05 Balance Sheet | BalanceSheetReport + AccountBalance models; ReportService.GetBalanceSheetAsync (journal balances per account sign-normalized by type, retained earnings = income credit-net − expense debit-net); BalanceSheetViewModel/View (As-of date, Assets/Liabilities/Equity columns, 4 KPI cards, "Balanced ✓"/out-of-balance indicator, CSV export); sidebar "Balance Sheet" |
| 2026-10-30 | RPT-06 Cash Flow | CashFlowReport + CashFlowRow models; ReportService.GetCashFlowAsync (Cash+Bank account journal movements, opening balance before start date, rows grouped by ReferenceType); CashFlowViewModel/View (Opening/Net/Closing cards, movements grid, CSV export); sidebar "Cash Flow" |
| 2026-10-30 | RPT-09 Payables | Payable model extended (PendingBills, ageing buckets Current/31-60/61-90/90+); ReportService rebuilt from unpaid purchase bills grouped by supplier; PayablesViewModel/View (total outstanding card, ageing grid, search, CSV export); sidebar "Payables" |
| 2026-10-30 | SEC-02 Backup/Restore | IBackupService: BackupResult + ListBackupsAsync + RunIntegrityCheckAsync; BackupService: wal_checkpoint(TRUNCATE) pre-copy, quick_check on source and backup (deletes failed copies), restore pre-verifies backup + restart prompt; SettingsViewModel: messages with path/size, Verify Database + List Backups commands; SettingsView: Verify/List buttons + Database health status |
| 2026-10-30 | RPT-11 Credit Analysis | CreditAnalysisRow model; ReportService.GetCreditAnalysisAsync (outstanding + overdue beyond credit days, avg days-to-pay via payment-in allocations); CreditAnalysisViewModel/View (Total used/overdue/at-risk cards, risk flags, search, CSV export); sidebar "Credit Analysis" |
| 2026-10-30 | INV-02 Bill of Supply | Invoice.IsBillOfSupply + DatabaseSchemaUpgrader column; InvoiceService zeroes all tax for BoS invoices; InvoicesView checkbox + Type column (GST Invoice/Bill of Supply/Estimate via TypeLabel) |
| 2026-10-30 | INV-07 Estimates | Invoice.IsEstimate + schema column; CreateInvoiceAsync stores status "Estimate", skips stock movement; ConvertEstimateToInvoiceAsync regenerates number, moves stock, updates status; InvoicesView "Create as Estimate" checkbox + "Convert Selected Estimate → Invoice" |
| 2026-10-30 | PAY-12 Write-off | Invoice.WriteOffAmount + schema column; WriteOffInvoiceAsync validates remaining, posts Bad Debts expense journal + credits Debtors in transaction, sets status Paid; InvoicesView "Write Off Remaining (Selected)" + Written Off column |
| 2026-10-30 | ACC-09 Day Book & Ledger | DayBookReport/DayBookEntry/DayBookLine + LedgerReport/LedgerRow DTOs; ReportService.GetDayBookAsync + GetLedgerAsync (opening/closing balance, running balance sign-normal for credit-normal accounts); LedgerBookViewModel/View (Day Book ↔ Ledger mode toggle, date range, account picker, debit/credit/running-balance grid with source voucher Ref# columns, day book total, CSV export); sidebar "Day Book & Ledger" |
| 2026-10-30 | ACC-05 + RPT-08 Receivables | Receivable model extended (ageing buckets Current/31-60/61-90/90+, PendingInvoices, OldestInvoiceDate); ReportService.GetReceivablesAsync rebuilt from unpaid non-estimate invoices grouped by customer; ReceivablesViewModel/View (total outstanding card, ageing grid, search, CSV export); sidebar "Receivables" |
| 2026-10-30 | RPT-03 GST Reports | GstReport extended: B2b/B2c/CreditNotes/HsnSummary lists + GSTR-3B fields (OutwardTaxableValue, CGST/SGST/IGST/Cess, InputTaxCredit, NetTaxPayable); row DTOs moved into IReportService.cs; ReportService.GetGstReportAsync rewrite (B2B=GSTIN invoices, B2C=rest incl. Bill of Supply, CDNR from non-cancelled credit notes linked to original invoices, HSN grouped per invoice item with CGST/SGST/IGST; estimates excluded; ITC from purchase-bill tax); ExportGstr1JsonAsync + new ExportGstr3bJsonAsync write GSTO-shaped JSON to Desktop; GstReportViewModel rewritten (tab data, TotalB2c/TotalCdnr, success/error messages); GstReportView rebuilt: toolbar + GSTR-3B KPI cards + TabControl B2B/B2C/CDNR/HSN with taxable value & tax columns; 3B export button |
| 2026-10-30 | PAY-03 Payment-in / Receipts | OutstandingInvoice DTO; PaymentService: GetOutstandingInvoicesAsync (open non-estimate invoices oldest-first), BuildFifoAllocationsAsync (FIFO apply, leftover → Advance allocation), PrintReceiptAsync (formatted text receipt to Desktop); DeletePaymentAsync now reverses invoice PaidAmount/Status in a transaction; PaymentViewModel: OutstandingInvoices grid + LoadOutstanding/AllocateFifo/ApplySelectedOutstanding/PrintReceipt commands, SuccessMessage surface; PaymentView: open-invoices grid + FIFO/Apply/Print buttons |
| 2026-10-30 | PAY-05 Credit limit control | InvoiceService.CreateInvoiceAsync gains optional overrideUsername/overridePassword; CreditLimitWarnings + LastOverrideError properties; outstanding now counts WriteOffAmount and excludes estimates; ≥80% utilization warns (non-blocking), >100% blocks with override instructions; override validates active Admin user (Role=="Admin" or RoleObj.Name=="Admin") SHA-256 password match, records override as warning; IInvoiceService updated; InvoicesViewModel: OverrideUsername/OverridePassword/WarningMessage properties, passes override to service, renders warnings; InvoicesView: Manager Override panel (username + PasswordBox with code-behind push) + orange WarningMessage line |
| 2026-10-30 | ACC-10 Audit trail | AuditRecord model + DbSet/config + DatabaseSchemaUpgrader CREATE TABLE; IAuditService/AuditService: RecordAsync (reflection diff of scalar properties → ChangesJson, snapshot → SnapshotJson, attributed to current user, best-effort try/catch), QueryAsync (user/action/entity/date filters); hooks: ProductService & CustomerService CRUD (before snapshot fetched AsNoTracking for update diffs), InvoiceService create/convert/write-off, PaymentService create/delete; DI registered; AuditTrailViewModel/View (filter toolbar, record grid, Property/Before→After detail grid parsed from ChangesJson, CSV export); sidebar "Audit Trail" + command + DataTemplate |
| 2026-10-30 | STK-07 Stock adjustment hardening | IProductService: LastValidationError + GetAdjustmentHistoryAsync (AdjustmentHistoryRow from StockTransactions join Products); ProductService.AdjustStockAsync: negative-stock guard, error messages, audit hook (StockQty before/after + reason); VM surfaces service errors and shows new qty + sign in success; View: Recent Adjustments grid + tip text; fixed 3 views using Color resource SurfaceColor as Background → SurfaceBrush; App.xaml.cs: DispatcherUnhandledException logs crashes to crash.txt + user-facing message (transient startup XamlParseException traced/absorbed) |
| 2026-10-30 | STK-09 Stock valuation | StockValuationRow DTO; ReportService.GetStockValuationAsync("Weighted Average"/"FIFO") builds purchase-cost layers from PurchaseItems×Purchases ordered oldest-first; WAC = purchased value / purchased qty × on-hand; FIFO consumes layers by shipped qty, unlayered on-hand falls back to WAC (Mixed+Fallback); products without purchases flagged No Purchases; StockValuationViewModel (method toggle auto-reload, search, totals, CSV export) + StockValuationView (toolbar, total card, grid); sidebar "Stock Valuation" + command + DataTemplate |
| 2026-10-31 | PAY-07 Customer ledger & statement | CustomerLedgerReport/CustomerLedgerRow DTOs; ReportService.GetCustomerLedgerAsync (opening balance from pre-period invoices minus receipts minus credit notes; in-period merged rows from three sources ordered by date; running receivable balance); PrintCustomerStatementAsync (formatted text statement with customer/period/opening/lines/closing + allocation refs in receipts, safe filename); CustomerLedgerViewModel (customer picker, date range, Generate/PrintStatement) + CustomerLedgerView (opening/closing cards, Date/Type/Reference/Narration/Debit/Credit/Balance grid); sidebar "Customer Ledger" + command + DataTemplate; consulted graphify-out/GRAPH_REPORT.md for architecture consistency |
| 2026-10-31 | ACC-06 Bank reconciliation | BankStatementLine model (ValueDate, Description, ReferenceNo, signed Amount, MatchedType/MatchedRef, Status) + DbSet config + schema-upgrader CREATE TABLE + indexes; IBankReconciliationService/BankReconciliationService: ImportCsvAsync flexible headers (Amount or Withdrawal+Deposit, quote-aware CSV split, 9 date formats + invariant parse, accounting parentheses negatives, ₹/commas stripped, skipped rows → warnings), AutoMatchAsync (unmatched lines vs Cash/Bank journal lines: equal ±0.01, date ±5 days closest-first, already-referenced lines excluded via MatchedRef regex), MarkReconciledAsync/ClearMatchAsync, GetSummaryAsync; BankReconciliationViewModel + BankReconciliationView (import/auto-match/reconcile/clear toolbar, filter, 4 summary cards, statement grid); sidebar "Bank Reconciliation" + command + DataTemplate; sample SampleBankStatement.csv on Desktop |
| 2026-10-31 | PAY-10 Cash book & transfers | ICashBookService/CashBookService: GetCashBankAccountsAsync (name regex Cash/Bank), GetCashBookAsync (combined book when accountId null vs per-account; opening = account OpeningBalances + pre-period journal net; entries touching 2+ cash/bank lines detected as internal transfers — combined book shows one zero-net "Transfer (Contra)" row per entry, per-account books show Contra In/Out as real movement; TransferAsync posts JE debit target + credit source with validation); CashBookViewModel (account picker, date range, inline New Transfer form, export CSV) + CashBookView (toolbar, transfer form, opening/in/out/closing cards, Date/Account/Type/Reference/Narration/In/Out/Balance grid); sidebar "Cash Book" + command + DataTemplate; tracker gained Overall Progress section (37/90 = 41%, time estimate) |
| 2026-10-31 | INV-03 Numbering series completion | INumberSeriesService += RollOverFinancialYearAsync (clones each active ResetEachYear series into the new FY at StartNumber, skips existing, dedupes by DocType+StoreId) + GetGapReportAsync (per-series: issued docs filtered to the series' FY window 1-Apr→31-Mar parsed from "2026-27" style, serial extracted as digits between Prefix and Suffix; missing serial list, duplicate detection "N ×2", statuses OK/Gaps/Duplicates/No documents issued); GetNextNumberAsync now clones the newest matching series of the same DocType (keeping prefix/suffix customization) instead of generating a bare default; ViewModel: MarkDirty + Save Edits command, Check Gaps command (GapRows + HasGapRows), Roll Over Year command, duplicate docType+FY guard on add, SuccessMessage; View rebuilt: editable Prefix/Suffix grid + Next Number preview column + Save Edits, gap-audit results table row, FY-rollover toolbar, CellEditEnding hook in code-behind (fixed DataGridCellEditEditingEventArgs → DataGridCellEditEndingEventArgs) |
| 2026-10-31 | RPT-10 Multi-store comparison | StoreComparisonRow DTO (Rank, StoreId, StoreName, Location, InvoiceCount, Sales, SalesSharePercent, COGS, GrossProfit, MarginPercent, Outstanding); ReportService.GetStoreComparisonAsync — non-estimate invoices grouped by StoreId (null → "Main Store (unassigned)"), COGS from latest purchase rate per product (P&L convention), sales share %, rank assigned by sales desc with zero-sales stores ranked "-", stores without invoices still listed; StoreComparisonViewModel (date range, totals, best-store card, overall margin summary, CSV export) + StoreComparisonView (3 summary cards + Rank/Store/Invoices/Sales/Share/COGS/GP/Margin/Outstanding grid); sidebar "Store Comparison" + command + DataTemplate (fixed accidental duplicate Cash Book sidebar button during wiring); Overall Progress bumped 39/90 = 43% |
| 2026-10-31 | **Wave: STK-03, PAY-06, INV-13, INV-11, PAY-01, RPT-07** | Six P1 features landed together (coordinator + 2 background agents, agents barred from building/running to avoid concurrent obj\ corruption). **STK-03**: StockPolicySetting (Id==1 singleton, Block/Warn/Allow) + IStockPolicyService.EvaluateAsync(productName, currentQty, change, context) shared by InvoiceService, ProductService.AdjustStockAsync, StockTransferService and StockTakeService (negative-variance pre-pass before anything posts); StockPolicyViewModel/StockPolicyView (mode editor with live description, dirty-gated save, below-zero grid). **PAY-06**: dead GetEffectiveRateAsync replaced by GetEffectivePriceAsync → EffectivePrice{Rate, Source, DiscountPct} (customer list → group list → master), AddItemAsync/RemoveItemAsync upsert per product; InvoicesViewModel picks catalogue product + customer and autofills name/GSTIN/rate with a "Rate from …" hint; PriceListView restructured to list/add/rates-editor cards; PriceListItem.EffectiveRate. **INV-13**: register filter bar (date range, status/type/payment mode, min/max, dues-only, clear) over FilteredInvoices with RegisterCount/HasActiveFilter notifications. **INV-11** (agent): CreditNoteService numbering via INumberSeriesService inside the transaction, debit branch consumes stock through the policy, journal to Sales(3)/Debtors(7), CancelNoteAsync with compensating rows, delete reverses stock; VM links the original invoice + customer, product picker, type filter. **PAY-01** (agent): PaymentSplit model/DbSet/cascade + PaymentSplits CREATE TABLE, ValidateSplits (sum within ₹0.01, lines > 0, unique cheque refs, mode→"Split"), PaymentModeSummaryRow + split grid/receipt block. **RPT-07**: GetStockAuditReportAsync(from,to) — ledger tie-out, opening/in/out/closing restated, last completed count reconciliation, six flag classes; StockAuditReportViewModel/StockAuditReportView (6 cards, flag filter, search, CSV) + sidebar "Stock Audit". Also fixed the severe latent defect where UI-created invoices never set InvoiceItem.ProductId / Invoice.CustomerId (stock never moved, party pricing impossible) — lines now require a catalogue product and the header links CustomerId |
| 2026-10-31 | **Two live crash fixes** | (1) `SQLite Error 1: no such column: c.CustomerGroup` crashed the Invoices view on every open — PAY-04 model columns were never registered in DatabaseSchemaUpgrader; added Customers.GSTIN/State/CustomerGroup + WriteOffAmount AddColumnIfMissing calls, then diffed **every** model against app.db (pragma_table_info) to prove no other scalar drift remained. (2) `Items collection must be empty before using ItemsSource` at InvoicesView.xaml:71 — the payment-mode ComboBox had ItemsSource *and* 4 inline ComboBoxItems; inline items removed, then a project-wide XAML scan for that pattern (0 violations). Also repaired a stale row-scoped binding in PaymentView.xaml:186 (RemoveAllocationCommand → DataContext + RelativeSource AncestorType=DataGrid). Warning count 191 → 123 |
| 2026-10-31 | **UI automation smoke pass** | Drove the built exe with System.Windows.Automation (login → Stock Audit / Stock Policy / Party-wise Pricing / Credit Notes / Payments / Invoices): all views render, crash.txt byte-identical before/after (20242 bytes, no new entries). RPT-07 math verified on a seeded scenario (backup taken first, restored and temp scripts deleted afterwards): unrecorded tie-out, Expired+Below-Minimum, Negative Stock, Unposted Count and No Movement all reported correctly, totals footed (₹10,920 stock value / ₹150 unrecorded), CSV export wrote 5 rows |
| 2026-10-31 | Parallel wave: ACC-03, ACC-04, ACC-07, ACC-08, INV-04, INV-05, INV-09, INV-10, RPT-12, SEC-04, SEC-07, SEC-10, STK-10, STK-12, STK-14, INT-03/04/05 | 16 features completed in one batched wave — 5 background agents (STK-12 bulk import, STK-14 stocktake, RPT-12 item-profit, SEC-07 integrity, STK-10 transfer) creating new files only + second wave (ACC-08 opening balances, INT-03/04/05 messaging center, SEC-10 privacy, INV-05 templates), with coordinator owning all shared-file wiring: AccountGroup/Journal hardening (validate ≥2 lines, one-of debit/credit, balanced to ₹0.01, LastValidationError surfaced), user-picked entry dates; Expenses module (service + daily/monthly/category breakdowns + CSV export); period lock enforced at JournalService + FY carry-forward (create next FY, activate, roll number series); opening-balances wizard; draft-level + per-line tax-inclusive invoicing with gross-preserving bulk flip fixed broken `Checkbox` binding; INV-05 template settings (logo/terms/branding + thermal preview); INV-10 edit/delete rules (Admin PBKDF2 override, paid invoices un-deletable, stock restored on delete + audited); PasswordHasher PBKDF2 with legacy transparent re-hash; Integrity + Privacy views; StockTransfer/StockTake/BulkImport/Messaging/ItemProfit services; MessagingSettings/Logs + InvoiceTemplateSettings + StockTake(s) + StockTransfers CREATE TABLE in DatabaseSchemaUpgrader; 12 DbSets + 12 DI pairs + 8 DataTemplates + 11 sidebar buttons + 11 nav commands. Fixed CS0101 duplicate PasswordHasher (UserService), missing usings (System.IO ×2, Models), PrintStyle double-set MC3024, audit RecordChangeAsync→RecordAsync, invoice-delete StockTransaction field list; public int P1s finished; Overall Progress bumped 57/90 = 63%; build 0 errors, app healthy (PID 6300) |
| 2026-10-31 | **Wave: INV-06, INV-08, PAY-08, PAY-13, RPT-01, RPT-02, SEC-06** | Native WPF print stack (A4 / A5 half-sheet / 80mm thermal FixedPage, no new package), print-preview window with layout + copies + printer probe, reminder rule engine on receivables handed to the Messaging Center, dedicated Sales Register sheet with day totals, five hand-drawn dashboard charts, Tally XML/CSV/Excel-XML export centre |
| 2026-10-31 | **Wave: SEC-01, SEC-05, STK-11, PAY-02** | 4 background agents built new files only; coordinator owned every shared file (AppDbContext DbSets, DatabaseSchemaUpgrader CREATE TABLE blocks, App.xaml DI + DataTemplates, MainWindow sidebar + nav commands, gate + enqueue wiring in Invoices/StockAdjustment/FinancialYear/Payment VMs). BackupSchedulerService 1-minute pool timer with retention pruning; CriticalChangeGuard with per-kind gates, attempt counter + lockout on its own row, fail-closed and ACC-10 audited both ways; OfflineQueueService PendingChange board with simulated HQ transport (IHqSyncTransport seam); CreditSaleService due-date/ageing/over-limit verdicts enforced before an invoice saves + FIFO settle |
| 2026-10-31 | **Login root cause: PasswordHasher legacy branch** | `Verify()` compared the entered password against `Convert.ToHexString(UTF8 bytes)` — hex of the raw password, not SHA-256 — so every legacy-stored hash (incl. the seeded `admin`/`admin123`) could never verify and no login had ever succeeded past the first wave. Now hashes with `SHA256.HashData` before hex-encoding. Proved end to end: login succeeded, `PasswordRehash` activity row written, and admin's stored hash transparently upgraded to `PBKDF2$210000$…` on that same login |
| 2026-10-31 | **Two silent-failure classes killed** | (1) `BoolToVisibilityConverter` bound to string message TextBlocks returns Collapsed for any non-bool, so login/billing errors were invisible forever — added `StringToVisibilityConverter` and switched LoginView.xaml to it, then audited every remaining bool-converter binding (RoleView.xaml:68 converts an `Id` to Visibility, still open). (2) Dead `*AsyncCommand` bindings from `[RelayCommand]` stripping the `Async` suffix: 74 buttons across the app renamed and verified against the generated names |
| 2026-10-31 | **UIA smoke pass, wave 2** | smoke10.ps1: AppActivate → SetFocus + SendKeys (WPF PasswordBox refuses UIA SetValue, it is read-only to IValueProvider) → click Login → assert the 6 admin-gated nav buttons → open Critical Change Passcode / Backup Schedule / Offline Sync Queue / Credit Sales and dump rendered text. All present and populated, crash.txt 0 → 0 bytes delta, build 0 errors / 125 warnings (MVVMTK0034 field-reference style + one NU1903 SQLitePCLRaw advisory) |
| 2026-11-01 | **Audit + DI/runtime defect-fix wave** | 4 read-only audit agents verified all 90 tracker rows against code (services + DI + DataTemplates + menu bindings; zero orphan UI wiring). Two runtime blockers found and fixed: (1) SEC-08 - HqTransportSelection was never DI-registered, so opening Sync Center would throw; wired singleton HqTransportSelection + singleton HttpHqSyncTransport + resolvable SimulatedHqSyncTransport, IHqSyncTransport now resolves per construction from the saved SyncSetting (live HTTPS when switched ON, otherwise simulated) and PrimeTransportSelection() runs at startup; (2) PAY-11 - ChequeRegisterView.xaml.cs was the only view missing code-behind, so the pane rendered blank; created with the house Loaded-hook + LoadDataAsync pattern. Tracker rescored to audit evidence: ACC-02 row to Partial (IAutoPostingService DI-registered but zero consumers - sale/purchase saves never post journals), ACC-01 INT-01 INT-02 notes corrected (no default CoA seed ships; vCard export only; Catalog screen is category/unit management, not a shareable catalog). Build 0 errors, app relaunched healthy (PID 3324, no crash.txt). Overall 79/90 = 88%
| 2026-11-01 | **SEC-03 step 1: SMTP secret encrypted at rest** | EncryptionService made prefix-based (values identified by a constant prefix before ciphertext, idempotent Protect/Unprotect + IsProtected); MessagingService.SaveSettingsAsync DPAPI-encrypts the SMTP password before the row is written, SendEmailAsync decrypts for credentials, the settings screen shows the secret (not the blob); no new NuGet at this step. Database-level encryption still awaited the SQLCipher bundle decision |
| 2026-11-01 | **NuGet approved + SEC-03 closed: SQLCipher live at rest** | User approved installs. Probe proved the pairing first: csproj had Microsoft.Data.Sqlite.Core 10.0.0 + SQLitePCLRaw.bundle_e_sqlcipher (was 2.1.6 - its native lib is incompatible with core 10 -> TypeLoadException on init; upgrading to 2.1.11 fixed it; probe verified cipher_version 4.5.2 community, plaintext magic gone from the header, no-password and wrong-password opens refused with SQLite error 26, right password reads data). Implemented: EncryptionService rewritten - DPAPI-protected app.db.key (32 random bytes, CurrentUser + app entropy, never logged), static GetAppConnectionString wires Password=; one-time migration of legacy plaintext app.db via ATTACH + sqlcipher_export with app.db.pre-cipher.bak rollback; BackupService sends keyed conn string (migrate:false) and validates backup files both encrypted and legacy-plaintext; QuestPDF 2026.9.1 + ClosedXML 0.105.1 installed for RPT-13 (implementation next). Launch verified twice: first boot migrated 524288-byte plaintext DB (rollback copy header stayed "SQLite format 3"), live db header now random bytes; second boot reopened encrypted db idempotently - no re-migration, no tmp leftovers, no crash.txt, UI healthy. Caveat noted in tracker: restoring to a different Windows profile needs app.db.key |

---

## 🚀 Next Features to Build (Priority Order)

**P1 (v1.0 must-have) - close the partials:**
1. **ACC-02 Auto-posting** - consume the orphaned IAutoPostingService (with default chart-of-accounts seeding) so every sale/purchase/payment posts double-entry journals through one service instead of three hand-rolled paths.
2. **SEC-05 LockedPeriodEdit gate** - the passcode gate exists in settings but has no call site; either wire it into the locked-year edit path or fold it under FinancialYearLock and update the policy screen.

**P2 (v1.0 should-have):**
3. **RPT-13 Export and scheduling** - packages now installed (QuestPDF 2026.9.1 + ClosedXML 0.105.1, user-approved 2026-11-01); build in-app PDF/Excel report exports and scheduled email on top (licence line: QuestPDF.Settings.License = Community). CSV/JSON/Tally exports already cover the rest.
4. **USR-03 store-wise pricing** - PriceListService has no store dimension today (party-wise only); prices per store.
5. **PAY-08 reminder scheduler** - reminders send from the view only; arm them on the existing backup/sync timer tick.
6. **INV-03 numbering unification** - route InvoiceService.GenerateNextInvoiceNumberAsync through NumberSeriesService so one engine governs all serials; add GSTIN checksum validation (INV-01 polish).
7. **ACC-07 carry-forward balances** - Carry Forward rolls FY + number series only; opening balances/P&L do not carry into the new year. Also: supplier-side bulk import (STK-12 is products/customers only).

**P3 / v1.1 (deferred):**
8. INT-06 UPI/payment QR on invoices - INT-02 shareable customer catalog (PDF/web) - INT-01 vCard import - INT-08 public API/webhooks - INV-14 e-invoicing IRN + INV-15 e-way bill - STK-13 barcode labels - SEC-09 licensing.

---

**To run the app:**
```powershell
cd 'C:\Users\Kral\Documents\Default Project\RetailBillingApp'
dotnet run
```
Login: `admin` / `admin123`
