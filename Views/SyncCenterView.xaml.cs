using System.Windows;
using System.Windows.Controls;

namespace RetailBillingApp.Views;

/// <summary>
/// SEC-08: sync control centre. Standard house load pattern plus the two things only code-behind can
/// do: a PasswordBox cannot be data-bound (Password is not a dependency property), so the API key is
/// pushed into the view model from these handlers, and the box is wiped afterwards. The stored key is
/// never written into the box — it only ever reaches the screen masked, through ApiKeyStatusText.
/// </summary>
public partial class SyncCenterView : UserControl
{
    private ViewModels.SyncCenterViewModel? _subscribedVm;

    public SyncCenterView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.SyncCenterViewModel vm)
            {
                Subscribe(vm);
                ApiKeyBox.Clear();
                await vm.LoadDataAsync();
            }
        };
        Unloaded += (s, e) =>
        {
            if (_subscribedVm != null)
            {
                _subscribedVm.ClearApiKeyBoxRequested -= OnClearApiKeyBoxRequested;
                _subscribedVm = null;
            }
        };
    }

    private void Subscribe(ViewModels.SyncCenterViewModel vm)
    {
        if (ReferenceEquals(_subscribedVm, vm)) return;

        if (_subscribedVm != null)
            _subscribedVm.ClearApiKeyBoxRequested -= OnClearApiKeyBoxRequested;

        _subscribedVm = vm;
        _subscribedVm.ClearApiKeyBoxRequested += OnClearApiKeyBoxRequested;
    }

    private void OnClearApiKeyBoxRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() => ApiKeyBox.Clear());
    }

    /// <summary>
    /// Keeps "Save connection" enabled as soon as a key is typed: the key on its own is a change, and
    /// without this the form would only dirty itself when another field moved.
    /// </summary>
    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SyncCenterViewModel vm && sender is PasswordBox box)
            vm.PendingApiKeyInput = box.Password;
    }

    /// <summary>Click fires before the button's Command, so the key is in the view model by the time Save validates.</summary>
    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SyncCenterViewModel vm)
            vm.PendingApiKeyInput = ApiKeyBox.Password;
    }
}
