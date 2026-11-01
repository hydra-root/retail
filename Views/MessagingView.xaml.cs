using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class MessagingView : UserControl
{
    public MessagingView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.MessagingViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
