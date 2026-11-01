using System.Windows.Controls;

namespace RetailBillingApp.Views;

/// <summary>
/// USR-03: store-wise pricing. House load pattern only — the Loaded hook builds the pricing grid
/// the first time the pane is shown; everything else lives in StorePricingViewModel.
/// </summary>
public partial class StorePricingView : UserControl
{
    public StorePricingView()
    {
        InitializeComponent();
        // INT-01 house pattern: the Loaded hook runs the initial query.
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.StorePricingViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
