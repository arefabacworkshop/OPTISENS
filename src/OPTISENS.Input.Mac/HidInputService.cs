using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using OPTISENS.Core.Input;
using static OPTISENS.Input.Mac.IOKitNative;

namespace OPTISENS.Input.Mac;

public sealed class HidInputService : IRawInputService
{
    private readonly Channel<RawMotionSample> _channel = Channel.CreateUnbounded<RawMotionSample>(
        new UnboundedChannelOptions { SingleWriter = true, SingleReader = false, AllowSynchronousContinuations = false });

    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _runLoopThread;
    private IntPtr _mgr;
    private IntPtr _runLoop;
    private MachTimebaseInfo _timebase;
    private volatile bool _running;
    private int _buttons;
    private int _pendingDx;
    private long _lastFlushUs;
    private PlatformInputException? _startupError;

    private static HidInputService? _activeInstance;

    public MouseButtons CurrentButtons => (MouseButtons)Volatile.Read(ref _buttons);
    public bool IsReceiving => _running && _mgr != IntPtr.Zero;
    public ChannelReader<RawMotionSample> Samples => _channel.Reader;
    public event Action<int>? DeviceCountChanged;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _startupError = null;
        _ready.Reset();
        _activeInstance = this;

        _runLoopThread = new Thread(RunLoopMain)
        {
            IsBackground = true,
            Name = "optisens-iokit",
            Priority = ThreadPriority.AboveNormal
        };
        _runLoopThread.Start();
        _ready.Wait();
        if (_startupError is not null)
            throw _startupError;
    }

    private unsafe void RunLoopMain()
    {
        MachTimebaseInfoFn(out _timebase);
        if (_timebase.Denom == 0) { _timebase.Numer = 1; _timebase.Denom = 1; }

        _mgr = IOHIDManagerCreate(IntPtr.Zero, 0);
        if (_mgr == IntPtr.Zero)
        {
            _startupError = new PlatformInputException(
                "Failed to create IOHIDManager.",
                "Restart the application and verify macOS Input Monitoring permissions.");
            _ready.Set();
            return;
        }

        IntPtr dict = CFDictionaryCreateMutable(IntPtr.Zero, 2, IntPtr.Zero, IntPtr.Zero);
        IntPtr kPage = CFStringCreateWithCString(IntPtr.Zero, "DeviceUsagePage", kCFStringEncodingUTF8);
        IntPtr kUsage = CFStringCreateWithCString(IntPtr.Zero, "DeviceUsage", kCFStringEncodingUTF8);
        int page = (int)kHIDPage_GenericDesktop;
        int usage = (int)kHIDUsage_GD_Mouse;
        IntPtr vPage = CFNumberCreate(IntPtr.Zero, kCFNumberSInt32Type, &page);
        IntPtr vUsage = CFNumberCreate(IntPtr.Zero, kCFNumberSInt32Type, &usage);
        CFDictionarySetValue(dict, kPage, vPage);
        CFDictionarySetValue(dict, kUsage, vUsage);
        IOHIDManagerSetDeviceMatching(_mgr, dict);
        CFRelease(vPage);
        CFRelease(vUsage);
        CFRelease(kPage);
        CFRelease(kUsage);
        CFRelease(dict);

        int openRes = IOHIDManagerOpen(_mgr, 0);
        if (openRes != 0)
        {
            _startupError = new PlatformInputException(
                $"IOHIDManagerOpen failed (0x{openRes:X8}).",
                "System Settings -> Privacy & Security -> Input Monitoring -> enable OPTISENS, then relaunch.");
            CFRelease(_mgr);
            _mgr = IntPtr.Zero;
            _ready.Set();
            return;
        }

        IOHIDManagerRegisterInputValueCallback(_mgr, &OnInputValue, IntPtr.Zero);
        _runLoop = CFRunLoopGetCurrent();
        IntPtr defaultMode = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", kCFStringEncodingUTF8);
        IOHIDManagerScheduleWithRunLoop(_mgr, _runLoop, defaultMode);
        CFRelease(defaultMode);

        DeviceCountChanged?.Invoke(1);
        _ready.Set();
        CFRunLoopRun();

        IOHIDManagerClose(_mgr, 0);
        CFRelease(_mgr);
        _mgr = IntPtr.Zero;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnInputValue(IntPtr context, int result, IntPtr sender, IntPtr value)
    {
        _activeInstance?.HandleValue(value);
    }

    private void HandleValue(IntPtr value)
    {
        IntPtr elem = IOHIDValueGetElement(value);
        uint page = IOHIDElementGetUsagePage(elem);
        uint usage = IOHIDElementGetUsage(elem);
        int val = IOHIDValueGetIntegerValue(value);

        ulong abs = MachAbsoluteTime();
        long tsUs = (long)(abs * _timebase.Numer / _timebase.Denom / 1000UL);

        if (page == kHIDPage_GenericDesktop)
        {
            if (usage == kHIDUsage_GD_X)
            {
                _pendingDx += val;
                if (_lastFlushUs != 0 && tsUs - _lastFlushUs >= 2000)
                {
                    _channel.Writer.TryWrite(new RawMotionSample(tsUs, _pendingDx, 0, CurrentButtons));
                    _pendingDx = 0;
                    _lastFlushUs = tsUs;
                }
            }
            else if (usage == kHIDUsage_GD_Y)
            {
                _channel.Writer.TryWrite(new RawMotionSample(tsUs, _pendingDx, val, CurrentButtons));
                _pendingDx = 0;
                _lastFlushUs = tsUs;
            }
        }
        else if (page == kHIDPage_Button && usage is >= 1 and <= 5)
        {
            MouseButtons flag = usage switch
            {
                1 => MouseButtons.Left,
                2 => MouseButtons.Right,
                3 => MouseButtons.Middle,
                4 => MouseButtons.X1,
                5 => MouseButtons.X2,
                _ => MouseButtons.None
            };
            int b = Volatile.Read(ref _buttons);
            if (val != 0) b |= (int)flag;
            else b &= ~(int)flag;
            Volatile.Write(ref _buttons, b);
            _channel.Writer.TryWrite(new RawMotionSample(tsUs, 0, 0, (MouseButtons)b));
        }
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        if (_runLoop != IntPtr.Zero)
            CFRunLoopStop(_runLoop);
        _runLoopThread?.Join(500);
        _runLoopThread = null;
        if (ReferenceEquals(_activeInstance, this))
            _activeInstance = null;
    }

    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }
}
