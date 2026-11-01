using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class PaymentView : UserControl
{
    public PaymentView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.PaymentViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
