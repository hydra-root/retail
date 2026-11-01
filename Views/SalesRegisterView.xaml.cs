using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class SalesRegisterView : UserControl
{
    public SalesRegisterView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.SalesRegisterViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
