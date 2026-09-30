using System.Globalization;
using ControlSpace.Controls.Uno;
using ControlSpace.Core;
using ControlSpace.Engineering;
using ControlSpace.Simulation;
using ControlSpace.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using static ControlSpace.Controls.Uno.EngineeringTheme;
namespace ControlSpace.Workbench.Uno;

/// <summary>Independent industrial engineering workbench composed of public reusable controls.</summary>
public sealed partial class WorkbenchView : UserControl, IDisposable
{
    private readonly Workspace _workspace = new(DemoProject.Create());
    private readonly IProjectFiles _files;
    private readonly ProjectTree _tree = new();
    private readonly EngineeringCanvas _canvas = new();
    private readonly EngineeringViewport _graphics;
    private readonly EngineeringTable _table = new();
    private readonly ContentControl _editor = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly StackPanel _properties = new() { Spacing = 5, Padding = new Thickness(10, 6, 10, 6) };
    private readonly StackPanel _messages = new() { Spacing = 3, Padding = new Thickness(8) };
    private readonly TextBlock _title = Label("ControlSpace", 13, "FFFFFF", true);
    private readonly TextBlock _breadcrumb = Label("Conveyor_Line  ›  PLC_1  ›  Program blocks  ›  Main [OB1]", 12, "FFFFFF");
    private readonly TextBlock _status = Label("Ready · Simulation only · No hardware connection", 11);
    private readonly TextBlock _metrics = Label("STOP · 0 scans", 11);
    private readonly DispatcherTimer _scan = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Grid _body = new();
    private TextBox? _source;
    private string _view = "block:main", _selection = "", _lastRecovery = "";
    private bool _hmiRuntime, _savingRecovery;
    public Workspace Workspace => _workspace;
    public WorkbenchView(IProjectFiles files)
    {
        _files = files; _graphics = new(_canvas); RequestedTheme = ElementTheme.Light;
        AutomationProperties.SetName(this, "ControlSpace engineering workspace");
        _table.Bind(_workspace);
        BuildShell();
        _workspace.Changed += WorkspaceChanged;
        _tree.OpenRequested += target => { if (target == "command:add-block") _ = EditBlockAsync(null); else Safe(() => Navigate(target)); };
        _tree.BlockCommandRequested += ProgramCommand;
        _programs.CommandRequested += ProgramCommand;
        _tree.SelectionChanged += text => _details.Text = text;
        _canvas.Selected += Select;
        _canvas.EditRequested += id => _ = EditLadderAsync(id);
        _canvas.ShortcutRequested += LadderCommand;
        _canvas.ContextMenuRequested += LadderContext;
        _canvas.InstructionDropped += (kind, id, snapshot) => ApplyLadderInstruction(kind, id, snapshot);
        _canvas.ViewportChanged += () => _zoomLabel.Text = _canvas.Mode == EditorMode.Hmi ? $"{_canvas.HmiTransform.Scale * 100:0}%" : $"{_canvas.Zoom * 100:0}%";
        _canvas.MoveRequested += MoveObject;
        _canvas.HmiInput += (tag, value) => Safe(() => { EnsureController(); _workspace.Controller!.SetInput(tag, value ? 1 : 0); UpdateRuntime(); });
        _table.TagSelected += ShowTag;
        _scan.Tick += (_, _) => { var controller = _workspace.Controller; if (controller?.State != ControllerState.Running) { _scan.Stop(); return; } controller.Step(TimeSpan.FromMilliseconds(100)); UpdateRuntime(); };
        _autosave.Tick += async (_, _) => await RecoverAsync(); _autosave.Start();
        ConfigureHmi(); ConfigureShortcuts();
        SizeChanged += (_, _) => ApplyLayout();
        Refresh();
    }
    private static void AddMenu(StackPanel panel, string caption, (string, Action)[] items)
    {
        var button = Button(caption, () => { }); button.BorderThickness = new Thickness(0); button.MinHeight = 25; button.Padding = new Thickness(9, 2, 9, 2);
        var flyout = new MenuFlyout(); foreach (var (text, action) in items) { var item = new MenuFlyoutItem { Text = text }; item.Click += (_, _) => action(); flyout.Items.Add(item); } button.Flyout = flyout; panel.Children.Add(button);
    }
    private void WorkspaceChanged(object? sender, EventArgs e) => Refresh();
    private void Refresh()
    {
        CaptureEditorState();
        if (_projectId != _workspace.Project.Id)
        {
            _projectId = _workspace.Project.Id; _documents.Clear(); _editorStates.Clear(); _canvas.ResetProjectView();
            _view = _workspace.Project.Blocks.Count > 0 ? "block:" + _workspace.Project.Blocks[0].Id : "";
        }
        foreach (var document in _documents.Documents.ToArray()) if (!IsValidView(document.Id)) { _documents.Close(document.Id); _editorStates.Remove(document.Id); }
        if (!IsValidView(_view)) _view = _documents.ActiveId ?? "";
        _tree.SetProject(_workspace.Project); _title.Text = "ControlSpace — " + _workspace.Project.Name + (_workspace.IsDirty ? " *" : "");
        _canvas.Project = _workspace.Project; _canvas.Controller = _workspace.Controller; _canvas.HmiRuntime = _hmiRuntime;
        ShowView();
        if (!ReferenceEquals(_lastCompilation, _workspace.Compilation) || !ReferenceEquals(_lastCompileProject, _workspace.Project))
        { _lastCompilation = _workspace.Compilation; _lastCompileProject = _workspace.Project; ShowCompilation(); }
        ShowPalette(); UpdateDocuments(); UpdateRuntime();
    }
    public void Navigate(string view)
    {
        CommitSource(); CaptureEditorState();
        if (view != _view) { _canvas.CancelHmiInteraction(); _hmiRuntime = false; _canvas.HmiRuntime = false; }
        if (view == "portal") { ShowPortal(); return; }
        _portal.Visibility = Visibility.Collapsed; _body.Visibility = Visibility.Visible; _projectDrawer = _taskDrawer = false;
        _view = view == "hmi" ? FirstHmiView() : view; _selection = ""; _canvas.Selection = null;
        ShowView(); ShowPalette(); ShowGeneralProperties(); if (IsHmiView) ShowHmiProperties(); UpdateDocuments(); ApplyLayout();
        if (_editor.Content == _graphics) _canvas.RequestRender();
    }
    private void ShowView()
    {
        var currentBlock = _workspace.Project.Blocks.FirstOrDefault(b => "block:" + b.Id == _view);
        if (_shownView == _view && ReferenceEquals(_shownProject, _workspace.Project) && _view != "diagnostics" &&
            (currentBlock?.Language != BlockLanguage.SCL || _source is not null)) return;
        // Compile/save notifications keep the same document. A committed SCL draft can
        // keep its native TextBox and undo/caret state instead of tearing it down.
        if (_shownView == _view && _source is not null && currentBlock?.Language == BlockLanguage.SCL &&
            (_sourceBase == currentBlock.Source || _source.Text == currentBlock.Source))
        { _sourceBase = currentBlock.Source; _shownProject = _workspace.Project; _breadcrumb.Text = _workspace.Project.Name + "  ›  " + ViewPath(_view); UpdateEditorTools(); return; }
        _shownView = _view; _shownProject = _workspace.Project; EditorBuilds++;
        _source = null;
        _breadcrumb.Text = _workspace.Project.Name + "  ›  " + ViewPath(_view);
        UpdateEditorTools();
        if (_view.Length == 0) { ShowEmptyEditor(); return; }
        if (_view.StartsWith("block:"))
        {
            var block = _workspace.Project.Blocks.Find(b => b.Id == _view[6..]);
            if (block is null) { _editor.Content = Label("Block not found."); return; }
            if (block.Language == BlockLanguage.SCL)
            {
                var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
                grid.Children.Add(Header("SCL subset: assignments, IF / ELSE, arithmetic and Boolean expressions"));
                _sourceBase = block.Source;
                var state = _editorStates.GetValueOrDefault(_view);
                // AcceptsReturn must precede Text: a single-line TextBox coerces multiline source.
                _source = new TextBox { AcceptsReturn = true, IsReadOnly = _workspace.Controller?.State == ControllerState.Running, Text = state is not null && state.BaseSource == block.Source ? state.Source : block.Source, TextWrapping = TextWrapping.NoWrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 14, Padding = new Thickness(14), VerticalAlignment = VerticalAlignment.Stretch };
                AutomationProperties.SetName(_source, "SCL source editor"); AutomationProperties.SetAutomationId(_source, "scl-source");
                if (state is not null) _source.Select(Math.Clamp(state.Caret, 0, _source.Text.Length), 0);
                Grid.SetRow(_source, 1); grid.Children.Add(_source); _editor.Content = grid;
            }
            else { _canvas.Mode = EditorMode.Ladder; _canvas.BlockId = block.Id; RestoreCanvasState(); _editor.Content = _graphics; }
        }
        else if (_view is "tags" or "watch") { _table.SetTags(_workspace.Project.Tags, _workspace.Controller, _view == "watch"); _editor.Content = _table; }
        else if (_view == "references") { _table.SetReferences(_workspace.CrossReferences()); _editor.Content = _table; }
        else if (_view == "portal") ShowPortal();
        else if (_view == "diagnostics") ShowDiagnostics();
        else if (_view == "blocks") { _programs.SetProject(_workspace.Project); _editor.Content = _programs; }
        else if (_view == "library") ShowLibrary();
        else if (_view == "screens") ShowHmiDirectory();
        else { _canvas.Mode = _view == "devices" ? EditorMode.Devices : IsHmiView ? EditorMode.Hmi : EditorMode.Trace; _canvas.ScreenId = IsHmiView ? _view[4..] : ""; if (IsHmiView) _canvas.BindHmiScreen(); _editor.Content = _graphics; }
        _graphics.Refresh();
        _tree.SetActive(_view);
        _canvas.RequestRender();
    }
    private void CommitSource()
    {
        if (!_table.TryCommitEdit()) throw new InvalidOperationException("Finish or cancel the invalid tag-table cell edit first.");
        if (_source is null || !_view.StartsWith("block:")) return;
        var source = _source.Text; string id = _view[6..]; var block = _workspace.Project.Blocks.Find(b => b.Id == id);
        if (block is null || block.Source == source) return;
        _workspace.Edit("Edit SCL source", p => { int i = p.Blocks.FindIndex(b => b.Id == id); p.Blocks[i] = p.Blocks[i] with { Source = source }; });
    }
    private void ShowDiagnostics()
    {
        var p = new StackPanel { Spacing = 10, Padding = new Thickness(20) }; var controller = _workspace.Controller;
        p.Children.Add(Label("Virtual CPU — online & diagnostics", 22, "008C95", true));
        foreach (string text in new[] { "Interface: in-process simulation. No network transport.", "Operating state: " + (controller?.State.ToString() ?? "Not compiled"), "Virtual cycle: 100 ms. UI scheduling is not real-time.", "Completed scans: " + (controller?.Cycle ?? 0), "Last measured CPU scan: " + (controller?.LastCpuMilliseconds ?? 0).ToString("0.000") + " ms", "Fault: " + (controller?.Fault ?? "None"), "Force entries: " + (controller?.Forces.Count ?? 0), "Trace buffer: 2048 snapshots maximum" }) p.Children.Add(Label(text, 13));
        p.Children.Add(Button("Cold reset", ResetController)); p.Children.Add(Button("Release all simulated forces", () => { controller?.ReleaseAll(); ShowDiagnostics(); })); _editor.Content = new ScrollViewer { Content = p };
    }
    private void ShowLibrary()
    {
        var p = new StackPanel { Padding = new Thickness(22), Spacing = 12 }; p.Children.Add(Label("Local instruction library", 23, "008C95", true));
        p.Children.Add(Label("Use the instruction palette to insert contacts into the selected LAD network.", 13));
        foreach (var kind in Enum.GetValues<InstructionKind>()) p.Children.Add(Label(kind.ToString(), 13)); _editor.Content = new ScrollViewer { Content = p };
    }
    private TextBox Field(string caption, string value)
    {
        var field = new TextBox { Text = value, FontSize = 12, MinHeight = 26, Padding = new Thickness(5, 3, 5, 3) }; AutomationProperties.SetName(field, caption);
        _properties.Children.Add(PropertyRow(caption, field)); return field;
    }
    private ComboBox Choice<T>(string caption, T selected) where T : struct, Enum
    {
        var choice = new ComboBox { FontSize = 12, MinHeight = 26, Padding = new Thickness(5, 3, 5, 3), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var option in Enum.GetValues<T>()) choice.Items.Add(option); choice.SelectedItem = selected; _properties.Children.Add(PropertyRow(caption, choice)); return choice;
    }
    private void Select(string id)
    {
        _selection = id; SetInspector("Properties", false); _properties.Children.Clear();
        if (ShowLadderProperties(id)) return;
        var device = _workspace.Project.Devices.FirstOrDefault(d => d.Id == id);
        if (device is not null)
        {
            _properties.Children.Add(Label("Device properties", 14, bold: true)); var name = Field("Device name", device.Name); var ip = Field("IPv4 address", device.IpAddress);
            _properties.Children.Add(Label("Configuration only. No device discovery or connection.", 11, "8B5347"));
            _properties.Children.Add(Button("Apply device", () => Safe(() => _workspace.Edit("Edit device", p => { int i = p.Devices.FindIndex(d => d.Id == id); p.Devices[i] = p.Devices[i] with { Name = name.Text, IpAddress = ip.Text }; }))));
            _properties.Children.Add(Button("Add DI module", () => Safe(() => _workspace.Edit("Add module", p => p.Devices.First(d => d.Id == id).Modules.Add("DI 16×24 V"))))); return;
        }
        var hmi = _workspace.Project.Screens.SelectMany(s => s.Objects).FirstOrDefault(o => o.Id == id);
        if (hmi is not null)
        {
            _properties.Children.Add(Label("HMI object", 14, bold: true)); var text = Field("Text", hmi.Text); var tag = Field("Tag binding", hmi.Tag); var color = Field("Color (#RRGGBB)", hmi.Color); var width = Field("Width", hmi.Width.ToString(CultureInfo.InvariantCulture)); var height = Field("Height", hmi.Height.ToString(CultureInfo.InvariantCulture));
            _properties.Children.Add(Button("Apply object", () => Safe(() => _workspace.Edit("Edit HMI object", p => { var screen = p.Screens.First(s => s.Objects.Any(o => o.Id == id)); int i = screen.Objects.FindIndex(o => o.Id == id); screen.Objects[i] = screen.Objects[i] with { Text = text.Text, Tag = tag.Text, Color = color.Text, Width = double.Parse(width.Text, CultureInfo.InvariantCulture), Height = double.Parse(height.Text, CultureInfo.InvariantCulture) }; }))));
            _properties.Children.Add(Button("Delete object", () => Safe(() => _workspace.Edit("Delete HMI object", p => { foreach (var s in p.Screens) s.Objects.RemoveAll(o => o.Id == id); }))));
        }
    }
    private void ShowTag(string name)
    {
        var tag = _workspace.Project.Tags.First(t => t.Name == name); SetInspector("Properties"); _properties.Children.Clear(); _properties.Children.Add(Label("PLC tag", 15, bold: true));
        var address = Field("Address", tag.Address); var type = Choice("Data type", tag.Type); var initial = Field("Initial value", tag.InitialValue.ToString(CultureInfo.InvariantCulture)); var comment = Field("Comment", tag.Comment);
        _properties.Children.Add(Button("Apply tag", () => Safe(() => _workspace.Edit("Edit tag", p => { int i = p.Tags.FindIndex(t => t.Name == name); p.Tags[i] = p.Tags[i] with { Address = address.Text, Type = (PlcType)type.SelectedItem, InitialValue = double.Parse(initial.Text, CultureInfo.InvariantCulture), Comment = comment.Text }; }))));
        var monitor = Field("Simulation value", (_workspace.Controller?.Read(name) ?? tag.InitialValue).ToString(CultureInfo.InvariantCulture));
        _properties.Children.Add(Button(PlcValues.IsInput(tag.Address) ? "Write simulated input" : "Force simulated value", () => Safe(() => { EnsureController(); double value = double.Parse(monitor.Text, CultureInfo.InvariantCulture); if (PlcValues.IsInput(tag.Address)) _workspace.Controller!.SetInput(name, value); else _workspace.Controller!.Force(name, value); UpdateRuntime(); })));
        _properties.Children.Add(Button("Release force", () => { _workspace.Controller?.Release(name); UpdateRuntime(); }));
        _properties.Children.Add(Label("Forces exist only inside this virtual controller. STOP clears all forces.", 11, "8B5347"));
    }
    private void MoveObject(string id, PointD point) => Safe(() => _workspace.Edit("Move object", p =>
    {
        int index = p.Devices.FindIndex(d => d.Id == id); if (index >= 0) p.Devices[index] = p.Devices[index] with { X = point.X, Y = point.Y };
        else foreach (var screen in p.Screens) { int i = screen.Objects.FindIndex(o => o.Id == id); if (i >= 0) screen.Objects[i] = screen.Objects[i] with { X = Math.Clamp(point.X, 0, screen.Width - screen.Objects[i].Width), Y = Math.Clamp(point.Y, 0, screen.Height - screen.Objects[i].Height) }; }
    }));
    private void AddNetwork() => LadderCommand("network-add");
    private void InsertContact(InstructionKind kind) => ApplyLadderInstruction(kind);
    private void AddTag() => Safe(() => { CommitSource(); Navigate("tags"); _table.AddTag(); });
    private void AddDevice() => Safe(() => _workspace.Edit("Add virtual device", p =>
    {
        int n = 10; while (p.Devices.Any(d => d.IpAddress == "192.168.0." + n)) n++;
        p.Devices.Add(new(Guid.NewGuid().ToString("N"), "IO_" + n, DeviceKind.RemoteIo, "Generic remote I/O", "192.168.0." + n, 80, 380, ["IM", "DI 8"]));
    }));
    private void AddHmi(HmiKind kind) => Safe(() =>
    {
        RequireHmiDesign(); CommitSource();
        if (!IsHmiView) { if (_workspace.Project.Screens.Count == 0) throw new InvalidOperationException("Create an HMI screen first."); Navigate(FirstHmiView()); }
        string id = Hmi.AddObject(_workspace.Project, ActiveHmi.Id, kind); _canvas.SelectHmi([id]);
    });
    private void Compile() => Safe(() => { _canvas.CancelHmiInteraction(); CommitSource(); _workspace.Compile(); SetInspector("Info"); ShowCompilation(); });
    private void EnsureController() { CommitSource(); if (_workspace.Controller is null) { if (!_workspace.Compile().Success) throw new InvalidOperationException("Compilation failed. Review diagnostics."); } }
    private void Run() => Safe(() => { EnsureController(); _workspace.Controller!.Run(); _scan.Start(); ShowInputs(); UpdateRuntime(); });
    private void Stop() { _canvas.CancelHmiInteraction(); _scan.Stop(); _workspace.Controller?.Stop(); UpdateRuntime(); }
    private void Step() => Safe(() => { EnsureController(); _workspace.Controller!.Step(TimeSpan.FromMilliseconds(100), true); UpdateRuntime(); });
    private void ResetController() { _canvas.CancelHmiInteraction(); _scan.Stop(); _workspace.Controller?.Reset(); UpdateRuntime(); }
    private void ShowCompilation()
    {
        _messages.Children.Clear(); var diagnostics = _workspace.Compilation?.Diagnostics;
        if (diagnostics is null) { _messages.Children.Add(Label("Project ready. Compile before simulation. F7: compile · F5: run · Esc: stop.", 12)); return; }
        _compileCounts.Text = $"  {diagnostics.Count(d => d.Severity == Severity.Error)} errors · {diagnostics.Count(d => d.Severity == Severity.Warning)} warnings";
        foreach (var d in diagnostics.Where(d => _diagnosticFilter == "All" || d.Severity.ToString() == _diagnosticFilter))
        {
            var row = Button($"{d.Severity}  {d.Code}  {d.Message}  {d.Location}" + (d.Line > 0 ? $" ({d.Line}:{d.Column})" : ""), () => Safe(() =>
            {
                var block = _workspace.Project.Blocks.FirstOrDefault(b => b.Id == d.Location || b.Networks.Any(n => n.Id == d.Location || n.Output.Id == d.Location || n.Branches.SelectMany(p => p).Any(i => i.Id == d.Location)));
                if (block is null) return; Navigate("block:" + block.Id);
                if (_source is not null && d.Line > 0) { int position = 0; for (int line = 1; line < d.Line && position < _source.Text.Length; line++) { int next = _source.Text.IndexOf('\n', position); position = next < 0 ? _source.Text.Length : next + 1; } _source.Select(Math.Min(_source.Text.Length, position + Math.Max(0, d.Column - 1)), 0); _source.Focus(FocusState.Keyboard); }
                else { _canvas.Selection = d.Location; Select(d.Location); _canvas.RequestRender(); }
            }));
            row.HorizontalAlignment = HorizontalAlignment.Stretch; row.HorizontalContentAlignment = HorizontalAlignment.Left; row.BorderThickness = new Thickness(0); row.Background = Brush("FFFFFF"); _messages.Children.Add(row);
        }
    }
    private void ShowInputs()
    {
        SetInspector("Info"); _messages.Children.Clear(); _messages.Children.Add(Label("Virtual inputs — click to toggle. STOP is a simulated process input, not a safety function.", 11, "586C7A"));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var tag in _workspace.Project.Tags.Where(t => PlcValues.IsInput(t.Address) && t.Type == PlcType.Bool)) { string name = tag.Name; buttons.Children.Add(Button(name, () => Safe(() => { EnsureController(); var c = _workspace.Controller!; c.SetInput(name, c.Read(name) == 0 ? 1 : 0); UpdateRuntime(); }), "input-" + name)); }
        _messages.Children.Add(buttons);
    }
    private void NewProject() => _ = NewProjectAsync();
    private async Task NewProjectAsync()
    {
        try { CommitSource(); if (!await ConfirmReplaceAsync()) return; Stop(); var demo = DemoProject.Create(); _workspace.Load(demo with { Id = Guid.NewGuid().ToString("N"), Name = "New_project" }); Navigate("block:main"); }
        catch (Exception ex) { Message(ex.Message); }
    }
    private async Task<bool> ConfirmReplaceAsync()
    {
        if (!_workspace.IsDirty) return true;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Unsaved project changes", Content = "Discard unsaved changes? Export the project first to keep a separate copy.", PrimaryButtonText = "Discard changes", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    public async Task InitializeAsync()
    {
        await LoadLayoutAsync();
        try { string? json = await _files.ReadRecoveryAsync(); if (!string.IsNullOrWhiteSpace(json)) { _workspace.Load(ProjectStorage.Deserialize(json)); _lastRecovery = json; _lastRecoveryProject = _workspace.Project; _lastRecoverySource = _source?.Text; _lastRecoverySourceId = _source is null ? null : _view; _status.Text = "Recovered local project. Simulation remains stopped."; } }
        catch (Exception ex) { Message("Recovery was not loaded: " + ex.Message); }
    }
    private async Task OpenAsync()
    {
        try { CommitSource(); if (!await ConfirmReplaceAsync()) return; var json = await _files.OpenAsync(); if (json is null) return; var project = ProjectStorage.Deserialize(json); Stop(); _workspace.Load(project); Navigate(project.Blocks.Count > 0 ? "block:" + project.Blocks[0].Id : "portal"); }
        catch (Exception ex) { Message("Open failed: " + ex.Message); }
    }
    private async Task ExportTagsAsync()
    {
        try { await _files.SaveAsync(ProjectStorage.ExportTagsCsv(_workspace.Project.Tags), _workspace.Project.Name + "-tags", ".csv"); }
        catch (Exception ex) { Message("CSV export failed: " + ex.Message); }
    }
    private void About() => Message("ControlSpace 0.1.0 is an independent TIA Portal-style engineering preview, not Siemens software. Native .ap/.zap projects, S7 code generation and online protocols, WinCC runtimes, safety, motion, drives, certified timing, enterprise services and exact UI parity are not implemented. Never use this simulator to operate real machinery.");
    private void Safe(Action action) { try { if (!_table.TryCommitEdit()) return; CaptureEditorState(); action(); } catch (Exception ex) { Message(ex.Message); } }
    private void Message(string message) { SetInspector("Info"); _messages.Children.Clear(); var text = Label(message, 12, "924937"); text.TextWrapping = TextWrapping.Wrap; _messages.Children.Add(text); _status.Text = message; }
    public void Dispose() {
#if __WASM__
        _verificationTimer?.Stop();
#endif
 _scan.Stop(); _autosave.Stop(); _layoutTimer.Stop(); _workspace.Changed -= WorkspaceChanged; _table.Unbind(); _canvas.Dispose(); }
}
