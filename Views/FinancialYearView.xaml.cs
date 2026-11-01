using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class FinancialYearView : UserControl
{
    public FinancialYearView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.FinancialYearViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
