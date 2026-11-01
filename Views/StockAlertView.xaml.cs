using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class StockAlertView : UserControl
{
    public StockAlertView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.StockAlertViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
