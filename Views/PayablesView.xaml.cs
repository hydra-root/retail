using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class PayablesView : UserControl
{
    public PayablesView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.PayablesViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
