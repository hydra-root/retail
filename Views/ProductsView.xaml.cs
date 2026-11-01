using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class ProductsView : UserControl
{
    public ProductsView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.ProductsViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
