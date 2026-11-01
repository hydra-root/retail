using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class SalesView : UserControl
{
    public SalesView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.SalesViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
