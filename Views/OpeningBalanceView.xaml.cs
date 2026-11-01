using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class OpeningBalanceView : UserControl
{
    public OpeningBalanceView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.OpeningBalanceViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
