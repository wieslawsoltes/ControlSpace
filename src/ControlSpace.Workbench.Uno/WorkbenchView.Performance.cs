using System.Diagnostics;
using ControlSpace.Core;
using ControlSpace.Controls.Uno;
using ControlSpace.Languages;
using ControlSpace.Storage;
using ControlSpace.Simulation;
using Microsoft.UI.Xaml;
namespace ControlSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private ControlProject? _shownProject, _lastCompileProject, _lastRecoveryProject, _sceneProject;
    private string? _shownView, _toolbarKey;
    private CompilationResult? _lastCompilation;
    private (ControlProject? Project, string Card, string Filter)? _paletteKey;
    private VirtualPlc? _sceneController, _statusController;
    private EditorMode _sceneMode;
    private long _sceneVersion = -1, _lastMetricTime;
    private string? _lastRecoverySource, _lastRecoverySourceId;
    private ControllerState? _statusState;
    public long RecoverySerializations { get; private set; }
    public long EditorBuilds { get; private set; }
    public long PaletteBuilds { get; private set; }
    public long ToolbarBuilds { get; private set; }

    // Workbench command chrome is compact; reusable table/dialog controls retain
    // their standard button styling. Automation IDs and accessible captions stay intact.
    private static Microsoft.UI.Xaml.Controls.Button Button(string text, Action action, string? automationId = null, string? tip = null)
    {
        if (automationId is "new" or "open" or "save" or "undo" or "redo" or "compile" or "run" or "stop" or "step" or "monitor"
            or "add-network" or "add-contact" or "add-nc-contact" or "ladder-branch" or "ladder-collapse-all" or "ladder-expand-all"
            or "ladder-network-up" or "ladder-network-down" or "zoom-in" or "zoom-out" or "zoom-reset" or "block-properties")
            return EngineeringTheme.ToolButton(text, action, automationId);
        return EngineeringTheme.Button(text, action, automationId, tip);
    }

    private void UpdateEditorTools()
    {
        var language = _workspace.Project.Blocks.FirstOrDefault(b => "block:" + b.Id == _view)?.Language;
        string key = _view + "|" + language + "|" + ViewTitle(_view);
        if (_toolbarKey == key) return;
        _toolbarKey = key; ToolbarBuilds++;
        _editorTools.Children.Clear(); BuildEditorTools();
    }
    private bool PaletteChanged()
    {
        string card = _layout.TaskCard == "Instructions"
            ? IsHmiView ? "HMI" : _view == "devices" ? "Devices" : "Instructions"
            : _layout.TaskCard;
        var key = (card is "Libraries" or "Devices" or "HMI" ? _workspace.Project : null, card == "HMI" ? card + _view + _hmiObjectCard : card, _paletteFilter);
        if (_paletteKey == key) return false;
        _paletteKey = key; return true;
    }
    private void UpdateRuntime()
    {
        var c = _workspace.Controller;
        UpdateHmiChrome();
        if (_source is not null) _source.IsReadOnly = c?.State == ControllerState.Running;
        _canvas.Controller = c;
        bool visible = _editor.Content == _graphics && _body.Visibility == Visibility.Visible;
        if (visible)
        {
            // Trace depends on sample time even when the image is unchanged. Device
            // topology is offline and does not depend on the PLC image at all.
            long version = _canvas.Mode == EditorMode.Trace ? c?.Cycle ?? 0
                : _canvas.Mode == EditorMode.Devices ? 0 : c?.VisualVersion ?? 0;
            if (!ReferenceEquals(_sceneProject, _workspace.Project) || !ReferenceEquals(_sceneController, c) ||
                _sceneMode != _canvas.Mode || _sceneVersion != version)
            {
                _sceneProject = _workspace.Project; _sceneController = c; _sceneMode = _canvas.Mode; _sceneVersion = version;
                _canvas.RequestRender();
            }
        }
        else { _sceneProject = null; _sceneVersion = -1; }
        if (_editor.Content == _table && _view is "tags" or "watch")
            _table.UpdateValues(_workspace.Project.Tags, c);
        bool stateChanged = !ReferenceEquals(c, _statusController) || c?.State != _statusState;
        long now = Stopwatch.GetTimestamp();
        if (stateChanged || _lastMetricTime == 0 || Stopwatch.GetElapsedTime(_lastMetricTime, now).TotalMilliseconds >= 250)
        {
            _lastMetricTime = now;
            _metrics.Text = $"{c?.State.ToString().ToUpperInvariant() ?? "STOP"}  ·  {c?.Cycle ?? 0} scans  ·  CPU {c?.LastCpuMilliseconds ?? 0:0.000} ms";
        }
        if (stateChanged)
        {
            _statusController = c; _statusState = c?.State;
            _status.Text = c?.Fault is string fault ? "Simulation fault: " + fault
                : "Simulation only · No hardware connection · Uno / Skia host renderer";
        }
    }
    private async Task SaveAsync()
    {
        try
        {
            CommitSource();
            var exported = ProjectSnapshot.Clone(_workspace.Project);
            if (await _files.SaveAsync(ProjectStorage.Serialize(exported), exported.Name + ".controlspace"))
            {
                // A newer edit can arrive while a platform picker is open.
                _workspace.MarkSaved(exported); _status.Text = "Project exported successfully.";
            }
        }
        catch (Exception ex) { Message("Save failed: " + ex.Message); }
    }
    private async Task RecoverAsync()
    {
        if (_savingRecovery) return;
        CaptureEditorState();
        var project = _workspace.Project;
        string? draft = _source?.Text, draftId = _source is null ? null : _view;
        // Reference identity survives repeated scan/selection ticks, unlike serializing
        // the full project to discover that absolutely nothing has changed.
        if (ReferenceEquals(_lastRecoveryProject, project) && _lastRecoverySource == draft && _lastRecoverySourceId == draftId) return;
        var recovery = ProjectSnapshot.Clone(project);
        for (int i = 0; i < recovery.Blocks.Count; i++)
            if (_editorStates.TryGetValue("block:" + recovery.Blocks[i].Id, out var state) && state.BaseSource == recovery.Blocks[i].Source)
                recovery.Blocks[i] = recovery.Blocks[i] with { Source = state.Source };
        string json = ProjectStorage.Serialize(recovery); RecoverySerializations++;
        _savingRecovery = true;
        try
        {
            if (json != _lastRecovery) await _files.WriteRecoveryAsync(json);
            _lastRecovery = json; _lastRecoveryProject = project; _lastRecoverySource = draft; _lastRecoverySourceId = draftId;
        }
        catch (Exception ex) { _status.Text = "Recovery unavailable: " + ex.Message; }
        finally { _savingRecovery = false; }
    }
}
