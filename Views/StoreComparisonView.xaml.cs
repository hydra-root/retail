using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class StoreComparisonView : UserControl
{
    public StoreComparisonView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.StoreComparisonViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
