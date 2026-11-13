using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using OPTISENS.App.ViewModels;
using OPTISENS.Platform;

namespace OPTISENS.App.Views;

public partial class ShellView : Window
{
    public ShellView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not ShellViewModel vm)
            return;

        switch (vm.CurrentPage)
        {
            case SessionViewModel sessionVm:
                e.Handled = true;
                try { CursorHelper.UnlockAndShow(); } catch { /* ignore */ }
                sessionVm.AbortCommand.Execute(null);
                break;

            case ResultsViewModel:
                e.Handled = true;
                vm.NavigateToProfile();
                break;

            case HardwareProfileViewModel:
                e.Handled = true;
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
                break;
        }
    }
}
