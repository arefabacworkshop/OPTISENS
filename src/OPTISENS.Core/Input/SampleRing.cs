using OPTISENS.Core.Config;

namespace OPTISENS.Core.Input;

/// <summary>Fixed-capacity overwrite-oldest ring of RawMotionSample with monotonic Sequence bookkeeping.</summary>
public sealed class SampleRing
{
    private readonly RawMotionSample[] _buffer;
    private readonly int _mask;
    private readonly object _gate = new();
    private long _sequence;

    public SampleRing(int capacity = Constants.SampleRingCapacity)
    {
        if (capacity <= 0 || (capacity & (capacity - 1)) != 0)
            throw new ArgumentException("Capacity must be a positive power of 2.", nameof(capacity));
        _buffer = new RawMotionSample[capacity];
        _mask = capacity - 1;
    }

    public int Capacity => _buffer.Length;

    public long Sequence
    {
        get { lock (_gate) return _sequence; }
    }

    public void Write(in RawMotionSample sample)
    {
        lock (_gate)
        {
            _buffer[_sequence & _mask] = sample;
            _sequence++;
        }
    }

    /// <summary>Snapshot samples in [fromSeq, toSeq). If overwritten, clamps fromSeq to oldest retained.</summary>
    public RawMotionSample[] SnapshotRange(long fromSeq, long toSeq)
    {
        lock (_gate)
        {
            long end = Math.Min(toSeq, _sequence);
            long earliest = Math.Max(0, _sequence - _buffer.Length);
            long start = Math.Max(fromSeq, earliest);
            if (end <= start) return Array.Empty<RawMotionSample>();

            int count = (int)(end - start);
            var result = new RawMotionSample[count];
            for (int i = 0; i < count; i++)
                result[i] = _buffer[(start + i) & _mask];
            return result;
        }
    }
}
