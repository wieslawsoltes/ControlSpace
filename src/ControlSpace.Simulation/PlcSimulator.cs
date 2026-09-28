using System.Diagnostics;
using ControlSpace.Core;
using ControlSpace.Languages;

namespace ControlSpace.Simulation;

public enum CpuMode { Stopped, Running, Paused, Faulted }
public sealed record AlarmEvent(Guid EventId, Guid RuleId, string Message, Severity Severity, double RaisedAt, double ChangedAt, bool Active, bool Acknowledged);

/// <summary>Deterministic, local-only scan interpreter. No physical device or network transport is present.</summary>
public sealed class PlcSimulator
{
    private struct InstructionState { public double Elapsed; public bool Previous; public bool Active; public bool Q; }
    private readonly CompiledProject _program;
    private readonly PlcType[] _types;
    private readonly double[] _values, _work;
    private readonly bool[] _output, _input, _previous, _nextPrevious, _contactPower, _nextContactPower, _networkPower, _nextNetworkPower;
    private readonly InstructionState[] _states, _nextStates;
    private readonly Dictionary<Guid, int> _networkIndices = [], _contactIndices = [];
    private readonly Dictionary<int, double> _forces = [];
    private readonly List<AlarmEvent> _alarms = [];
    private readonly int[] _alarmSlots;
    private int[] _traceSlots = [];
    public CpuMode Mode { get; private set; }
    public long ScanCount { get; private set; }
    public double SimulatedMilliseconds { get; private set; }
    public double LastScanMilliseconds { get; private set; }
    public double MaximumScanMilliseconds { get; private set; }
    public string? LastFault { get; private set; }
    public TraceBuffer? Trace { get; private set; }
    public int ScanInstructionBudget { get; init; } = 250000;
    public IReadOnlyList<AlarmEvent> Alarms => _alarms;
    public IReadOnlyList<TagDefinition> Tags => _program.Tags;
    public int ForceCount => _forces.Count;

    public PlcSimulator(CompiledProject program)
    {
        ArgumentNullException.ThrowIfNull(program); _program = program;
        _types = program.Tags.Select(t => t.Type).ToArray(); _values = new double[program.Tags.Length]; _work = new double[_values.Length];
        _output = new bool[_values.Length]; _input = new bool[_values.Length];
        for (var i = 0; i < _values.Length; i++)
        {
            var tag = program.Tags[i];
            if (PlcAddress.TryParse(tag.Address, tag.Type, out var address)) { _output[i] = address.IsOutput; _input[i] = address.IsInput; }
        }
        _previous = new bool[program.ContactCount]; _nextPrevious = new bool[_previous.Length];
        _contactPower = new bool[_previous.Length]; _nextContactPower = new bool[_previous.Length];
        _networkPower = new bool[program.NetworkCount]; _nextNetworkPower = new bool[_networkPower.Length];
        _states = new InstructionState[program.NetworkCount]; _nextStates = new InstructionState[_states.Length];
        foreach (var block in program.Blocks) foreach (var network in block.Networks)
        {
            _networkIndices[network.Id] = network.StateIndex;
            foreach (var path in network.Paths) foreach (var contact in path) _contactIndices[contact.Id] = contact.StateIndex;
        }
        _alarmSlots = program.Alarms.Select(a => program.Symbols[a.Tag]).ToArray();
        var channels = program.Tags.Where(t => t.Name is "ConveyorRun" or "SystemReady" or "BatchCount" or "BeltSpeed").Select(t => t.Name).ToArray();
        if (channels.Length == 0) channels = program.Tags.Take(4).Select(t => t.Name).ToArray();
        if (channels.Length > 0) ConfigureTrace(channels);
        Reset(true);
    }
    private int Slot(string name) => _program.Symbols.TryGetValue(name, out var slot) ? slot : throw new KeyNotFoundException($"Unknown tag '{name}'.");
    public double GetValue(string name) => _values[Slot(name)];
    public bool IsInput(string name) => _input[Slot(name)];
    public bool IsOutput(string name) => _output[Slot(name)];
    public bool IsForced(string name) => _forces.ContainsKey(Slot(name));
    public bool IsNetworkPowered(Guid id) => _networkIndices.TryGetValue(id, out var index) && _networkPower[index];
    public bool IsContactPowered(Guid id) => _contactIndices.TryGetValue(id, out var index) && _contactPower[index];
    public void SetValue(string name, double value)
    {
        var slot = Slot(name);
        if (_output[slot]) throw new InvalidOperationException("Program output tags are read-only in the watch table. Modify inputs or memory instead.");
        if (_forces.ContainsKey(slot)) throw new InvalidOperationException("Remove the input force before modifying its value.");
        _values[slot] = PlcValues.Normalize(_types[slot], value);
    }
    public void ForceInput(string name, double value)
    {
        var slot = Slot(name);
        if (!_input[slot]) throw new InvalidOperationException("Only simulated input tags can be forced.");
        _forces[slot] = PlcValues.Normalize(_types[slot], value); _values[slot] = _forces[slot];
    }
    public void ReleaseForce(string name) => _forces.Remove(Slot(name));
    public void ReleaseAllForces() => _forces.Clear();
    public void Start()
    {
        if (Mode == CpuMode.Faulted) throw new InvalidOperationException("Reset the simulator after a fault before entering RUN.");
        Mode = CpuMode.Running;
    }
    public void Pause() { if (Mode == CpuMode.Running) Mode = CpuMode.Paused; }
    public void Stop()
    {
        Mode = CpuMode.Stopped; _forces.Clear();
        for (var i = 0; i < _values.Length; i++) if (_output[i]) _values[i] = 0;
        Array.Clear(_networkPower); Array.Clear(_contactPower);
    }
    public void Reset(bool cold = true)
    {
        Stop();
        for (var i = 0; i < _values.Length; i++) if (cold || !_program.Tags[i].Retain) _values[i] = PlcValues.Normalize(_types[i], _program.Tags[i].InitialValue);
        // Output process image is de-energized while stopped, even for retained tags.
        for (var i = 0; i < _values.Length; i++) if (_output[i]) _values[i] = 0;
        Array.Clear(_previous); Array.Clear(_states); _alarms.Clear(); Trace?.Clear();
        ScanCount = 0; SimulatedMilliseconds = 0; LastFault = null; LastScanMilliseconds = 0; MaximumScanMilliseconds = 0;
    }
    public void ConfigureTrace(IEnumerable<string> channels, int capacity = 2048)
    {
        var next = new TraceBuffer(channels, capacity); var slots = next.Channels.Select(Slot).ToArray();
        Trace = next; _traceSlots = slots;
    }

