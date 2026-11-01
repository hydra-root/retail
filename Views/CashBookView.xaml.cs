using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class CashBookView : UserControl
{
    public CashBookView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.CashBookViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
