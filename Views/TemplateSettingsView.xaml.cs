using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class TemplateSettingsView : UserControl
{
    public TemplateSettingsView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.TemplateSettingsViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
