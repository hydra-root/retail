using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class CreditAnalysisView : UserControl
{
    public CreditAnalysisView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.CreditAnalysisViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
