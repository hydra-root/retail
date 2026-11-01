using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class AuditTrailView : UserControl
{
    public AuditTrailView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.AuditTrailViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
