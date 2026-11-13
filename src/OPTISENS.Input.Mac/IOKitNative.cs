using System.Runtime.InteropServices;

namespace OPTISENS.Input.Mac;

internal static unsafe partial class IOKitNative
{
    internal const uint kHIDPage_GenericDesktop = 0x01;
    internal const uint kHIDPage_Button = 0x09;
    internal const uint kHIDUsage_GD_Mouse = 0x02;
    internal const uint kHIDUsage_GD_X = 0x30;
    internal const uint kHIDUsage_GD_Y = 0x31;
    internal const int kIOReturnNotPrivileged = unchecked((int)0xE00002C1);
    internal const uint kCFStringEncodingUTF8 = 0x08000100;
    internal const int kCFNumberSInt32Type = 3;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MachTimebaseInfo
    {
        public uint Numer;
        public uint Denom;
    }

    [LibraryImport("IOKit", EntryPoint = "IOHIDManagerCreate")]
    internal static partial IntPtr IOHIDManagerCreate(IntPtr allocator, uint options);

    [LibraryImport("IOKit", EntryPoint = "IOHIDManagerSetDeviceMatching")]
    internal static partial void IOHIDManagerSetDeviceMatching(IntPtr mgr, IntPtr matching);

    [LibraryImport("IOKit", EntryPoint = "IOHIDManagerOpen")]
    internal static partial int IOHIDManagerOpen(IntPtr mgr, uint options);

    [LibraryImport("IOKit", EntryPoint = "IOHIDManagerClose")]
    internal static partial int IOHIDManagerClose(IntPtr mgr, uint options);

    [LibraryImport("IOKit", EntryPoint = "IOHIDManagerRegisterInputValueCallback")]
    internal static partial void IOHIDManagerRegisterInputValueCallback(
        IntPtr mgr, delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, IntPtr, void> callback, IntPtr context);

    [LibraryImport("IOKit", EntryPoint = "IOHIDManagerScheduleWithRunLoop")]
    internal static partial void IOHIDManagerScheduleWithRunLoop(IntPtr mgr, IntPtr runLoop, IntPtr mode);

    [LibraryImport("IOKit", EntryPoint = "IOHIDValueGetElement")]
    internal static partial IntPtr IOHIDValueGetElement(IntPtr value);

    [LibraryImport("IOKit", EntryPoint = "IOHIDValueGetIntegerValue")]
    internal static partial int IOHIDValueGetIntegerValue(IntPtr value);

    [LibraryImport("IOKit", EntryPoint = "IOHIDElementGetUsagePage")]
    internal static partial uint IOHIDElementGetUsagePage(IntPtr elem);

    [LibraryImport("IOKit", EntryPoint = "IOHIDElementGetUsage")]
    internal static partial uint IOHIDElementGetUsage(IntPtr elem);

    [LibraryImport("CoreFoundation", EntryPoint = "CFDictionaryCreateMutable")]
    internal static partial IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [LibraryImport("CoreFoundation", EntryPoint = "CFDictionarySetValue")]
    internal static partial void CFDictionarySetValue(IntPtr dict, IntPtr key, IntPtr value);

    [LibraryImport("CoreFoundation", EntryPoint = "CFStringCreateWithCString")]
    internal static partial IntPtr CFStringCreateWithCString(IntPtr alloc, [MarshalAs(UnmanagedType.LPUTF8Str)] string cStr, uint encoding);

    [LibraryImport("CoreFoundation", EntryPoint = "CFNumberCreate")]
    internal static partial IntPtr CFNumberCreate(IntPtr allocator, int theType, int* valuePtr);

    [LibraryImport("CoreFoundation", EntryPoint = "CFRelease")]
    internal static partial void CFRelease(IntPtr cf);

    [LibraryImport("CoreFoundation", EntryPoint = "CFRunLoopGetCurrent")]
    internal static partial IntPtr CFRunLoopGetCurrent();

    [LibraryImport("CoreFoundation", EntryPoint = "CFRunLoopRun")]
    internal static partial void CFRunLoopRun();

    [LibraryImport("CoreFoundation", EntryPoint = "CFRunLoopStop")]
    internal static partial void CFRunLoopStop(IntPtr rl);

    [LibraryImport("libSystem.B.dylib", EntryPoint = "mach_absolute_time")]
    internal static partial ulong MachAbsoluteTime();

    [LibraryImport("libSystem.B.dylib", EntryPoint = "mach_timebase_info")]
    internal static partial int MachTimebaseInfoFn(out MachTimebaseInfo info);
}
