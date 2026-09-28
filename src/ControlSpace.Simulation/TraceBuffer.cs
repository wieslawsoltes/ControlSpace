namespace ControlSpace.Simulation;

/// <summary>Bounded O(1) append history. The UI selects the channels to display.</summary>
public sealed class TraceBuffer
{
    private readonly ScanSnapshot?[] _items;
    private int _next;
    public int Count { get; private set; }
    public int Capacity => _items.Length;
    public TraceBuffer(int capacity)
    {
        if (capacity is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(capacity)); _items = new ScanSnapshot[capacity];
    }
    public void Add(ScanSnapshot sample) { _items[_next] = sample; _next = (_next + 1) % Capacity; Count = Math.Min(Count + 1, Capacity); }
    public IReadOnlyList<ScanSnapshot> Read()
    {
        var result = new ScanSnapshot[Count]; int start = (_next - Count + Capacity) % Capacity;
        for (int i = 0; i < Count; i++) result[i] = _items[(start + i) % Capacity]!; return result;
    }
    public void Clear() { Array.Clear(_items); Count = _next = 0; }
}
