using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class ReminderView : UserControl
{
    public ReminderView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.ReminderViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
