using System.Windows.Controls;

namespace RetailBillingApp.Views;

public partial class ContactsView : UserControl
{
    public ContactsView()
    {
        InitializeComponent();
        // INT-01: house code-behind pattern - the Loaded hook builds the derived address book.
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.ContactsViewModel vm)
                await vm.LoadDataAsync();
        };
    }
}
