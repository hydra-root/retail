using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class MultiStoreView : UserControl
{
    public MultiStoreView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.MultiStoreViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
