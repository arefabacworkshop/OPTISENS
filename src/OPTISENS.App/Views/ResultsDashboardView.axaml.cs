using Avalonia.Controls;
using OPTISENS.App.ViewModels;

namespace OPTISENS.App.Views;

public partial class ResultsDashboardView : UserControl
{
    public ResultsDashboardView()
    {
        InitializeComponent();
        GpPlot.PointClicked += cm =>
        {
            if (DataContext is ResultsViewModel vm)
                vm.SelectCm360FromPlot(cm);
        };
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is ResultsViewModel vm && TopLevel.GetTopLevel(this) is { Clipboard: { } clip })
                vm.Clipboard = clip;
        };
    }
}
