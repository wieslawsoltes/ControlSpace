using ControlSpace.Core;
using ControlSpace.Simulation;
using Microsoft.UI.Xaml.Input;
namespace ControlSpace.Controls.Uno;

public sealed partial class EngineeringCanvas
{
    private HmiRuntimeActivation? _hmiActivation;
    public event Action<HmiRuntimeActivation>? HmiActivationRequested;

    private void PressHmiRuntime(HmiObject? item, PointerRoutedEventArgs e)
    {
        if (item is null || Controller is not { } controller || controller.State == ControllerState.Faulted || !HmiRuntimeRules.IsInteractive(item)) return;
        bool momentary = item.Kind == HmiKind.Button && HmiRuntimeRules.Action(item) == HmiButtonAction.Momentary;
        if (!CapturePointer(e.Pointer)) return;
        _hmiPointer = e.Pointer.PointerId;
        try
        {
            if (momentary)
            {
                if (!_hmiInput.Press(controller, item.Tag)) { CancelHmiInteraction(); return; }
            }
            else _hmiActivation = new(Project, controller, ScreenId, item.Id, controller.InteractionEpoch);
            HmiRuntimeChanged?.Invoke(); RequestRender();
        }
        catch { CancelHmiInteraction(); throw; }
    }
    private void ReleaseHmiRuntime(PointerRoutedEventArgs e)
    {
        var activation = _hmiActivation;
        var p = e.GetCurrentPoint(this).Position;
        bool inside = p.X >= 22 && p.Y >= 22 && p.X < ActualWidth && p.Y < ActualHeight &&
            activation is not null && HmiHit(HmiPoint(p)) == activation.ObjectId;
        // Drop capture before invoking the host: navigation and dialogs can replace
        // this surface, and cancellation must never dispatch an action recursively.
        CancelHmiInteraction();
        if (inside && activation is not null && HmiRuntime && ReferenceEquals(Project, activation.Project) &&
            ReferenceEquals(Controller, activation.Controller) && ScreenId == activation.ScreenId &&
            Controller.InteractionEpoch == activation.Epoch)
            HmiActivationRequested?.Invoke(activation);
    }
}
