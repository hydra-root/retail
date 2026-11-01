using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class ExpenseView : UserControl
{
    public ExpenseView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.ExpenseViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
