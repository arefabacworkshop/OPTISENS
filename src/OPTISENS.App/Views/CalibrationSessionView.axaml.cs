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
        Viewport.GlInitFailed += OnGlInitFailed;
        if (DataContext is SessionViewModel vm)
        {
            vm.RequestCursorLock += LockCursor;
            vm.RequestCursorUnlock += UnlockCursor;
        }
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Viewport.FrameRendered -= OnFrameRendered;
        Viewport.GlInitFailed -= OnGlInitFailed;
        if (DataContext is SessionViewModel vm)
        {
            vm.RequestCursorLock -= LockCursor;
            vm.RequestCursorUnlock -= UnlockCursor;
        }
        UnlockCursor();
    }

    private void OnGlInitFailed(string msg)
    {
        if (DataContext is SessionViewModel vm)
            vm.CursorWarning = $"OpenGL init failed: {msg}";
    }

    private void OnFrameRendered(double ms)
    {
        if (DataContext is SessionViewModel vm)
            vm.ReportFrameTime(ms);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SessionViewModel vm) return;
        if (e.Key == Key.F12)
        {
            vm.ToggleDevOverlay();
            e.Handled = true;
        }
        else if (e.Key == Key.Space && vm.IsRestPhase)
        {
            vm.SkipRestCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void LockCursor()
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var size = Viewport.Bounds.Width > 0 ? Viewport.Bounds.Size : top.ClientSize;
            var center = Viewport.TranslatePoint(new Point(size.Width / 2, size.Height / 2), top)
                         ?? new Point(top.ClientSize.Width / 2, top.ClientSize.Height / 2);
            var screenPt = top.PointToScreen(center);
            CursorHelper.LockAndHide(screenPt.X, screenPt.Y);
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
