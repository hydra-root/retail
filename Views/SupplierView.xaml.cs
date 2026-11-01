using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class SupplierView : UserControl
{
    public SupplierView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.SupplierViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
