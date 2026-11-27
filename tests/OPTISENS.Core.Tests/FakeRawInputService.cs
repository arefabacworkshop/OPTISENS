using System.Threading.Channels;
using OPTISENS.Core.Input;

namespace OPTISENS.Core.Tests;

public sealed class FakeRawInputService : IRawInputService
{
    private readonly Channel<RawMotionSample> _channel = Channel.CreateUnbounded<RawMotionSample>();
    private int _buttons;
    private bool _running;

    public MouseButtons CurrentButtons => (MouseButtons)Volatile.Read(ref _buttons);
    public bool IsReceiving => _running;
    public long DroppedSamples => 0;
    public ChannelReader<RawMotionSample> Samples => _channel.Reader;
    public event Action<int>? DeviceCountChanged;

    public void Start()
    {
        _running = true;
        DeviceCountChanged?.Invoke(1);
    }

    public void Stop() => _running = false;

    public void Emit(RawMotionSample sample)
    {
        Volatile.Write(ref _buttons, (int)sample.Buttons);
        _channel.Writer.TryWrite(sample);
    }

    public void EmitAll(IEnumerable<RawMotionSample> samples)
    {
        foreach (var s in samples) Emit(s);
    }

    public void Dispose() => Stop();
}