    /// <summary>Executes one transactional scan. dt is simulated time, not measured wall-clock time.</summary>
    public bool Step(double milliseconds = 20)
    {
        if (!double.IsFinite(milliseconds) || milliseconds is <= 0 or > 60000) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        if (Mode == CpuMode.Faulted) return false;
        var started = Stopwatch.GetTimestamp();
        Array.Copy(_values, _work, _values.Length); Array.Copy(_previous, _nextPrevious, _previous.Length); Array.Copy(_states, _nextStates, _states.Length);
        Array.Clear(_nextContactPower); Array.Clear(_nextNetworkPower);
        foreach (var force in _forces) _work[force.Key] = force.Value;
        var budget = ScanInstructionBudget;
        try
        {
            foreach (var block in _program.Blocks)
            {
                if (block.Text is not null) budget -= block.Text.Execute(_work, _types, budget);
                else foreach (var network in block.Networks)
                {
                    if (--budget <= 0) throw new InvalidOperationException("Scan instruction budget exceeded.");
                    var power = false;
                    foreach (var path in network.Paths)
                    {
                        var pathPower = true;
                        foreach (var contact in path)
                        {
                            if (--budget <= 0) throw new InvalidOperationException("Scan instruction budget exceeded.");
                            var value = contact.Operand.Read(_work); var other = contact.CompareTo.Read(_work); var bit = value != 0;
                            var enabled = contact.Kind switch
                            {
                                ContactKind.NormallyOpen => bit,
                                ContactKind.NormallyClosed => !bit,
                                ContactKind.RisingEdge => bit && !_previous[contact.StateIndex],
                                ContactKind.FallingEdge => !bit && _previous[contact.StateIndex],
                                ContactKind.Equal => value == other,
                                ContactKind.NotEqual => value != other,
                                ContactKind.Greater => value > other,
                                ContactKind.Less => value < other,
                                ContactKind.GreaterOrEqual => value >= other,
                                ContactKind.LessOrEqual => value <= other,
                                _ => throw new InvalidOperationException("Unsupported contact.")
                            };
                            _nextPrevious[contact.StateIndex] = bit; _nextContactPower[contact.StateIndex] = enabled;
                            pathPower &= enabled; // Deliberately evaluate every edge detector, even on an unpowered path.
                        }
                        power |= pathPower;
                    }
                    _nextNetworkPower[network.StateIndex] = power;
                    Execute(network, power, milliseconds);
                }
            }
            Array.Copy(_work, _values, _values.Length); Array.Copy(_nextPrevious, _previous, _previous.Length); Array.Copy(_nextStates, _states, _states.Length);
            Array.Copy(_nextContactPower, _contactPower, _contactPower.Length); Array.Copy(_nextNetworkPower, _networkPower, _networkPower.Length);
            ScanCount++; SimulatedMilliseconds += milliseconds;
            if (Mode != CpuMode.Running) Mode = CpuMode.Paused;
            CaptureTrace(); UpdateAlarms(); return true;
        }
        catch (Exception e) when (e is ArithmeticException or InvalidOperationException or IndexOutOfRangeException)
        {
            // No working state is committed on failure. STOP also clears physical-style output addresses and forces.
            Stop(); Mode = CpuMode.Faulted; LastFault = e.Message; return false;
        }
        finally
        {
            LastScanMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            MaximumScanMilliseconds = Math.Max(MaximumScanMilliseconds, LastScanMilliseconds);
        }
    }
    private void Write(int slot, double value) => _work[slot] = PlcValues.Normalize(_types[slot], value);
    private void Execute(CompiledNetwork network, bool input, double milliseconds)
    {
        ref var state = ref _nextStates[network.StateIndex];
        var target = network.Target;
        switch (network.Kind)
        {
            case ActionKind.Coil: Write(target, input ? 1 : 0); break;
            case ActionKind.Set: if (input) Write(target, 1); break;
            case ActionKind.Reset: if (input) Write(target, 0); break;
            case ActionKind.TON:
                state.Elapsed = input ? Math.Min(network.Preset, state.Elapsed + milliseconds) : 0;
                state.Q = input && state.Elapsed >= network.Preset; Write(target, state.Q ? 1 : 0); break;
            case ActionKind.TOF:
                if (input) { state.Elapsed = 0; state.Active = false; state.Q = true; }
                else
                {
                    if (state.Previous) { state.Active = true; state.Elapsed = 0; }
                    if (state.Active) { state.Elapsed = Math.Min(network.Preset, state.Elapsed + milliseconds); if (state.Elapsed >= network.Preset) state.Active = false; }
                    state.Q = state.Active;
                }
                Write(target, state.Q ? 1 : 0); break;
            case ActionKind.TP:
                if (input && !state.Previous && !state.Active) { state.Active = network.Preset > 0; state.Elapsed = 0; }
                state.Q = state.Active;
                if (state.Active) { state.Elapsed = Math.Min(network.Preset, state.Elapsed + milliseconds); if (state.Elapsed >= network.Preset) state.Active = false; }
                Write(target, state.Q ? 1 : 0); break;
            case ActionKind.CTU:
                if (network.ResetSlot >= 0 && _work[network.ResetSlot] != 0) Write(target, 0);
                else if (input && !state.Previous) Write(target, Math.Min(_types[target] == PlcType.Int ? short.MaxValue : int.MaxValue, _work[target] + 1));
                if (network.AuxiliarySlot >= 0) Write(network.AuxiliarySlot, _work[target] >= network.Preset ? 1 : 0);
                break;
            case ActionKind.Move: if (input) Write(target, network.InputA.Read(_work)); break;
            case ActionKind.Add: if (input) Write(target, network.InputA.Read(_work) + network.InputB.Read(_work)); break;
            case ActionKind.Subtract: if (input) Write(target, network.InputA.Read(_work) - network.InputB.Read(_work)); break;
            case ActionKind.Multiply: if (input) Write(target, network.InputA.Read(_work) * network.InputB.Read(_work)); break;
            case ActionKind.Divide:
                if (input)
                {
                    var divisor = network.InputB.Read(_work); if (divisor == 0) throw new DivideByZeroException("Division by zero in ladder instruction.");
                    var result = network.InputA.Read(_work) / divisor; Write(target, _types[target] == PlcType.Real ? result : Math.Truncate(result));
                }
                break;
            default: throw new InvalidOperationException("Unsupported instruction.");
        }
        if (network.Kind is ActionKind.TON or ActionKind.TOF or ActionKind.TP && network.AuxiliarySlot >= 0) Write(network.AuxiliarySlot, Math.Truncate(state.Elapsed));
        state.Previous = input;
    }
    private void CaptureTrace()
    {
        if (Trace is null) return;
        Span<double> values = stackalloc double[_traceSlots.Length];
        for (var i = 0; i < values.Length; i++) values[i] = _values[_traceSlots[i]];
        Trace.Add(SimulatedMilliseconds, values);
    }
    private void UpdateAlarms()
    {
        for (var i = 0; i < _program.Alarms.Length; i++)
        {
            var rule = _program.Alarms[i]; var active = (_values[_alarmSlots[i]] != 0) == rule.ActiveWhen;
            var index = _alarms.FindLastIndex(a => a.RuleId == rule.Id && a.Active);
            if (active && index < 0) { _alarms.Add(new(Guid.NewGuid(), rule.Id, rule.Message, rule.Severity, SimulatedMilliseconds, SimulatedMilliseconds, true, false)); if (_alarms.Count > 1000) _alarms.RemoveAt(0); }
            else if (!active && index >= 0) _alarms[index] = _alarms[index] with { Active = false, ChangedAt = SimulatedMilliseconds };
        }
    }
    public void AcknowledgeAll()
    {
        for (var i = 0; i < _alarms.Count; i++) if (!_alarms[i].Acknowledged) _alarms[i] = _alarms[i] with { Acknowledged = true, ChangedAt = SimulatedMilliseconds };
    }
}
