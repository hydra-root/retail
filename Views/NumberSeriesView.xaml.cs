using System.Windows.Controls;
using RetailBillingApp.Models;

namespace RetailBillingApp.Views;

public partial class NumberSeriesView : UserControl
{
    public NumberSeriesView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.NumberSeriesViewModel vm)
                await vm.LoadDataAsync();
        };
    }

    /// <summary>INV-03: flag prefix/suffix edits made inline in the grid.</summary>
    private void SeriesGrid_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
    {
        if (e.Row.Item is NumberSeries series
            && DataContext is ViewModels.NumberSeriesViewModel vm)
        {
            vm.MarkDirty(series);
        }
    }
}
