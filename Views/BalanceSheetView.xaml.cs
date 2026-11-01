using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class BalanceSheetView : UserControl
{
    public BalanceSheetView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.BalanceSheetViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
