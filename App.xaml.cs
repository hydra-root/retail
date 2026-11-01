using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailBillingApp.Data;
using RetailBillingApp.Services;
using RetailBillingApp.ViewModels;
using System.IO;
using System.Windows;

namespace RetailBillingApp;

public partial class App : Application
{
    public static IServiceProvider? ServiceProvider { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global safety net: log render/UI thread crashes to crash.txt next to the exe
        DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                File.AppendAllText("crash.txt",
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} UI: {args.Exception}\n\n");
            }
            catch { /* ignore */ }
            MessageBox.Show($"Unexpected error: {args.Exception.Message}\n\nDetails written to crash.txt",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var services = new ServiceCollection();

        // RPT-13: QuestPDF community licence must be set before the first document render
        QuestPDF.Settings.License = QuestPDF.LicenseType.Community;

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(EncryptionService.GetAppConnectionString())); // SEC-03: SQLCipher at rest + one-time plaintext migration

        services.AddTransient<IDatabaseService, DatabaseService>();
        services.AddTransient<IProductService, ProductService>();
        services.AddTransient<ICustomerService, CustomerService>();
        services.AddTransient<IInvoiceService, InvoiceService>();
        services.AddTransient<ISaleService, SaleService>();
        services.AddTransient<IReportService, ReportService>();
        services.AddTransient<IBankReconciliationService, BankReconciliationService>();
        services.AddTransient<ICashBookService, CashBookService>();
        services.AddTransient<IExpenseService, ExpenseService>();
        services.AddTransient<IStockTakeService, StockTakeService>();
        services.AddTransient<IStockTransferService, StockTransferService>();
        services.AddTransient<IIntegrityService, IntegrityService>();
        services.AddTransient<IItemProfitService, ItemProfitService>();
        services.AddTransient<IBulkImportService, BulkImportService>();
        services.AddTransient<IOpeningBalanceService, OpeningBalanceService>();
        services.AddTransient<IPrivacyService, PrivacyService>();
        services.AddTransient<IMessagingService, MessagingService>();
        services.AddTransient<ITemplateService, TemplateService>();
        services.AddTransient<IBackupService, BackupService>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddTransient<ISupplierService, SupplierService>();
        services.AddTransient<IPurchaseService, PurchaseService>();
        services.AddTransient<IAccountGroupService, AccountGroupService>();
        services.AddTransient<IAccountService, AccountService>();
        services.AddTransient<IJournalService, JournalService>();
        services.AddTransient<IAutoPostingService, AutoPostingService>();
        services.AddTransient<ICreditNoteService, CreditNoteService>();
        services.AddTransient<IRoleService, RoleService>();
        services.AddTransient<IPermissionService, PermissionService>();
        services.AddTransient<IRolePermissionService, RolePermissionService>();
        services.AddTransient<INumberSeriesService, NumberSeriesService>();
        services.AddTransient<IPaymentService, PaymentService>();
        services.AddTransient<IPriceListService, PriceListService>();
        // USR-03: store-wise pricing sits in front of PAY-06's party lists and falls back to them.
        services.AddTransient<IStorePricingService, StorePricingService>();
        // RPT-13: in-app PDF/Excel report exports + scheduled report email
        services.AddTransient<IReportExportService, ReportExportService>();
        // RPT-13: long-lived 1-minute timer like SEC-01; opens its own DI scope per run
        services.AddSingleton<IScheduledReportService, ScheduledReportService>();
        services.AddTransient<IFinancialYearService, FinancialYearService>();
        services.AddTransient<IEncryptionService, EncryptionService>();
        services.AddTransient<IStoreService, StoreService>();
        services.AddTransient<IUserService, UserService>();
        services.AddTransient<IActivityLogService, ActivityLogService>();
        services.AddTransient<IStockPolicyService, StockPolicyService>();

        services.AddTransient<IAuditService, AuditService>();
        // SEC-01: the timer outlives every view, so it is a singleton; it opens its own
        // DI scope per run rather than capturing the scoped AppDbContext.
        services.AddSingleton<IBackupSchedulerService, BackupSchedulerService>();
        services.AddTransient<IReminderService, ReminderService>();
        // SEC-05: separate shop passcode that gates deletes, write-offs, period close, etc.
        services.AddTransient<ICriticalChangeGuard, CriticalChangeGuard>();
        services.AddTransient<CriticalChangePolicyViewModel>();
        // STK-11/SEC-08: local-first outbox with a selectable HQ transport. The queue holds an
        // IHqSyncTransport that is re-resolved each time a queue or Sync Center screen is built:
        // when the saved SyncSetting has live sync switched ON (and a usable base URL), the
        // container hands out the real HTTPS transport; otherwise the built-in simulated endpoint
        // replays the queue offline. HqTransportSelection is the synchronous in-memory mirror of
        // that choice — primed at startup below, refreshed again by both transports on every call.
        services.AddSingleton<HqTransportSelection>();
        // The HTTP transport must be long-lived: one pooled HttpClient and no captive DbContext —
        // it re-reads connection settings through its own DI scope per call.
        services.AddSingleton<HttpHqSyncTransport>();
        services.AddTransient<SimulatedHqSyncTransport>();
        services.AddTransient<IHqSyncTransport>(sp => sp.GetRequiredService<HqTransportSelection>().IsLive
            ? sp.GetRequiredService<HttpHqSyncTransport>()
            : sp.GetRequiredService<SimulatedHqSyncTransport>());
        services.AddTransient<IOfflineQueueService, OfflineQueueService>();
        services.AddTransient<OfflineSyncViewModel>();
        // PAY-02: credit-sale policy, per-invoice credit ledger and FIFO settle.
        services.AddTransient<ICreditSaleService, CreditSaleService>();
        services.AddTransient<CreditSalesViewModel>();
        // INV-12: Quick POS till. Settle still routes through IInvoiceService (GST/stock/PAY-05).
        services.AddTransient<IQuickPosService, QuickPosService>();
        services.AddTransient<QuickPosViewModel>();
        services.AddTransient<IInvoicePrintService, InvoicePrintService>();
        services.AddTransient<ITallyExportService, TallyExportService>();
        services.AddTransient<TallyExportViewModel>();
        services.AddTransient<InvoicePrintPreviewViewModel>();
        services.AddTransient<BackupScheduleViewModel>();
        services.AddTransient<ReminderViewModel>();
        // PAY-11: cheque register (post-dated lifecycle, auto-receipt on clear)
        services.AddTransient<IChequeService, ChequeService>();
        services.AddTransient<ChequeRegisterViewModel>();
        // INT-01: unified contacts (customers + suppliers, vCard/CSV)
        services.AddTransient<IContactService, ContactService>();
        services.AddTransient<ContactsViewModel>();
        // SEC-08/STK-11: HQ sync center (transport selection + run log)
        services.AddTransient<ISyncCenterService, SyncCenterService>();
        services.AddTransient<SyncCenterViewModel>();
        services.AddTransient<SalesRegisterViewModel>();
        services.AddTransient<AuditTrailViewModel>();
        services.AddTransient<CustomerViewModel>();
        services.AddTransient<PLReportViewModel>();
        services.AddTransient<BalanceSheetViewModel>();
        services.AddTransient<CashFlowViewModel>();
        services.AddTransient<PayablesViewModel>();

