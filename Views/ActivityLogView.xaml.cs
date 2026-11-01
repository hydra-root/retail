using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class ActivityLogView : UserControl
{
    public ActivityLogView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.ActivityLogViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
