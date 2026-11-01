using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class LedgerBookView : UserControl
{
    public LedgerBookView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.LedgerBookViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
