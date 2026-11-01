using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using RetailBillingApp.Services;

namespace RetailBillingApp.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private ObservableObject _currentViewModel = null!;

    [ObservableProperty]
    private bool _isAdmin;

    /// <summary>UI-01: the category bar and Logout must not be clickable while the login card is up.</summary>
    [ObservableProperty]
    private bool _isNavVisible;

    public ObservableObject CurrentViewModel
    {
        get => _currentViewModel;
        set => SetProperty(ref _currentViewModel, value);
    }

    public MainWindowViewModel(IAuthService authService)
    {
        _authService = authService;

        if (_authService.IsLoggedIn)
        {
            ShowDashboard();
        }
        else
        {
            ShowLogin();
        }
    }

    [RelayCommand]
    private void ShowLogin()
    {
        IsNavVisible = false;
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<LoginViewModel>();
    }

    [RelayCommand]
    public void ShowDashboard()
    {
        IsAdmin = _authService.IsAdmin;
        IsNavVisible = true;
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<DashboardViewModel>();
    }

    [RelayCommand]
    private void ShowProducts()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<ProductsViewModel>();
    }

    [RelayCommand]
    private void ShowInvoices()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<InvoicesViewModel>();
    }

    [RelayCommand]
    private void ShowQuickPos()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<QuickPosViewModel>();
    }

    [RelayCommand]
    private void ShowSales()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<SalesViewModel>();
    }

    [RelayCommand]
    private void ShowReports()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<ReportsViewModel>();
    }

    [RelayCommand]
    private void ShowPLReport()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<PLReportViewModel>();
    }

    [RelayCommand]
    private void ShowBalanceSheet()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<BalanceSheetViewModel>();
    }

    [RelayCommand]
    private void ShowCashFlow()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<CashFlowViewModel>();
    }

    [RelayCommand]
    private void ShowPayables()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<PayablesViewModel>();
    }

    [RelayCommand]
    private void ShowCreditAnalysis()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<CreditAnalysisViewModel>();
    }

    [RelayCommand]
    private void ShowSettings()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<SettingsViewModel>();
    }

    [RelayCommand]
    private void ShowSuppliers()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<SupplierViewModel>();
    }

    [RelayCommand]
    private void ShowCustomers()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<CustomerViewModel>();
    }

    [RelayCommand]
    private void ShowPurchases()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<PurchaseViewModel>();
    }

    [RelayCommand]
    private void ShowChartOfAccounts()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<ChartOfAccountsViewModel>();
    }

    [RelayCommand]
    private void ShowJournal()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<JournalViewModel>();
    }

    [RelayCommand]
    private void ShowCreditNotes()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<CreditNoteViewModel>();
    }

    [RelayCommand]
    private void ShowRoles()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<RoleViewModel>();
    }

    [RelayCommand]
    private void ShowPermissions()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<PermissionViewModel>();
    }

    [RelayCommand]
    private void ShowStores()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<StoreViewModel>();
    }

    [RelayCommand]
    private void ShowUsers()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<UsersViewModel>();
    }

    [RelayCommand]
    private void ShowActivityLog()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<ActivityLogViewModel>();
    }

    [RelayCommand]
    private void ShowMultiStore()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<MultiStoreViewModel>();
    }

    [RelayCommand]
    private void ShowNumberSeries()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<NumberSeriesViewModel>();
    }

    [RelayCommand]
    private void ShowStockAdjustment()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<StockAdjustmentViewModel>();
    }

    [RelayCommand]
    private void ShowPayments()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<PaymentViewModel>();
    }

    [RelayCommand]
    private void ShowPriceList()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<PriceListViewModel>();
    }

    [RelayCommand]
    private void ShowFinancialYear()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<FinancialYearViewModel>();
    }

    [RelayCommand]
    private void ShowGstReport()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<GstReportViewModel>();
    }

    [RelayCommand]
    private void ShowStockAlerts()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<StockAlertViewModel>();
    }

    [RelayCommand]
    private void ShowBatches()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<BatchViewModel>();
    }

    [RelayCommand]
    private void ShowCatalog()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<CatalogViewModel>();
    }

    [RelayCommand]
    private void ShowLedgerBook()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<LedgerBookViewModel>();
    }

    [RelayCommand]
    private void ShowReceivables()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<ReceivablesViewModel>();
    }

    [RelayCommand]
    private void ShowAuditTrail()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<AuditTrailViewModel>();
    }

    [RelayCommand]
    private void ShowStockValuation()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<StockValuationViewModel>();
    }

    [RelayCommand]
    private void ShowStockAuditReport()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<StockAuditReportViewModel>();
    }

    [RelayCommand]
    private void ShowCustomerLedger()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<CustomerLedgerViewModel>();
    }

    [RelayCommand]
    private void ShowBankReconciliation()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<BankReconciliationViewModel>();
    }

    [RelayCommand]
    private void ShowCashBook()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<CashBookViewModel>();
    }

    [RelayCommand]
    private void ShowStoreComparison()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<StoreComparisonViewModel>();
    }

    [RelayCommand]
    private void ShowExpense()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<ExpenseViewModel>();
    }

    [RelayCommand]
    private void ShowStockTake()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<StockTakeViewModel>();
    }

    [RelayCommand]
    private void ShowStockTransfer()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<StockTransferViewModel>();
    }

    [RelayCommand]
    private void ShowStockPolicy()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<StockPolicyViewModel>();
    }

    [RelayCommand]
    private void ShowCriticalChangePasscode()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<CriticalChangePolicyViewModel>();
    }

    [RelayCommand]
    private void ShowOfflineSync()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<OfflineSyncViewModel>();
    }

    [RelayCommand]
    private void ShowCreditSales()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<CreditSalesViewModel>();
    }

    [RelayCommand]
    private void ShowIntegrity()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<IntegrityViewModel>();
    }

    [RelayCommand]
    private void ShowItemProfit()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<ItemProfitViewModel>();
    }

    [RelayCommand]
    private void ShowBulkImport()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<BulkImportViewModel>();
    }

    [RelayCommand]
    private void ShowOpeningBalance()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<OpeningBalanceViewModel>();
    }

    [RelayCommand]
    private void ShowPrivacy()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<PrivacyViewModel>();
    }

    [RelayCommand]
    private void ShowMessaging()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<MessagingViewModel>();
    }

    [RelayCommand]
    private void ShowTemplateSettings()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<TemplateSettingsViewModel>();
    }

    [RelayCommand]
    private void ShowSalesRegister()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<SalesRegisterViewModel>();
    }

    [RelayCommand]
    private void ShowReminders()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<ReminderViewModel>();
    }

    [RelayCommand]
    private void ShowBackupSchedule()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<BackupScheduleViewModel>();
    }

    [RelayCommand]
    private void ShowTallyExport()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<TallyExportViewModel>();
    }

    [RelayCommand]
    private void ShowChequeRegister()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<ChequeRegisterViewModel>();
    }

    [RelayCommand]
    private void ShowStorePricing()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<StorePricingViewModel>();
    }

    [RelayCommand]
    private void ShowContacts()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<ContactsViewModel>();
    }

    [RelayCommand]
    private void ShowSyncCenter()
    {
        CurrentViewModel = App.ServiceProvider!.GetRequiredService<SyncCenterViewModel>();
    }

    [RelayCommand]
    private void Logout()
    {
        _authService.Logout();
        ShowLogin();
    }
}

