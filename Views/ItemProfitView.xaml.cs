using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class ItemProfitView : UserControl
{
    public ItemProfitView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.ItemProfitViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
