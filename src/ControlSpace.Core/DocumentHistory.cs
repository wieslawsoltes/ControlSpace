namespace ControlSpace.Core;

/// <summary>Bounded, immutable-snapshot undo/redo with a saved-state identity independent of undo depth.</summary>
public sealed class DocumentHistory<T> where T : class
{
    private readonly List<(T Value, long Identity)> _undo = [];
    private readonly List<(T Value, long Identity)> _redo = [];
    private readonly int _capacity;
    private long _nextIdentity, _identity, _savedIdentity;
    public T Current { get; private set; }
    public long Revision { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsDirty => _identity != _savedIdentity;
    public event EventHandler? Changed;

    public DocumentHistory(T initial, int capacity = 100)
    {
        ArgumentNullException.ThrowIfNull(initial);
        if (capacity is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(capacity));
        Current = initial; _capacity = capacity;
    }

    public void Commit(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (ReferenceEquals(Current, value)) return;
        _undo.Add((Current, _identity));
        if (_undo.Count > _capacity) _undo.RemoveAt(0);
        _redo.Clear(); Current = value; _identity = ++_nextIdentity; Notify();
    }
    public bool Undo()
    {
        if (!CanUndo) return false;
        _redo.Add((Current, _identity));
        (Current, _identity) = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); Notify(); return true;
    }
    public bool Redo()
    {
        if (!CanRedo) return false;
        _undo.Add((Current, _identity));
        (Current, _identity) = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); Notify(); return true;
    }
    public void MarkSaved() { _savedIdentity = _identity; Changed?.Invoke(this, EventArgs.Empty); }
    public void Reset(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Current = value; _undo.Clear(); _redo.Clear(); _identity = ++_nextIdentity; _savedIdentity = _identity; Notify();
    }
    private void Notify() { Revision++; Changed?.Invoke(this, EventArgs.Empty); }
}
