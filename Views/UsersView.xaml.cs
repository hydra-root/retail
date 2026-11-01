using System.Windows;
using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class UsersView : UserControl
{
    public UsersView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.UsersViewModel vm)
                await vm.LoadDataAsync();
        };
    }

    // PasswordBox cannot be data-bound; push the password into
    // UsersViewModel.NewPassword before the add command validates it.
    private void AddPassword_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.UsersViewModel vm)
            vm.NewPassword = NewPasswordBox.Password;
    }
}
