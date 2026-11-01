using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class PLReportView : UserControl
{
    public PLReportView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.PLReportViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
