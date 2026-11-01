using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class InvoicesView : UserControl
{
    public InvoicesView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.InvoicesViewModel vm)
                await vm.LoadDataAsync();
        };
    }

    /// <summary>PAY-05: PasswordBox is not bindable in WPF; push the override password into the VM.</summary>
    private void OverridePasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.InvoicesViewModel vm && sender is PasswordBox box)
            vm.OverridePassword = box.Password;
    }
}
