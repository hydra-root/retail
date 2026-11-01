using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class RoleView : UserControl
{
    public RoleView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.RoleViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
