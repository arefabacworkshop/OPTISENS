using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using OPTISENS.App.ViewModels;
using OPTISENS.Platform;

namespace OPTISENS.App.Views;

public partial class CalibrationSessionView : UserControl
{
    public CalibrationSessionView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
        KeyDown += OnKeyDown;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Focus();
        Viewport.FrameRendered += OnFrameRendered;
        if (DataContext is SessionViewModel vm)
        {
            vm.RequestCursorLock += LockCursor;
            vm.RequestCursorUnlock += UnlockCursor;
        }
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Viewport.FrameRendered -= OnFrameRendered;
        if (DataContext is SessionViewModel vm)
        {
            vm.RequestCursorLock -= LockCursor;
            vm.RequestCursorUnlock -= UnlockCursor;
        }
        UnlockCursor();
    }

    private void OnFrameRendered(double ms)
    {
        if (DataContext is SessionViewModel vm)
            vm.ReportFrameTime(ms);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SessionViewModel vm) return;
        if (e.Key == Key.Escape)
        {
            UnlockCursor();
            vm.RequestAbortPrompt();
            e.Handled = true;
        }
        else if (e.Key == Key.F12)
        {
            vm.ToggleDevOverlay();
            e.Handled = true;
        }
    }

    private void LockCursor()
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var center = Viewport.TranslatePoint(new Point(Viewport.Bounds.Width / 2, Viewport.Bounds.Height / 2), top);
            if (center is { } p)
            {
                var screenPt = top.PointToScreen(p);
                CursorHelper.LockAndHide(screenPt.X, screenPt.Y);
            }
        }
        catch
        {
            if (DataContext is SessionViewModel vm)
                vm.CursorWarning = "cursor not confined";
        }
    }

    private void UnlockCursor()
    {
        try { CursorHelper.UnlockAndShow(); }
        catch { /* ignore on unlock */ }
    }
}
