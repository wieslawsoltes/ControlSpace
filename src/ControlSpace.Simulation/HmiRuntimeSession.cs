using System.Globalization;
using ControlSpace.Core;
namespace ControlSpace.Simulation;

/// <summary>Captures the exact scene/controller receiving a pointer press. A release
/// must not dispatch into a different document, screen or controller lifecycle.</summary>
public sealed record HmiRuntimeActivation(ControlProject Project, VirtualPlc Controller, string ScreenId, string ObjectId, long Epoch);

/// <summary>Same-thread simulated operator actions and bounded screen history.
/// The host disposes/replaces this session when the source document or CPU changes.</summary>
public sealed class HmiRuntimeSession : IDisposable
{
    private readonly Dictionary<string, HmiScreen> _screens;
    private readonly Dictionary<string, PlcTag> _tags;
    private readonly HashSet<string> _screenIds;
    private readonly List<string> _history = [];
    private long _generation;
    private bool _disposed;
    public ControlProject Project { get; }
    public VirtualPlc Controller { get; }
    public string ScreenId { get; private set; }
    public bool CanGoBack => _history.Count > 0;
    public const int HistoryLimit = 32;

    public HmiRuntimeSession(ControlProject project, VirtualPlc controller, string screenId)
    {
        ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(controller);
        if (!ProjectSnapshot.ContentEquals(project, controller.Program.Project)) throw new ArgumentException("The HMI scene and compiled controller must come from the same project snapshot.");
        Project = project; Controller = controller;
        _screens = project.Screens.ToDictionary(s => s.Id, StringComparer.Ordinal);
        _tags = project.Tags.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        _screenIds = _screens.Keys.ToHashSet(StringComparer.Ordinal);
        if (!_screens.ContainsKey(screenId)) throw new ArgumentException("The runtime start screen does not exist.");
        ScreenId = screenId;
    }
    private void Guard()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Controller.State == ControllerState.Faulted) throw new InvalidOperationException("Reset the faulted virtual controller before operating HMI controls.");
    }
    private HmiObject Object(string id)
    {
        Guard();
        var o = _screens[ScreenId].Objects.FirstOrDefault(o => o.Id == id) ?? throw new InvalidOperationException("This object is not on the active runtime screen.");
        if (HmiRuntimeRules.Validate(o, name => _tags.GetValueOrDefault(name), _screenIds) is string error) throw new InvalidOperationException(error);
        return o;
    }
    public void ValidateActivation(HmiRuntimeActivation activation)
    {
        Guard();
        if (!ReferenceEquals(activation.Project, Project) || !ReferenceEquals(activation.Controller, Controller) ||
            activation.ScreenId != ScreenId || activation.Epoch != Controller.InteractionEpoch)
            throw new InvalidOperationException("The runtime context changed during the pointer interaction.");
    }
    /// <summary>Invalidate a pending numeric entry on deactivation or interaction cancellation.</summary>
    public void CancelPendingInput() => _generation++;
    public void Navigate(string target)
    {
        Guard();
        if (!_screens.ContainsKey(target)) throw new ArgumentException("The target screen no longer exists.");
        CancelPendingInput(); if (ScreenId == target) return;
        _history.Add(ScreenId); if (_history.Count > HistoryLimit) _history.RemoveAt(0);
        ScreenId = target;
    }
    public void Back()
    {
        Guard(); CancelPendingInput(); if (_history.Count == 0) return;
        ScreenId = _history[^1]; _history.RemoveAt(_history.Count - 1);
    }
    /// <summary>Dispatch one completed button click. Momentary press/release remains
    /// owned by HmiMomentaryInput, and never dispatches as a one-shot write.</summary>
    public void ActivateButton(string objectId)
    {
        var o = Object(objectId);
        if (o.Kind != HmiKind.Button) throw new InvalidOperationException("This object is not a button.");
        var action = HmiRuntimeRules.Action(o); CancelPendingInput();
        if (action == HmiButtonAction.ActivateScreen) { Navigate(o.Runtime!.ScreenId); return; }
        if (action == HmiButtonAction.PreviousScreen) { Back(); return; }
        if (action == HmiButtonAction.Momentary) throw new InvalidOperationException("Momentary buttons require a paired press and release.");
        if (!_tags.TryGetValue(o.Tag, out var tag) || !HmiRuntimeRules.Accepts(o, tag)) throw new InvalidOperationException("The button has no compatible writable tag.");
        double value = action switch
        {
            HmiButtonAction.SetBit => 1,
            HmiButtonAction.ResetBit => 0,
            HmiButtonAction.ToggleBit => Controller.Read(tag.Name) == 0 ? 1 : 0,
            HmiButtonAction.SetValue => o.Runtime!.WriteValue,
            _ => throw new InvalidOperationException("Unsupported runtime action.")
        };
        Controller.WriteHmiValue(tag.Name, value);
    }
    public HmiNumericEntry BeginNumericInput(string objectId)
    {
        var o = Object(objectId);
        if (!HmiRuntimeRules.IsNumericInput(o) || !_tags.TryGetValue(o.Tag, out var tag) || !HmiRuntimeRules.Accepts(o, tag))
            throw new InvalidOperationException("This object is not a writable numeric I/O field.");
        CancelPendingInput();
        return new(this, o, tag, _generation, Controller.InteractionEpoch, ScreenId);
    }
    internal void Commit(HmiNumericEntry entry, string text)
    {
        Guard();
        if (entry.Generation != _generation || entry.Epoch != Controller.InteractionEpoch || entry.Screen != ScreenId)
            throw new InvalidOperationException("The controller, screen or input session changed. Open the field again.");
        double value = HmiRuntimeRules.ParseInput(entry.Object, entry.Tag, text);
        Controller.WriteHmiValue(entry.Tag.Name, value);
        CancelPendingInput();
    }
    public void Dispose() { if (_disposed) return; CancelPendingInput(); _disposed = true; }
}

/// <summary>Uncommitted input buffer: invalid values do not write, and a successful
/// commit can occur only once in the original screen/controller lifecycle.</summary>
public sealed class HmiNumericEntry : IDisposable
{
    private readonly HmiRuntimeSession _session;
    private bool _closed;
    internal long Generation { get; }
    internal long Epoch { get; }
    internal string Screen { get; }
    internal PlcTag Tag { get; }
    public HmiObject Object { get; }
    public string InitialText { get; }
    internal HmiNumericEntry(HmiRuntimeSession session, HmiObject item, PlcTag tag, long generation, long epoch, string screen)
    {
        _session = session; Object = item; Tag = tag; Generation = generation; Epoch = epoch; Screen = screen;
        InitialText = item.Runtime?.IoMode == HmiIoMode.Input ? "" : session.Controller.Read(tag.Name).ToString("G17", CultureInfo.InvariantCulture);
    }
    public void Commit(string text)
    {
        if (_closed) throw new InvalidOperationException("This numeric entry is already closed.");
        _session.Commit(this, text); _closed = true;
    }
    public void Dispose() => _closed = true;
}
