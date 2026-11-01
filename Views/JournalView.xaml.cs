using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class JournalView : UserControl
{
    public JournalView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.JournalViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
