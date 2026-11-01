using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class BatchView : UserControl
{
    public BatchView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.BatchViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
