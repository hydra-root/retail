using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.DashboardViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
