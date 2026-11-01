using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class StoreView : UserControl
{
    public StoreView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.StoreViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
