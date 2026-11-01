using System.Windows.Controls;

namespace RetailBillingApp.Views;

/// <summary>
/// PAY-11: cheque register. House load pattern only — the Loaded hook builds the register grid
/// the first time the pane is shown; everything else lives in ChequeRegisterViewModel.
/// </summary>
public partial class ChequeRegisterView : UserControl
{
    public ChequeRegisterView()
    {
        InitializeComponent();
        // INT-01 house pattern: the Loaded hook runs the initial query.
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.ChequeRegisterViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
