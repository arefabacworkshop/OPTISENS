using Avalonia.Controls;
using Avalonia.Platform.Storage;
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
            if (DataContext is not ResultsViewModel vm) return;
            if (TopLevel.GetTopLevel(this) is { Clipboard: { } clip })
                vm.Clipboard = clip;
            vm.SaveJsonFileAsync = async json =>
            {
                var top = TopLevel.GetTopLevel(this);
                if (top is null) return;
                var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export session telemetry",
                    SuggestedFileName = "optisens-session.json",
                    DefaultExtension = "json",
                    FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
                });
                if (file is null) return;
                await using var stream = await file.OpenWriteAsync();
                await using var writer = new System.IO.StreamWriter(stream);
                await writer.WriteAsync(json);
            };
        };
    }
}
