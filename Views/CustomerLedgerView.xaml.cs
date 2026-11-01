using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class CustomerLedgerView : UserControl
{
    public CustomerLedgerView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.CustomerLedgerViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
