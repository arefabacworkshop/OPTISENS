using System.Threading.Channels;

namespace OPTISENS.Core.Input;

/// <summary>One OS-level relative motion report. Timestamps are microseconds,
/// monotonic (Windows: QPC; Linux: input_event.time; macOS: mach_absolute_time).</summary>
public readonly record struct RawMotionSample(long TimestampUs, int DxCounts, int DyCounts, MouseButtons Buttons);

[Flags]
public enum MouseButtons { None = 0, Left = 1, Right = 2, Middle = 4, X1 = 8, X2 = 16 }

public sealed class PlatformInputException : Exception
{
    public string Remediation { get; }

    public PlatformInputException(string message, string remediation, Exception? inner = null)
        : base(message, inner)
    {
        Remediation = remediation;
    }
}

public interface IRawInputService : IDisposable
{
    MouseButtons CurrentButtons { get; }          // atomic snapshot for click detection
    bool IsReceiving { get; }                     // >= 1 device delivering samples
    long DroppedSamples { get; }                  // samples discarded by the bounded channel since start
    ChannelReader<RawMotionSample> Samples { get; }
    event Action<int>? DeviceCountChanged;        // drives D20 remediation dialog
    void Start();
    void Stop();
}
