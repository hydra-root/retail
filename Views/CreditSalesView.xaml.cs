using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class CreditSalesView : UserControl
{
    public CreditSalesView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.CreditSalesViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
