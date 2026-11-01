using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace RetailBillingApp.Views;

/// <summary>
/// INV-12: Quick POS view. The F-key shortcuts are wired through the root Grid's
/// PreviewKeyDown (handled in code-behind, never via KeyBinding to a command inside a
/// DataTemplate), and the barcode box stays focused: on load, after every scan/hold/
/// recall/settle via the view model's FocusBarcodeRequested event, and on F2.
/// </summary>
public partial class QuickPosView : UserControl
{
    public QuickPosView()
    {
        InitializeComponent();

        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.QuickPosViewModel vm)
            {
                vm.FocusBarcodeRequested += OnFocusBarcodeRequested;
                await vm.LoadDataAsync();
            }
            FocusBarcode();
        };
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ViewModels.QuickPosViewModel vm)
            return;

        switch (e.Key)
        {
            case Key.F2:   // barcode box
                FocusBarcode();
                e.Handled = true;
                break;
            case Key.F3:   // customer name box
                CustomerNameBox.Focus();
                CustomerNameBox.SelectAll();
                e.Handled = true;
                break;
            case Key.F4:   // hold ticket
                vm.HoldCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F5:   // recall held ticket (selected, newest if none)
                vm.RecallCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F9:   // settle as cash
                vm.SettleCashCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape: // clear the till
                vm.ClearFormCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnFocusBarcodeRequested(object? sender, EventArgs e)
    {
        // Deferred to the dispatcher so it lands after the command's UI updates.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusBarcode));
    }

    private void FocusBarcode()
    {
        BarcodeBox.Focus();
        BarcodeBox.SelectAll();
    }
}
