using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class ChartOfAccountsView : UserControl
{
    public ChartOfAccountsView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.ChartOfAccountsViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
