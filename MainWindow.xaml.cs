using Microsoft.Extensions.DependencyInjection;
using RetailBillingApp.ViewModels;
using System.Windows;

namespace RetailBillingApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var viewModel = App.ServiceProvider!.GetRequiredService<MainWindowViewModel>();
        DataContext = viewModel;
    }
}
