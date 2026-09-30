using ControlSpace.Core;
namespace ControlSpace.Simulation;

/// <summary>A held input belongs to the controller that received its press, even if the host navigates or recompiles.</summary>
public sealed class HmiMomentaryInput : IDisposable
{
    private VirtualPlc? _controller;
    private string? _tag;
    public bool IsPressed => _controller is not null;
    public bool Press(VirtualPlc? controller, string tag)
    {
        Release(); if (controller is null || controller.State == ControllerState.Faulted) return false;
        var declaration = controller.Program.Project.Tags.FirstOrDefault(t => t.Name.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (declaration is null || declaration.Type != PlcType.Bool || !PlcValues.IsInput(declaration.Address)) return false;
        if (controller.Forces.ContainsKey(controller.FindSlot(declaration.Name))) return false;
        controller.SetInput(declaration.Name, 1); _controller = controller; _tag = declaration.Name; return true;
    }
    public void Release()
    {
        var controller = _controller; string? tag = _tag; _controller = null; _tag = null;
        if (controller is not null && tag is not null) controller.SetInput(tag, 0);
    }
    public void Dispose() => Release();
}
