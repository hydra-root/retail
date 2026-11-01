using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class StockAuditReportView : UserControl
{
    public StockAuditReportView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.StockAuditReportViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
