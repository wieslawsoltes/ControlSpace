using System.Collections;
namespace ControlSpace.Simulation;

/// <summary>Bounded append history with optional conservative payload budget.
/// Indexed access avoids allocating a complete history array for each paint.</summary>
public sealed class TraceBuffer : IReadOnlyList<ScanSnapshot>
{
    private readonly ScanSnapshot?[] _items;
    private readonly long[] _sizes;
    private int _next;
    public int Count { get; private set; }
    public int Capacity => _items.Length;
    public long MaximumRetainedBytes { get; }
    public long EstimatedRetainedBytes { get; private set; }
    public long DroppedSamples { get; private set; }
    public TraceBuffer(int capacity) : this(capacity, long.MaxValue) { }
    public TraceBuffer(int capacity, long maximumRetainedBytes)
    {
        if (capacity is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (maximumRetainedBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumRetainedBytes));
        _items = new ScanSnapshot[capacity]; _sizes = new long[capacity]; MaximumRetainedBytes = maximumRetainedBytes;
    }
    public ScanSnapshot this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _items[(_next - Count + Capacity + index) % Capacity]!;
        }
    }
    public void Add(ScanSnapshot sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        long size = 128L + sample.Values.LongLength * sizeof(double);
        foreach (var key in sample.Flow.Keys) size += 64L + key.Length * 2L;
        if (size > MaximumRetainedBytes) { DroppedSamples++; return; }
        while (Count > 0 && (Count == Capacity || EstimatedRetainedBytes > MaximumRetainedBytes - size))
        {
            int first = (_next - Count + Capacity) % Capacity;
            EstimatedRetainedBytes -= _sizes[first]; _sizes[first] = 0; _items[first] = null; Count--; DroppedSamples++;
        }
        _items[_next] = sample; _sizes[_next] = size; EstimatedRetainedBytes += size;
        _next = (_next + 1) % Capacity; Count++;
    }
    /// <summary>Detached list container, retaining the public historical API.</summary>
    public IReadOnlyList<ScanSnapshot> Read()
    {
        var result = new ScanSnapshot[Count];
        for (int i = 0; i < Count; i++) result[i] = this[i];
        return result;
    }
    public IEnumerator<ScanSnapshot> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void Clear() { Array.Clear(_items); Array.Clear(_sizes); Count = _next = 0; EstimatedRetainedBytes = DroppedSamples = 0; }
}
