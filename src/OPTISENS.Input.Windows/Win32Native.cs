using System.Runtime.InteropServices;

namespace OPTISENS.Input.Windows;

internal static partial class Win32Native
{
    internal const int WM_INPUT = 0x00FF, WM_INPUT_DEVICE_CHANGE = 0x00FE, WM_QUIT = 0x0012;
    internal const int RIDEV_INPUTSINK = 0x00000100, RIDEV_DEVNOTIFY = 0x00002000, RIDEV_REMOVE = 0x00000001;
    internal const int RID_INPUT = 0x10000003;
    internal const int RIM_TYPEMOUSE = 0;
    internal const ushort RI_MOUSE_LEFT_BUTTON_DOWN   = 0x0001, RI_MOUSE_LEFT_BUTTON_UP   = 0x0002;
    internal const ushort RI_MOUSE_RIGHT_BUTTON_DOWN  = 0x0004, RI_MOUSE_RIGHT_BUTTON_UP  = 0x0008;
    internal const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010, RI_MOUSE_MIDDLE_BUTTON_UP = 0x0020;
    internal const ushort RI_MOUSE_BUTTON_4_DOWN      = 0x0040, RI_MOUSE_BUTTON_4_UP      = 0x0080;
    internal const ushort RI_MOUSE_BUTTON_5_DOWN      = 0x0100, RI_MOUSE_BUTTON_5_UP      = 0x0200;
    internal const ushort MOUSE_MOVE_RELATIVE = 0x00, MOUSE_MOVE_ABSOLUTE = 0x01;
    internal const int GIDC_ARRIVAL = 1, GIDC_REMOVAL = 2;
    internal static readonly IntPtr HWND_MESSAGE = new(-3);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public int dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTHEADER
    {
        public int dwType;
        public int dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct RAWMOUSE
    {
        [FieldOffset(0)]  public ushort usFlags;
        [FieldOffset(4)]  public ushort usButtonFlags;
        [FieldOffset(6)]  public ushort usButtonData;
        [FieldOffset(8)]  public uint   ulRawButtons;
        [FieldOffset(12)] public int    lLastX;
        [FieldOffset(16)] public int    lLastY;
        [FieldOffset(20)] public uint   ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct WNDCLASSW
    {
        public uint style;
        public delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int RegisterRawInputDevices(
        [In] RAWINPUTDEVICE[] pRawInputDevices, int uiNumDevices, int cbSize);

    [LibraryImport("user32.dll")]
    internal static partial uint GetRawInputData(
        IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr CreateWindowExW(
        uint exStyle, IntPtr className, IntPtr windowName, uint style, int x, int y,
        int width, int height, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial ushort RegisterClassW(ref WNDCLASSW lpWndClass);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    internal static partial int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    internal static partial int TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr DispatchMessageW(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    internal static partial int PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    internal static partial int DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    internal static partial int UnregisterClassW(IntPtr lpClassName, IntPtr hInstance);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryPerformanceCounter(out long lpPerformanceCount);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryPerformanceFrequency(out long lpFrequency);
}
