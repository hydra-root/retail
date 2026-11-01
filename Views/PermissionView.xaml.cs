using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class PermissionView : UserControl
{
    public PermissionView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.PermissionViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
