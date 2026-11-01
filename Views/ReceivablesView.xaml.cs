using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class ReceivablesView : UserControl
{
    public ReceivablesView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.ReceivablesViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