        services.AddTransient<LedgerBookViewModel>();
        services.AddTransient<ReceivablesViewModel>();
        services.AddTransient<StockValuationViewModel>();
        services.AddTransient<StockAuditReportViewModel>();
        services.AddTransient<CustomerLedgerViewModel>();
        services.AddTransient<BankReconciliationViewModel>();
        services.AddTransient<CashBookViewModel>();
        services.AddTransient<StoreComparisonViewModel>();
        services.AddTransient<ExpenseViewModel>();
        services.AddTransient<StockTakeViewModel>();
        services.AddTransient<StockTransferViewModel>();
        services.AddTransient<StockPolicyViewModel>();
        services.AddTransient<IntegrityViewModel>();
        services.AddTransient<ItemProfitViewModel>();
        services.AddTransient<BulkImportViewModel>();
        services.AddTransient<OpeningBalanceViewModel>();
        services.AddTransient<PrivacyViewModel>();
        services.AddTransient<MessagingViewModel>();
        services.AddTransient<TemplateSettingsViewModel>();
        services.AddTransient<CreditAnalysisViewModel>();
        services.AddTransient<ICategoryService, CategoryService>();
        services.AddTransient<IUnitService, UnitService>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ProductsViewModel>();
        services.AddTransient<InvoicesViewModel>();
        services.AddTransient<SalesViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SupplierViewModel>();
        services.AddTransient<PurchaseViewModel>();
        services.AddTransient<ChartOfAccountsViewModel>();
        services.AddTransient<JournalViewModel>();
        services.AddTransient<CreditNoteViewModel>();
        services.AddTransient<RoleViewModel>();
        services.AddTransient<PermissionViewModel>();
        services.AddTransient<NumberSeriesViewModel>();
        services.AddTransient<StockAdjustmentViewModel>();
        services.AddTransient<PaymentViewModel>();
        services.AddTransient<PriceListViewModel>();
        services.AddTransient<FinancialYearViewModel>();
        services.AddTransient<GstReportViewModel>();
        services.AddTransient<StoreViewModel>();
        services.AddTransient<UsersViewModel>();
        services.AddTransient<ActivityLogViewModel>();
        services.AddTransient<MultiStoreViewModel>();
        services.AddTransient<StockAlertViewModel>();
        services.AddTransient<BatchViewModel>();
        services.AddTransient<CatalogViewModel>();
        services.AddTransient<StorePricingViewModel>();
        services.AddTransient<IBatchService, BatchService>();

        ServiceProvider = services.BuildServiceProvider();

        try
        {
            using var scope = ServiceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.Database.EnsureCreated();
            DatabaseSchemaUpgrader.Apply(dbContext);

            // ACC-02: default chart of accounts so auto-posting always finds its accounts
            ChartOfAccountsSeeder.SeedAsync(dbContext).GetAwaiter().GetResult();

            var authService = ServiceProvider.GetRequiredService<IAuthService>();
            authService.SeedDefaultAdmin("admin", "admin123");

            // SEC-08: fill the transport-selection mirror from the saved SyncSetting so the queue
            // and the Sync Center agree on simulated-vs-live from the very first run.
            ServiceProvider.GetRequiredService<ISyncCenterService>().PrimeTransportSelection();

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            mainWindow.Activate();

            // SEC-01: arm the automatic backup timer once the window is up, then let it
            // collapse any slots missed while the app was closed into one catch-up run.
            var backupScheduler = ServiceProvider.GetRequiredService<IBackupSchedulerService>();
            backupScheduler.Start();
            _ = backupScheduler.RunIfDueAsync();

            // RPT-13: arm the scheduled-report email timer alongside the backup scheduler
            var scheduledReports = ServiceProvider.GetRequiredService<IScheduledReportService>();
            scheduledReports.Start();
            _ = scheduledReports.RunIfDueAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Application startup error: {ex.Message}\n\n{ex.InnerException?.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown();
        }
    }
}
