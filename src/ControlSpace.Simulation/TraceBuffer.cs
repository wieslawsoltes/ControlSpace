namespace ControlSpace.Simulation;

/// <summary>A fixed-capacity, allocation-free circular buffer with up to eight selected channels.</summary>
public sealed class TraceBuffer
{
    private readonly double[] _time;
    private readonly double[][] _values;
    private int _next;
    public IReadOnlyList<string> Channels { get; }
    public int Capacity => _time.Length;
    public int Count { get; private set; }
    public TraceBuffer(IEnumerable<string> channels, int capacity = 2048)
    {
        if (capacity is < 2 or > 100000) throw new ArgumentOutOfRangeException(nameof(capacity));
        var names = channels.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length is < 1 or > 8) throw new ArgumentException("Select between one and eight trace channels.", nameof(channels));
        Channels = names; _time = new double[capacity]; _values = names.Select(_ => new double[capacity]).ToArray();
    }
    public void Add(double milliseconds, ReadOnlySpan<double> values)
    {
        if (values.Length != Channels.Count) throw new ArgumentException("Channel count mismatch.", nameof(values));
        _time[_next] = milliseconds;
        for (var i = 0; i < values.Length; i++) _values[i][_next] = values[i];
        _next = (_next + 1) % Capacity; Count = Math.Min(Count + 1, Capacity);
    }
    private int Index(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        return (_next - Count + index + Capacity) % Capacity;
    }
    public double TimeAt(int index) => _time[Index(index)];
    public double ValueAt(int channel, int index) => _values[channel][Index(index)];
    public void Clear() { _next = 0; Count = 0; }
}
