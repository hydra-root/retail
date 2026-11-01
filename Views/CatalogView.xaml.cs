using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class CatalogView : UserControl
{
    public CatalogView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.CatalogViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
