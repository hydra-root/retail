using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class OfflineSyncView : UserControl
{
    public OfflineSyncView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.OfflineSyncViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
