using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class StockTransferView : UserControl
{
    public StockTransferView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.StockTransferViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
