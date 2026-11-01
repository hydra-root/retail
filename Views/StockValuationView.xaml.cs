using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class StockValuationView : UserControl
{
    public StockValuationView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.StockValuationViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
