using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class BulkImportView : UserControl
{
    public BulkImportView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.BulkImportViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
