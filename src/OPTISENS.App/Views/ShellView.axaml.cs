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

    private void ToggleWindowState(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    private void MinimizeWindow(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShellViewModel vm)
            return;

        if (e.Key == Key.F11)
        {
            e.Handled = true;
            ToggleWindowState(this, new RoutedEventArgs());
            return;
        }

        if (e.Key == Key.T &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
            vm.CurrentPage is HardwareProfileViewModel or SessionViewModel)
        {
            e.Handled = true;
            try { CursorHelper.UnlockAndShow(); } catch { /* ignore */ }
            vm.SkipToSampleResults();
            return;
        }

        if (e.Key != Key.Escape)
            return;

        switch (vm.CurrentPage)
        {
            case SessionViewModel sessionVm:
                e.Handled = true;
                if (sessionVm.ShowAbortConfirm)
                    sessionVm.CancelAbortCommand.Execute(null);
                else
                    sessionVm.RequestAbortPrompt();
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
