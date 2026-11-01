using System.Windows;

namespace RetailBillingApp.Views;

/// <summary>
/// USR-06: modal passcode/password prompt for sensitive actions
/// (reset password, delete users, etc.). Returns the entered passcode.
/// </summary>
public partial class PasscodeDialog : Window
{
    public PasscodeDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows the dialog and returns the entered passcode, or null when cancelled.
    /// If <paramref name="validate" /> is given, keeps prompting until validation passes.
    /// Validation can return a custom error message; empty string means valid.
    /// </summary>
    public static string? Show(string title, string message, Func<string, string?>? validate = null)
    {
        var dialog = new PasscodeDialog
        {
            Title = title,
            Owner = System.Windows.Application.Current?.Windows
                .OfType<Window>().FirstOrDefault(w => w.IsActive)
        };

        dialog.MessageText.Text = message;

        while (true)
        {
            dialog.ErrorText.Visibility = Visibility.Collapsed;
            dialog.PasswordBox.Clear();
            var result = dialog.ShowDialog();
            if (result != true) return null;

            var passcode = dialog.PasswordBox.Password;
            if (string.IsNullOrEmpty(passcode))
            {
                dialog.ShowError("Please enter the passcode.");
                continue;
            }

            var error = validate?.Invoke(passcode);
            if (string.IsNullOrEmpty(error)) return passcode;

            dialog.ShowError(error!);
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PasswordBox.Password))
        {
            ShowError("Please enter the passcode.");
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
