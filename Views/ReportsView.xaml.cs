using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.ReportsViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
