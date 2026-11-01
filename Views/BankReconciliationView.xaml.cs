using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class BankReconciliationView : UserControl
{
    public BankReconciliationView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.BankReconciliationViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
