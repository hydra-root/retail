using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class IntegrityView : UserControl
{
    public IntegrityView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.IntegrityViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
