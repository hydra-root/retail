using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class StockTakeView : UserControl
{
    public StockTakeView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.StockTakeViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
