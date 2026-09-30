using ControlSpace.Core;
namespace ControlSpace.Simulation;

public sealed partial class VirtualPlc
{
    /// <summary>Changes on controller lifecycle transitions, not ordinary scans. Invalidates pending operator entry.</summary>
    public long InteractionEpoch { get; private set; }
    /// <summary>Write a simulated numeric input/marker once, without creating a force. Program writers may replace marker values on the next scan.</summary>
    public void SetOperatorValue(string name, double value)
    {
        if (State == ControllerState.Faulted) throw new InvalidOperationException("Reset the faulted simulator before operator input.");
        int slot = FindSlot(name); var tag = _program.Project.Tags[slot];
        if (!HmiRuntimeOptions.CanEnter(tag)) throw new InvalidOperationException("Operator numeric input cannot write BOOL or output-image tags.");
        ValidateValue(slot, value);
        if (_forces.ContainsKey(slot)) throw new InvalidOperationException("Release the simulated force before entering a value. Operator input never overrides a force.");
        if (PlcValues.IsInput(tag.Address)) { SetInput(name, value); return; }
        if (_values[slot] != value) { _values[slot] = value; VisualVersion++; }
    }
}
