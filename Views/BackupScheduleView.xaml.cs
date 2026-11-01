using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class BackupScheduleView : UserControl
{
    public BackupScheduleView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.BackupScheduleViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
