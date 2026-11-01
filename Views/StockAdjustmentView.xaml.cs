using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class StockAdjustmentView : UserControl
{
    public StockAdjustmentView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.StockAdjustmentViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
