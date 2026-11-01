using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class SecurityView : UserControl
{
    public SecurityView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.SecurityViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
