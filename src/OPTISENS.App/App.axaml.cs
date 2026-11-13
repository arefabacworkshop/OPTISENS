using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OPTISENS.App.ViewModels;
using OPTISENS.App.Views;
using OPTISENS.Core.Input;

namespace OPTISENS.App;

public partial class App : Application
{
    public static IRawInputService? InputService { get; set; }
    public static string ModelDir { get; set; } = "";
    public static bool DevMode { get; set; }
    public static bool SkipAi { get; set; }
    public static string? StartupError { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && InputService is not null)
        {
            var shellVm = new ShellViewModel(InputService, ModelDir, DevMode, SkipAi, StartupError);
            desktop.MainWindow = new ShellView { DataContext = shellVm };
            desktop.ShutdownRequested += (_, _) =>
            {
                shellVm.Dispose();
                InputService.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
