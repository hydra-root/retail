using System.Windows;
using System.Windows.Controls;

namespace RetailBillingApp.Views;

/// <summary>
/// SEC-05: admin editor for the critical-change passcode policy.
/// A PasswordBox.Password is not a dependency property, so it cannot be bound; the secrets are
/// pushed into the ViewModel from these Click handlers (the Click event fires before the
/// button's Command), exactly like UsersView does for the new-user password. The ViewModel then
/// raises ClearPasscodeBoxesRequested so the boxes are wiped once the write is done.
/// </summary>
public partial class CriticalChangePolicyView : UserControl
{
    private ViewModels.CriticalChangePolicyViewModel? _subscribedVm;

    public CriticalChangePolicyView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.CriticalChangePolicyViewModel vm)
            {
                Subscribe(vm);
                await vm.LoadDataAsync();
            }
        };
        Unloaded += (s, e) =>
        {
            if (_subscribedVm != null)
            {
                _subscribedVm.ClearPasscodeBoxesRequested -= OnClearPasscodeBoxesRequested;
                _subscribedVm = null;
            }
        };
    }

    private void Subscribe(ViewModels.CriticalChangePolicyViewModel vm)
    {
        if (ReferenceEquals(_subscribedVm, vm)) return;

        if (_subscribedVm != null)
            _subscribedVm.ClearPasscodeBoxesRequested -= OnClearPasscodeBoxesRequested;

        _subscribedVm = vm;
        _subscribedVm.ClearPasscodeBoxesRequested += OnClearPasscodeBoxesRequested;
    }

    private void OnClearPasscodeBoxesRequested(
        object? sender, ViewModels.CriticalChangePolicyViewModel.PasscodeFieldScope scope)
    {
        Dispatcher.Invoke(() =>
        {
            TestPasscodeBox.Clear();

            if (scope == ViewModels.CriticalChangePolicyViewModel.PasscodeFieldScope.All)
            {
                CurrentPasscodeBox.Clear();
                NewPasscodeBox.Clear();
                ConfirmPasscodeBox.Clear();
            }
        });
    }

    private ViewModels.CriticalChangePolicyViewModel? ViewModel =>
        DataContext as ViewModels.CriticalChangePolicyViewModel;

    private void SavePasscode_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.CurrentPasscode = CurrentPasscodeBox.Password;
            vm.NewPasscode = NewPasscodeBox.Password;
            vm.ConfirmPasscode = ConfirmPasscodeBox.Password;
        }
    }

    private void ClearPasscode_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            vm.CurrentPasscode = CurrentPasscodeBox.Password;
    }

    private void TestGate_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            vm.TestPasscode = TestPasscodeBox.Password;
    }
}
