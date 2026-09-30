namespace ControlSpace.Simulation;

/// <summary>Read-only synchronous display access. A live view belongs to its controller's thread;
/// use Snapshot when a detached, historical or cross-thread image is required.</summary>
public interface IScanReadView
{
    long Cycle { get; }
    ControllerState State { get; }
    int ValueCount { get; }
    double ReadValue(int slot);
    bool TryGetFlow(string id, out bool active);
}
