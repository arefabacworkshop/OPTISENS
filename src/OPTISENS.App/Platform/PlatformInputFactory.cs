using OPTISENS.Core.Input;
using OPTISENS.Input.Linux;
using OPTISENS.Input.Mac;
using OPTISENS.Input.Windows;

namespace OPTISENS.App.Platform;

public static class PlatformInputFactory
{
    public static IRawInputService Create()
    {
        if (OperatingSystem.IsWindows()) return new WindowsRawInputService();
        if (OperatingSystem.IsLinux())   return new EvdevInputService();
        if (OperatingSystem.IsMacOS())   return new HidInputService();
        throw new PlatformInputException(
            "Unsupported operating system for raw mouse input.",
            "OPTISENS supports Windows, Linux (evdev), and macOS (IOHIDManager).");
    }
}
