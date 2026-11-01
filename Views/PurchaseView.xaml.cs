using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class PurchaseView : UserControl
{
    public PurchaseView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.PurchaseViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
