using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ControlSpace.Core;
using ControlSpace.Languages;
namespace ControlSpace.Simulation;

public sealed partial class VirtualPlc
{
    private int[] _writtenSlots = [];
    private uint[] _writtenGeneration = [];
    private uint _generation;
    private int _writtenCount;
    private bool _unknownExpression;
    private void InitializeChangeTracking()
    {
        _writtenSlots = new int[_values.Length]; _writtenGeneration = new uint[_values.Length];
    }
    private void BeginTrackedScan()
    {
        _writtenCount = 0; _unknownExpression = false;
        _generation = unchecked(_generation + 1);
        if (_generation == 0) { Array.Clear(_writtenGeneration); _generation = 1; }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TrackWrite(int slot)
    {
        if (_writtenGeneration[slot] == _generation) return;
        _writtenGeneration[slot] = _generation; _writtenSlots[_writtenCount++] = slot;
    }
    private bool ValuesChanged(double[] working)
    {
        // The public IR permits custom Expression implementations. Such code can
        // mutate any slot, so preserve full-image comparison for that extension case.
        if (_unknownExpression)
            return !MemoryMarshal.AsBytes(_values.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(working.AsSpan()));
        // Every stock instruction/assignment writes through a known slot. Compare
        // final values, not transient writes: multiple writers may cancel each other.
        for (int i = 0; i < _writtenCount; i++)
        {
            int slot = _writtenSlots[i];
            if (_values[slot] != working[slot]) return true;
        }
        return false;
    }
    private static bool KnownPure(Expression expression) => expression switch
    {
        LiteralExpression or TagExpression => true,
        UnaryExpression unary => KnownPure(unary.Operand),
        BinaryExpression binary => KnownPure(binary.Left) && KnownPure(binary.Right),
        _ => false
    };
}
