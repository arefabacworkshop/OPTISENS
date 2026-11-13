using System.Runtime.InteropServices;

namespace OPTISENS.Platform;

/// <summary>OS cursor confinement/hiding for active calibration rounds.
/// Windows: ClipCursor + ShowCursor. macOS: CGAssociateMouseAndMouseCursorPosition + hide/show.
/// Linux: confinement is compositor-managed; raw evdev button capture is global
/// regardless, so hit-testing is unaffected — helper is a documented no-op there.</summary>
public static partial class CursorHelper
{
    private static int _lockDepth;
    private static bool _cursorHidden;

    // ---- Windows ----
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [LibraryImport("user32.dll")]
    internal static partial int ClipCursor(ref RECT rect);

    [LibraryImport("user32.dll")]
    internal static partial int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool bShow);

    [LibraryImport("user32.dll")]
    internal static partial int ClipCursor(nint zero);   // NULL overload to release

    // ---- macOS (CoreGraphics) ----
    [LibraryImport("CoreGraphics", EntryPoint = "CGAssociateMouseAndMouseCursorPosition")]
    internal static partial int CGAssociateMouseAndMouseCursorPosition(
        [MarshalAs(UnmanagedType.I1)] bool connected); // false = decouple cursor from mouse

    [LibraryImport("CoreGraphics", EntryPoint = "CGDisplayHideCursor")]
    internal static partial uint CGDisplayHideCursor(uint display);

    [LibraryImport("CoreGraphics", EntryPoint = "CGDisplayShowCursor")]
    internal static partial uint CGDisplayShowCursor(uint display);

    [LibraryImport("CoreGraphics", EntryPoint = "CGMainDisplayID")]
    internal static partial uint CGMainDisplayID();

    /// <summary>Confine the OS cursor to a 1-px box at (centerX, centerY) in *screen* coordinates
    /// and hide it. Idempotent (depth-counted). Call from the UI thread.</summary>
    public static void LockAndHide(int centerX, int centerY)
    {
        if (Interlocked.Increment(ref _lockDepth) > 1) return;
        if (OperatingSystem.IsWindows())
        {
            var r = new RECT { Left = centerX - 1, Top = centerY - 1, Right = centerX + 1, Bottom = centerY + 1 };
            ClipCursor(ref r);
            if (!_cursorHidden) { ShowCursor(false); _cursorHidden = true; }
        }
        else if (OperatingSystem.IsMacOS())
        {
            CGAssociateMouseAndMouseCursorPosition(false);   // decouple: flicks don't move the OS cursor
            CGDisplayHideCursor(CGMainDisplayID());
        }
    }

    /// <summary>Restore cursor visibility/association. Safe to call without a prior lock.</summary>
    public static void UnlockAndShow()
    {
        if (Interlocked.Decrement(ref _lockDepth) > 0) return;
        _lockDepth = 0;
        if (OperatingSystem.IsWindows())
        {
            ClipCursor(0);                                   // release confinement
            if (_cursorHidden) { ShowCursor(true); _cursorHidden = false; }
        }
        else if (OperatingSystem.IsMacOS())
        {
            CGDisplayShowCursor(CGMainDisplayID());
            CGAssociateMouseAndMouseCursorPosition(true);    // re-couple
        }
    }
}
