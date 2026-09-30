using ControlSpace.Core;
namespace ControlSpace.Simulation;

public sealed partial class VirtualPlc
{
    /// <summary>Write a simulated numeric input/marker once, without creating a force. Program writers may replace marker values on the next scan.</summary>
    public void SetOperatorValue(string name, double value)
    {
        int slot = FindSlot(name);
        var tag = _program.Project.Tags[slot];
        if (tag.Type == PlcType.Bool || !HmiRuntimeRules.IsWritable(tag))
            throw new InvalidOperationException("Operator numeric input cannot write BOOL or output-image tags.");
        WriteHmiValue(name, value);
    }
}
