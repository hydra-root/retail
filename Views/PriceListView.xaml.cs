using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class PriceListView : UserControl
{
    public PriceListView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.PriceListViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
