using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetailBillingApp.Services;
using System.Windows;

namespace RetailBillingApp.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly IAuthService _authService;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public LoginViewModel(IAuthService authService)
    {
        _authService = authService;
    }

    [RelayCommand]
    private void Login()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter username and password.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        if (_authService.Login(Username, Password))
        {
            // Navigate to dashboard via the MainWindow's DataContext
            if (App.Current.MainWindow?.DataContext is MainWindowViewModel mainVm)
            {
                mainVm.ShowDashboard();
            }
        }
        else
        {
            ErrorMessage = _authService.LastLoginError ?? "Invalid username or password.";
        }

        IsBusy = false;
    }
}

