using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class TallyExportView : UserControl
{
    public TallyExportView()
    {
        InitializeComponent();
        // House pattern: the view's Loaded hook seeds defaults (stores, financial-year range, preview).
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.TallyExportViewModel vm && !vm.IsInitialized)
                await vm.InitializeAsync();
        };
    }
}
