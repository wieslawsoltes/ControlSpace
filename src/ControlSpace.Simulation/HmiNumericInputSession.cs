using ControlSpace.Core;
namespace ControlSpace.Simulation;

/// <summary>A one-shot operator-entry transaction bound to a project and controller lifecycle.</summary>
public sealed class HmiNumericInputSession
{
    private readonly ControlProject _project;
    private readonly VirtualPlc _controller;
    private readonly long _epoch;
    private bool _completed;
    public HmiObject Object { get; }
    public PlcTag Tag { get; }
    public double InitialValue { get; }
    public HmiNumericInputSession(ControlProject project, VirtualPlc controller, string screenId, string objectId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(controller);
        if (controller.State == ControllerState.Faulted) throw new InvalidOperationException("Reset the faulted simulator before operator input.");
        _project = project; _controller = controller; _epoch = controller.InteractionEpoch;
        if (!ProjectSnapshot.ContentEquals(project, controller.Program.Project))
            throw new InvalidOperationException("Compile the current project before entering values.");
        Object = project.Screens.FirstOrDefault(s => s.Id == screenId)?.Objects.FirstOrDefault(o => o.Id == objectId) ?? throw new ArgumentException("The input object no longer exists.");
        Tag = project.Tags.FirstOrDefault(t => t.Name.Equals(Object.Tag, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Bind this input to a compatible numeric tag first.");
        if (!HmiRuntimeRules.IsNumericInput(Object) || !HmiRuntimeRules.Accepts(Object, Tag)) throw new ArgumentException("This object is not an editable numeric input.");
        InitialValue = controller.Read(Tag.Name);
    }
    public double Commit(ControlProject project, VirtualPlc? controller, string text)
    {
        if (_completed || !ReferenceEquals(project, _project) || !ReferenceEquals(controller, _controller) || _controller.InteractionEpoch != _epoch)
            throw new InvalidOperationException("The project or controller changed. Cancel the entry and reopen it.");
        double value = HmiRuntimeRules.ParseInput(Object, Tag, text);
        _controller.SetOperatorValue(Tag.Name, value); _completed = true; return value;
    }
}
