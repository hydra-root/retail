using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class CustomerView : UserControl
{
    public CustomerView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.CustomerViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
