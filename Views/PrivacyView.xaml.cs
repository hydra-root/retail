using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class PrivacyView : UserControl
{
    public PrivacyView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.PrivacyViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
