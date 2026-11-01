using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class GstReportView : UserControl
{
    public GstReportView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.GstReportViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
