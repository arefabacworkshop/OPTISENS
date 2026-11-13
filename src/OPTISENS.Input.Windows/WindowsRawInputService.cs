using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using OPTISENS.Core.Input;
using static OPTISENS.Input.Windows.Win32Native;

namespace OPTISENS.Input.Windows;

public sealed class WindowsRawInputService : IRawInputService
{
    private readonly Channel<RawMotionSample> _channel = Channel.CreateUnbounded<RawMotionSample>(
        new UnboundedChannelOptions { SingleWriter = true, SingleReader = false, AllowSynchronousContinuations = false });

    private readonly HashSet<IntPtr> _devices = new();
    private readonly object _deviceLock = new();
    private readonly ManualResetEventSlim _ready = new(false);

    private Thread? _pumpThread;
    private uint _threadId;
    private IntPtr _msgHwnd;
    private ushort _classAtom;
    private long _qpcFreq;
    private int _buttons;
    private volatile bool _running;
    private PlatformInputException? _startupError;

    private static WindowsRawInputService? _activeInstance;

    public MouseButtons CurrentButtons => (MouseButtons)Volatile.Read(ref _buttons);
    public bool IsReceiving { get { lock (_deviceLock) return _devices.Count > 0; } }
    public ChannelReader<RawMotionSample> Samples => _channel.Reader;
    public event Action<int>? DeviceCountChanged;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _startupError = null;
        _ready.Reset();
        _activeInstance = this;

        _pumpThread = new Thread(PumpMain)
        {
            IsBackground = true,
            Name = "optisens-rawinput",
            Priority = ThreadPriority.AboveNormal
        };
        _pumpThread.Start();
        _ready.Wait();
        if (_startupError is not null)
            throw _startupError;
    }

    private unsafe void PumpMain()
    {
        try
        {
            QueryPerformanceFrequency(out _qpcFreq);
            if (_qpcFreq <= 0) _qpcFreq = 10_000_000;
            _threadId = GetCurrentThreadId();

            fixed (char* className = "OPTISENSRawInput")
            {
                var wc = new WNDCLASSW
                {
                    lpfnWndProc = &WndProc,
                    lpszClassName = className
                };
                _classAtom = RegisterClassW(ref wc);
            }

            _msgHwnd = CreateWindowExW(0, (IntPtr)_classAtom, 0, 0, 0, 0, 0, 0, HWND_MESSAGE, 0, 0, 0);
            if (_msgHwnd == IntPtr.Zero)
            {
                _startupError = new PlatformInputException(
                    "Failed to create message-only window for Raw Input.",
                    "Restart the application or verify Windows desktop session permissions.");
                _ready.Set();
                return;
            }

            var rid = new RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x02,
                dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY,
                hwndTarget = _msgHwnd
            };

            if (RegisterRawInputDevices([rid], 1, Marshal.SizeOf<RAWINPUTDEVICE>()) == 0)
            {
                int err = Marshal.GetLastWin32Error();
                _startupError = new PlatformInputException(
                    $"RegisterRawInputDevices failed (Win32 error {err}).",
                    "Ensure a mouse device is connected and the application has desktop input access.");
                _ready.Set();
                return;
            }

            _ready.Set();

            while (_running && GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }
        finally
        {
            if (_msgHwnd != IntPtr.Zero)
            {
                var remove = new RAWINPUTDEVICE
                {
                    usUsagePage = 0x01,
                    usUsage = 0x02,
                    dwFlags = RIDEV_REMOVE,
                    hwndTarget = IntPtr.Zero
                };
                RegisterRawInputDevices([remove], 1, Marshal.SizeOf<RAWINPUTDEVICE>());
                DestroyWindow(_msgHwnd);
                _msgHwnd = IntPtr.Zero;
            }
            if (_classAtom != 0)
            {
                UnregisterClassW((IntPtr)_classAtom, IntPtr.Zero);
                _classAtom = 0;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var inst = _activeInstance;
        if (inst is not null)
        {
            if (msg == WM_INPUT)
            {
                inst.HandleWmInput(lParam);
                return IntPtr.Zero;
            }
            if (msg == WM_INPUT_DEVICE_CHANGE)
            {
                inst.HandleDeviceChange((int)wParam, lParam);
                return IntPtr.Zero;
            }
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private unsafe void HandleWmInput(IntPtr lParam)
    {
        QueryPerformanceCounter(out long qpc);
        long tsUs = (long)(qpc * 1_000_000.0 / _qpcFreq);

        uint size = 0;
        uint headerSize = (uint)Unsafe.SizeOf<RAWINPUTHEADER>();
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size < headerSize + (uint)Unsafe.SizeOf<RAWMOUSE>()) return;

        byte[] buf = ArrayPool<byte>.Shared.Rent((int)size);
        try
        {
            fixed (byte* p = buf)
            {
                if (GetRawInputData(lParam, RID_INPUT, (IntPtr)p, ref size, headerSize) != size)
                    return;

                ref var header = ref Unsafe.As<byte, RAWINPUTHEADER>(ref buf[0]);
                if (header.dwType != RIM_TYPEMOUSE) return;

                lock (_deviceLock)
                {
                    if (_devices.Add(header.hDevice))
                        DeviceCountChanged?.Invoke(_devices.Count);
                }

                ref var mouse = ref Unsafe.As<byte, RAWMOUSE>(ref buf[headerSize]);
                if ((mouse.usFlags & MOUSE_MOVE_ABSOLUTE) != 0) return;

                MouseButtons buttons = UpdateButtons(mouse.usButtonFlags);
                _channel.Writer.TryWrite(new RawMotionSample(tsUs, mouse.lLastX, mouse.lLastY, buttons));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private static readonly (ushort Down, ushort Up, MouseButtons Flag)[] ButtonMap =
    [
        (RI_MOUSE_LEFT_BUTTON_DOWN,   RI_MOUSE_LEFT_BUTTON_UP,   MouseButtons.Left),
        (RI_MOUSE_RIGHT_BUTTON_DOWN,  RI_MOUSE_RIGHT_BUTTON_UP,  MouseButtons.Right),
        (RI_MOUSE_MIDDLE_BUTTON_DOWN, RI_MOUSE_MIDDLE_BUTTON_UP, MouseButtons.Middle),
        (RI_MOUSE_BUTTON_4_DOWN,      RI_MOUSE_BUTTON_4_UP,      MouseButtons.X1),
        (RI_MOUSE_BUTTON_5_DOWN,      RI_MOUSE_BUTTON_5_UP,      MouseButtons.X2),
    ];

    private MouseButtons UpdateButtons(ushort bf)
    {
        int b = Volatile.Read(ref _buttons);
        foreach (var (down, up, flag) in ButtonMap)
        {
            if ((bf & down) != 0) b |= (int)flag;
            if ((bf & up) != 0)   b &= ~(int)flag;
        }
        Volatile.Write(ref _buttons, b);
        return (MouseButtons)b;
    }

    private void HandleDeviceChange(int code, IntPtr hDevice)
    {
        int count;
        lock (_deviceLock)
        {
            if (code == GIDC_ARRIVAL) _devices.Add(hDevice);
            else if (code == GIDC_REMOVAL) _devices.Remove(hDevice);
            count = _devices.Count;
        }
        DeviceCountChanged?.Invoke(count);
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        if (_threadId != 0)
            PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _pumpThread?.Join(500);
        _pumpThread = null;
        if (ReferenceEquals(_activeInstance, this))
            _activeInstance = null;
    }

    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }
}
