using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class CashFlowView : UserControl
{
    public CashFlowView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.CashFlowViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
