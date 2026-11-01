using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class StockPolicyView : UserControl
{
    public StockPolicyView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.StockPolicyViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
