using System.Runtime.InteropServices;

namespace OPTISENS.Input.Linux;

internal static unsafe partial class LibcNative
{
    internal const int O_RDONLY = 0, O_NONBLOCK = 0x800, O_CLOEXEC = 0x80000;
    internal const int EACCES = 13, EAGAIN = 11;
    internal const short POLLIN = 0x001;
    internal const ushort EV_SYN = 0x00, EV_KEY = 0x01, EV_REL = 0x02;
    internal const ushort SYN_REPORT = 0, REL_X = 0x00, REL_Y = 0x01;
    internal const ushort BTN_LEFT = 0x110, BTN_RIGHT = 0x111, BTN_MIDDLE = 0x112,
                          BTN_SIDE = 0x113, BTN_EXTRA = 0x114;
    internal const uint EVIOCGVERSION = 0x80044501;

    internal static nuint Eviocgbit(int ev, int len)
    {
        // _IOC(_IOC_READ=2, 'E'=0x45, 0x20 + ev, len)
        const uint dir = 2u;
        const uint type = 0x45u;
        uint nr = (uint)(0x20 + ev);
        uint size = (uint)len;
        return (nuint)((dir << 30) | (size << 16) | (type << 8) | nr);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InputEvent
    {
        public long Sec;
        public long Usec;
        public ushort Type;
        public ushort Code;
        public int Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true)]
    internal static partial int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    internal static partial nint Read(int fd, void* buf, nuint count);

    [LibraryImport("libc", EntryPoint = "close")]
    internal static partial int Close(int fd);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    internal static partial int Ioctl(int fd, nuint request, void* arg);

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    internal static partial int Poll(PollFd* fds, nuint nfds, int timeout);
}
