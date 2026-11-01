using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RetailBillingApp.ViewModels;

namespace RetailBillingApp.Views;

/// <summary>
/// INV-06 / INV-08: hosts the invoice print preview (thermal text strip or paginated
/// A4 / half-sheet) with the toolbar that drives layout, copies and printing.
/// The view model is resolved from App.ServiceProvider the way navigation does, so the
/// window is safe to open repeatedly — no static state. Pass the invoice Id to open it.
/// </summary>
public partial class InvoicePrintPreviewWindow : Window
{
    /// <summary>
    /// Opens the preview for one invoice. <paramref name="layoutOverride"/> optionally
    /// forces a layout ("Thermal 80mm" / "Half Sheet" / "A4"); when null the saved
    /// template paper size is used.
    /// </summary>
    public InvoicePrintPreviewWindow(int invoiceId, string? layoutOverride = null)
    {
        InitializeComponent();

        var vm = App.ServiceProvider!.GetRequiredService<InvoicePrintPreviewViewModel>();
        vm.CloseAction = Close;
        DataContext = vm;

        Loaded += async (_, _) => await vm.InitializeAsync(invoiceId, layoutOverride);
    }
}
