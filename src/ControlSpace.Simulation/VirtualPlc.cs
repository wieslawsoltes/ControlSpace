using System.Diagnostics;
using ControlSpace.Core;
using ControlSpace.Languages;
namespace ControlSpace.Simulation;

public enum ControllerState { Stopped, Running, Faulted }
public sealed record ScanSnapshot(long Cycle, double VirtualMilliseconds, double CpuMilliseconds, ControllerState State, double[] Values, IReadOnlyDictionary<string, bool> Flow, string? Fault) : IScanReadView
{
    public int ValueCount => Values.Length;
    public double ReadValue(int slot) => Values[slot];
    public bool TryGetFlow(string id, out bool active) => Flow.TryGetValue(id, out active);
}

/// <summary>Deterministic, in-process training runtime. Never connects to a PLC or physical output.</summary>
public sealed class VirtualPlc
{
    private sealed class InstructionState { public double Elapsed; public bool Previous, Active; }
    private readonly Dictionary<string, InstructionState> _memory = new(StringComparer.Ordinal);
    private readonly Dictionary<int, double> _inputs = [];
    private readonly Dictionary<int, double> _forces = [];
    private readonly CompiledProgram _program;
    private double[] _values, _working;
    private Dictionary<string, bool> _nextFlow = new(StringComparer.Ordinal);
    private sealed class LiveView(VirtualPlc owner) : IScanReadView
    {
        public long Cycle => owner.Cycle;
        public ControllerState State => owner.State;
        public int ValueCount => owner._values.Length;
        public double ReadValue(int slot) => owner._values[slot];
        public bool TryGetFlow(string id, out bool active) => owner._flow.TryGetValue(id, out active);
    }
    public IScanReadView ReadView { get; }
    /// <summary>Changes only when displayed values, power flow or operating state changes.</summary>
    public long VisualVersion { get; private set; }
    public long SnapshotCopies { get; private set; }
    public bool TraceEnabled { get; set; } = true;
    private Dictionary<string, bool> _flow = [];
    public ControllerState State { get; private set; }
    public string? Fault { get; private set; }
    public long Cycle { get; private set; }
    public double VirtualMilliseconds { get; private set; }
    public double LastCpuMilliseconds { get; private set; }
    public CompiledProgram Program => _program;
    public TraceBuffer Trace { get; } = new(2048, 32L * 1024 * 1024);
    public IReadOnlyDictionary<int, double> Forces => _forces;
    public VirtualPlc(CompiledProgram program)
    {
        _program = program; _values = program.Project.Tags.Select(t => t.InitialValue).ToArray();
        _working = new double[_values.Length]; ReadView = new LiveView(this);
        for (int i = 0; i < _values.Length; i++) if (PlcValues.IsInput(program.Project.Tags[i].Address)) _inputs[i] = _values[i];
        ClearOutputs();
    }
    public int FindSlot(string name) => _program.Symbols.TryGetValue(name, out int slot) ? slot : throw new ArgumentException("Unknown tag: " + name, nameof(name));
    public double Read(string name) => _values[FindSlot(name)];
    public ScanSnapshot Snapshot()
    {
        SnapshotCopies++;
        return new(Cycle, VirtualMilliseconds, LastCpuMilliseconds, State, (double[])_values.Clone(), new Dictionary<string, bool>(_flow), Fault);
    }
    public void SetInput(string name, double value)
    {
        int slot = FindSlot(name); var tag = _program.Project.Tags[slot];
        if (!PlcValues.IsInput(tag.Address)) throw new InvalidOperationException("Only %I tags can be written through SetInput.");
        ValidateValue(slot, value); _inputs[slot] = value; if (_values[slot] != value) VisualVersion++; _values[slot] = value;
    }
    public void Force(string name, double value)
    {
        int slot = FindSlot(name); ValidateValue(slot, value); _forces[slot] = value;
    }
    public void Release(string name) => _forces.Remove(FindSlot(name));
    public void ReleaseAll() => _forces.Clear();
    public void Run()
    {
        if (State == ControllerState.Faulted) throw new InvalidOperationException("Reset the virtual controller before restarting after a fault.");
        if (State != ControllerState.Running) VisualVersion++; State = ControllerState.Running;
    }
    public void Stop()
    {
        if (State != ControllerState.Faulted) State = ControllerState.Stopped; _forces.Clear(); _memory.Clear(); _flow.Clear(); ClearOutputs(); VisualVersion++;
    }
    public void Reset(bool retain = false)
    {
        Stop(); State = ControllerState.Stopped; Fault = null; Cycle = 0; VirtualMilliseconds = LastCpuMilliseconds = 0; Trace.Clear();
        for (int i = 0; i < _values.Length; i++)
        {
            var tag = _program.Project.Tags[i];
            if (!retain || !tag.Retain) _values[i] = tag.InitialValue;
            if (PlcValues.IsInput(tag.Address)) _inputs[i] = _values[i];
        }
        ClearOutputs();
    }
    public bool Step(TimeSpan period, bool singleStep = false)
    {
        double milliseconds = period.TotalMilliseconds;
        if (!double.IsFinite(milliseconds) || milliseconds != Math.Truncate(milliseconds) || milliseconds is <= 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(period), "Scan period must be an integer in 1..1000 ms.");
        if (State == ControllerState.Faulted || State != ControllerState.Running && !singleStep) return false;
        long start = Stopwatch.GetTimestamp();
        var working = _working; Array.Copy(_values, working, _values.Length);
        var flow = _nextFlow; flow.Clear();
        foreach (var (slot, value) in _inputs) working[slot] = value;
        foreach (var (slot, value) in _forces) working[slot] = value;
        try
        {
            foreach (var block in _program.Blocks)
            {
                foreach (var network in block.Networks)
                {
                    bool power = false;
                    foreach (var path in network.Branches)
                    {
                        bool branch = true;
                        foreach (var contact in path)
                        {
                            double operand = working[contact.Slot]; bool active = Contact(contact, operand);
                            branch &= active; flow[contact.Id] = branch;
                        }
                        power |= branch;
                    }
                    flow[network.Id] = power; ExecuteOutput(network.Output, power, milliseconds, working); flow[network.Output.Id] = working[network.Output.Slot] != 0;
                }
                ExecuteStatements(block.Statements, working);
            }
            foreach (var (slot, value) in _forces) working[slot] = value;
            for (int slot = 0; slot < working.Length; slot++) ValidateValue(slot, working[slot]);
            bool changed = !_values.AsSpan().SequenceEqual(working) || _flow.Count != flow.Count;
            if (!changed) foreach (var item in flow) if (!_flow.TryGetValue(item.Key, out bool old) || old != item.Value) { changed = true; break; }
            if (changed) VisualVersion++;
            _working = _values; _values = working; _nextFlow = _flow; _flow = flow; Cycle++; VirtualMilliseconds += milliseconds;
            LastCpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (TraceEnabled) Trace.Add(new(Cycle, VirtualMilliseconds, LastCpuMilliseconds, State, (double[])_values.Clone(), new Dictionary<string, bool>(_flow), null));
            return true;
        }
        catch (Exception ex) when (ex is ArithmeticException or InvalidOperationException or ArgumentException)
        {
            // No partial scan is committed. Physical-style outputs are forced to zero.
            VisualVersion++; State = ControllerState.Faulted; Fault = ex.Message; _forces.Clear(); _memory.Clear(); _flow.Clear(); ClearOutputs();
            LastCpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds; return false;
        }
    }
    private InstructionState Memory(string id)
    {
        if (!_memory.TryGetValue(id, out var state)) _memory[id] = state = new(); return state;
    }
    private bool Contact(CompiledContact c, double value)
    {
        bool bit = value != 0;
        if (c.Kind is InstructionKind.RisingEdge or InstructionKind.FallingEdge)
        {
            var state = Memory(c.Id); bool result = c.Kind == InstructionKind.RisingEdge ? bit && !state.Previous : !bit && state.Previous;
            state.Previous = bit; return result;
        }
        return c.Kind switch
        {
            InstructionKind.Contact => bit, InstructionKind.NegatedContact => !bit,
            InstructionKind.Greater => value > c.Parameter, InstructionKind.Less => value < c.Parameter,
            InstructionKind.Equal => value == c.Parameter, _ => throw new InvalidOperationException("Unsupported contact.")
        };
    }
    private void ExecuteOutput(CompiledOutput output, bool power, double period, double[] values)
    {
        var state = Memory(output.Id);
        switch (output.Kind)
        {
            case InstructionKind.Coil: values[output.Slot] = power ? 1 : 0; break;
            case InstructionKind.SetCoil: if (power) values[output.Slot] = 1; break;
            case InstructionKind.ResetCoil: if (power) values[output.Slot] = 0; break;
            case InstructionKind.Move: if (power) values[output.Slot] = output.Parameter; break;
            case InstructionKind.TimerOn:
                state.Elapsed = power ? Math.Min(output.Parameter, state.Elapsed + period) : 0;
                values[output.Slot] = power && state.Elapsed >= output.Parameter ? 1 : 0;
                if (output.AuxiliarySlot >= 0) values[output.AuxiliarySlot] = state.Elapsed;
                break;
            case InstructionKind.TimerOff:
                if (power) { state.Elapsed = 0; state.Active = true; }
                else if (state.Active) { state.Elapsed = Math.Min(output.Parameter, state.Elapsed + period); if (state.Elapsed >= output.Parameter) state.Active = false; }
                values[output.Slot] = state.Active ? 1 : 0;
                if (output.AuxiliarySlot >= 0) values[output.AuxiliarySlot] = state.Elapsed;
                break;
            case InstructionKind.Pulse:
                if (power && !state.Previous && !state.Active) { state.Active = true; state.Elapsed = 0; }
                bool pulse = state.Active && state.Elapsed < output.Parameter;
                if (state.Active) { state.Elapsed = Math.Min(output.Parameter, state.Elapsed + period); if (state.Elapsed >= output.Parameter) state.Active = false; }
                values[output.Slot] = pulse ? 1 : 0;
                if (output.AuxiliarySlot >= 0) values[output.AuxiliarySlot] = state.Elapsed;
                break;
            case InstructionKind.CountUp:
                if (output.AuxiliarySlot >= 0 && values[output.AuxiliarySlot] != 0) values[output.Slot] = 0;
                else if (power && !state.Previous) values[output.Slot] = Math.Min(_program.Project.Tags[output.Slot].Type == PlcType.Int ? short.MaxValue : int.MaxValue, values[output.Slot] + 1);
                break;
            default: throw new InvalidOperationException("Unsupported output instruction.");
        }
        state.Previous = power;
    }
    private void ExecuteStatements(IReadOnlyList<Statement> statements, double[] values)
    {
        foreach (var statement in statements) switch (statement)
        {
            case AssignmentStatement assignment:
                double value = assignment.Value.Evaluate(values); ValidateValue(assignment.Slot, value); values[assignment.Slot] = value; break;
            case IfStatement conditional:
                ExecuteStatements(conditional.Condition.Evaluate(values) != 0 ? conditional.Then : conditional.Else, values); break;
        }
    }
    private void ValidateValue(int slot, double value)
    {
        if (!PlcValues.IsValid(_program.Project.Tags[slot].Type, value)) throw new ArgumentOutOfRangeException(nameof(value), $"Value for '{_program.Project.Tags[slot].Name}' is out of range or has the wrong type.");
    }
    private void ClearOutputs()
    {
        for (int i = 0; i < _values.Length; i++) if (PlcValues.IsOutput(_program.Project.Tags[i].Address)) _values[i] = 0;
    }
}
