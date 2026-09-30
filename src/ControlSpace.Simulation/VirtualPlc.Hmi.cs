using ControlSpace.Core;
namespace ControlSpace.Simulation;

public sealed partial class VirtualPlc
{
    /// <summary>Changes on Run/Stop/Reset/fault, not ordinary scans. Invalidates pending operator edits.</summary>
    public long InteractionEpoch { get; private set; }

    /// <summary>One simulated HMI write. Inputs update the input image; markers are
    /// one-shot writes and may be overwritten by the next program scan. Not forcing.</summary>
    public void WriteHmiValue(string name, double value)
    {
        if (State == ControllerState.Faulted) throw new InvalidOperationException("Reset the faulted virtual controller before writing.");
        int slot = FindSlot(name); var tag = _program.Project.Tags[slot];
        if (!HmiRuntimeRules.IsWritable(tag)) throw new InvalidOperationException("HMI writes require input or marker tags; output-image writes are not supported.");
        if (_forces.ContainsKey(slot)) throw new InvalidOperationException("Release the simulated force on this tag before an HMI write.");
        ValidateValue(slot, value);
        if (PlcValues.IsInput(tag.Address)) _inputs[slot] = value;
        if (_values[slot] != value) VisualVersion++;
        _values[slot] = value;
    }
}
