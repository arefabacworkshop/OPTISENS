using System.Runtime.InteropServices;
using System.Threading.Channels;
using OPTISENS.Core.Input;
using static OPTISENS.Input.Linux.LibcNative;

namespace OPTISENS.Input.Linux;

public sealed class EvdevInputService : IRawInputService
{
    private sealed class DeviceState
    {
        public int Fd;
        public string Path = "";
        public int Dx;
        public int Dy;
        public bool ButtonsChanged;
    }

    private static long _droppedSamples;   // ponytail: static — one raw-input service per process
    private readonly Channel<RawMotionSample> _channel = Channel.CreateBounded<RawMotionSample>(
        new BoundedChannelOptions(16_384)
        {
            SingleWriter = true,
            SingleReader = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest
        },
        _ => Interlocked.Increment(ref _droppedSamples));

    private readonly object _gate = new();
    private readonly List<DeviceState> _devices = new();
    private FileSystemWatcher? _watcher;
    private Thread? _readerThread;
    private volatile bool _running;
    private volatile bool _rescanRequested;
    private int _buttons;

    public MouseButtons CurrentButtons => (MouseButtons)Volatile.Read(ref _buttons);
    public bool IsReceiving { get { lock (_gate) return _devices.Count > 0; } }
    public ChannelReader<RawMotionSample> Samples => _channel.Reader;
    public long DroppedSamples => Volatile.Read(ref _droppedSamples);
    public event Action<int>? DeviceCountChanged;

    public void Start()
    {
        if (_running) return;
        DiscoverDevices(throwOnAccessDenied: true);

        _running = true;
        if (Directory.Exists("/dev/input"))
        {
            _watcher = new FileSystemWatcher("/dev/input")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            _watcher.Created += (_, _) => _rescanRequested = true;
            _watcher.Deleted += (_, _) => _rescanRequested = true;
        }

        _readerThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "optisens-evdev",
            Priority = ThreadPriority.AboveNormal
        };
        _readerThread.Start();
    }

    private unsafe void DiscoverDevices(bool throwOnAccessDenied)
    {
        var candidates = new List<string>();
        if (Directory.Exists("/dev/input/by-id"))
        {
            foreach (var p in Directory.GetFiles("/dev/input/by-id", "*-event-mouse"))
                candidates.Add(p);
        }
        if (candidates.Count == 0)
        {
            for (int i = 0; i < 32; i++)
            {
                string p = $"/dev/input/event{i}";
                if (File.Exists(p)) candidates.Add(p);
            }
        }

        bool sawEacces = false;
        var opened = new List<DeviceState>();
        byte* evBits = stackalloc byte[8];
        byte* relBits = stackalloc byte[8];
        foreach (var path in candidates)
        {
            int fd = Open(path, O_RDONLY | O_NONBLOCK | O_CLOEXEC);
            if (fd < 0)
            {
                if (Marshal.GetLastWin32Error() == EACCES) sawEacces = true;
                continue;
            }

            int ver = 0;
            if (Ioctl(fd, EVIOCGVERSION, &ver) < 0 ||
                Ioctl(fd, Eviocgbit(0, 8), evBits) < 0 ||
                (evBits[0] & (1 << EV_REL)) == 0 ||
                Ioctl(fd, Eviocgbit(EV_REL, 8), relBits) < 0 ||
                (relBits[0] & ((1 << REL_X) | (1 << REL_Y))) != ((1 << REL_X) | (1 << REL_Y)))
            {
                Close(fd);
                continue;
            }
            opened.Add(new DeviceState { Fd = fd, Path = path });
        }

        if (opened.Count == 0 && sawEacces && throwOnAccessDenied)
        {
            throw new PlatformInputException(
                "Permission denied opening /dev/input/event* mouse devices (EACCES).",
                "Run packaging/linux/install-udev.sh (via pkexec/sudo) to add your user to the 'input' group and install 99-optisens-input.rules, then re-login.");
        }

        int count;
        lock (_gate)
        {
            foreach (var old in _devices) Close(old.Fd);
            _devices.Clear();
            _devices.AddRange(opened);
            count = _devices.Count;
        }
        DeviceCountChanged?.Invoke(count);
    }

    private unsafe void ReadLoop()
    {
        var evBuf = new InputEvent[64];
        var pollFds = new PollFd[16];
        while (_running)
        {
            if (_rescanRequested)
            {
                _rescanRequested = false;
                DiscoverDevices(throwOnAccessDenied: false);
            }

            DeviceState[] snapshot;
            lock (_gate) snapshot = _devices.ToArray();
            if (snapshot.Length == 0)
            {
                Thread.Sleep(20);
                continue;
            }

            if (pollFds.Length < snapshot.Length)
                pollFds = new PollFd[snapshot.Length];

            for (int i = 0; i < snapshot.Length; i++)
            {
                pollFds[i].fd = snapshot[i].Fd;
                pollFds[i].events = POLLIN;
                pollFds[i].revents = 0;
            }

            int ready;
            fixed (PollFd* fds = pollFds)
                ready = Poll(fds, (nuint)snapshot.Length, 10);
            if (ready <= 0) continue;

            for (int i = 0; i < snapshot.Length; i++)
            {
                if ((pollFds[i].revents & POLLIN) == 0) continue;
                var dev = snapshot[i];
                fixed (InputEvent* pBuf = evBuf)
                {
                    while (_running)
                    {
                        nint n = Read(dev.Fd, pBuf, (nuint)(evBuf.Length * sizeof(InputEvent)));
                        if (n <= 0) break;
                        int count = (int)(n / sizeof(InputEvent));
                        for (int k = 0; k < count; k++)
                            ProcessEvent(dev, in pBuf[k]);
                    }
                }
            }
        }
    }

    private void ProcessEvent(DeviceState dev, in InputEvent ev)
    {
        if (ev.Type == EV_REL)
        {
            if (ev.Code == REL_X) dev.Dx += ev.Value;
            else if (ev.Code == REL_Y) dev.Dy += ev.Value;
        }
        else if (ev.Type == EV_KEY && ev.Value != 2)
        {
            MouseButtons flag = ev.Code switch
            {
                BTN_LEFT => MouseButtons.Left,
                BTN_RIGHT => MouseButtons.Right,
                BTN_MIDDLE => MouseButtons.Middle,
                BTN_SIDE => MouseButtons.X1,
                BTN_EXTRA => MouseButtons.X2,
                _ => MouseButtons.None
            };
            if (flag != MouseButtons.None)
            {
                int b = Volatile.Read(ref _buttons);
                if (ev.Value != 0) b |= (int)flag;
                else b &= ~(int)flag;
                Volatile.Write(ref _buttons, b);
                dev.ButtonsChanged = true;
            }
        }
        else if (ev.Type == EV_SYN && ev.Code == SYN_REPORT)
        {
            if (dev.Dx != 0 || dev.Dy != 0 || dev.ButtonsChanged)
            {
                long tsUs = ev.Sec * 1_000_000L + ev.Usec;
                _channel.Writer.TryWrite(new RawMotionSample(tsUs, dev.Dx, dev.Dy, CurrentButtons));
                dev.Dx = 0;
                dev.Dy = 0;
                dev.ButtonsChanged = false;
            }
        }
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _readerThread?.Join(500);
        _readerThread = null;
        _watcher?.Dispose();
        _watcher = null;
        lock (_gate)
        {
            foreach (var d in _devices) Close(d.Fd);
            _devices.Clear();
        }
    }

    public void Dispose() => Stop();
}
