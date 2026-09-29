using ControlSpace.Controls.Uno;
using ControlSpace.Core;
using ControlSpace.Engineering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using static ControlSpace.Controls.Uno.EngineeringTheme;
namespace ControlSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private sealed record EditorState(string BaseSource, string Source, int Caret, float Scroll, float Zoom, float Horizontal = 0);
    private readonly EditorSession _documents = new();
    private readonly Dictionary<string, EditorState> _editorStates = [];
    private readonly EditorBar _editorBar = new();
    private readonly StackPanel _palette = new() { Spacing = 1, Padding = new Thickness(4) };
    private readonly StackPanel _editorTools = new() { Orientation = Orientation.Horizontal, Spacing = 3 };
    private readonly TextBlock _details = Label("Select a project object to see details.", 11);
    private readonly TextBlock _zoomLabel = Label("100%", 11);
    private readonly TextBlock _compileCounts = Label("Compile", 11);
    private readonly ContentControl _portal = new() { Visibility = Visibility.Collapsed, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly ContentControl _inspectorContent = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly Grid _middle = new();
    private readonly Dictionary<string, Button> _inspectorTabs = [];
    private readonly DispatcherTimer _layoutTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly SemaphoreSlim _layoutWrite = new(1, 1);
    private WorkbenchLayout _layout = new();
    private WorkbenchPane _treePane = null!, _taskPane = null!;
    private UIElement _propertyHost = null!, _infoHost = null!;
    private string _sourceBase = "", _projectId = "", _paletteFilter = "", _diagnosticFilter = "All";
    private bool _maximized, _projectDrawer, _taskDrawer;
    private int _focusArea;
    private bool IsHmiView => _view.StartsWith("hmi:", StringComparison.Ordinal);
    private string FirstHmiView() => _workspace.Project.Screens.FirstOrDefault() is HmiScreen s ? "hmi:" + s.Id : "";

    private void BuildShell()
    {
        var root = new Grid { Background = Brush("D5D5DB") };
        foreach (var h in new[] { 28d, 26, 33, -1, 29, 22 }) root.RowDefinitions.Add(new() { Height = h < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(h) });
        var title = new Grid { Background = Brush("343542"), Padding = new Thickness(9, 0, 9, 0) };
        title.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); title.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); title.Children.Add(_title);
        var badge = Label("ControlSpace  ·  SIMULATION ONLY", 10, "E3E4EC"); Grid.SetColumn(badge, 1); title.Children.Add(badge); root.Children.Add(title);
        var menu = new StackPanel { Orientation = Orientation.Horizontal, Background = Brush("E5E5E9") };
        AddMenu(menu, "Project", [("New project…", NewProject), ("Open…  Ctrl+O", () => _ = OpenAsync()), ("Save as…  Ctrl+S", () => _ = SaveAsync()), ("Export tag table…", () => _ = ExportTagsAsync()), ("Portal view", () => Navigate("portal"))]);
        AddMenu(menu, "Edit", [("Undo  Ctrl+Z", () => Safe(_workspace.Undo)), ("Redo  Ctrl+Y", () => Safe(_workspace.Redo)), ("Add network", AddNetwork), ("Add tag", AddTag)]);
        AddMenu(menu, "View", [("Program blocks", () => Navigate("blocks")), ("Project tree  Ctrl+1", ToggleProject), ("Inspector window  Ctrl+2", ToggleInspector), ("Task cards  Ctrl+3", ToggleTasks), ("Devices & networks", () => Navigate("devices")), ("PLC tags", () => Navigate("tags")), ("HMI screen overview", () => Navigate("screens")), ("Cross-references", () => Navigate("references"))]);
        AddMenu(menu, "Insert", [("Add new block…", () => _ = EditBlockAsync(null)), ("Program blocks", () => Navigate("blocks")), ("LAD network", AddNetwork), ("HMI lamp", () => AddHmi(HmiKind.Lamp)), ("HMI button", () => AddHmi(HmiKind.Button)), ("Virtual I/O station", AddDevice)]);
        AddMenu(menu, "Online", [("Start simulation  F5", Run), ("Stop simulation  Esc", Stop), ("Single scan", Step), ("Virtual CPU diagnostics", () => Navigate("diagnostics")), ("Watch and force table", () => Navigate("watch"))]);
        AddMenu(menu, "Options", [("Compile  F7", Compile), ("Cold reset simulation", ResetController), ("Compatibility and safety", About)]);
        AddMenu(menu, "Window", [("Maximize / restore work area", ToggleMaximize), ("Next editor  Ctrl+F6", () => CycleDocument(1)), ("Previous editor  Ctrl+Shift+F6", () => CycleDocument(-1)), ("Close editor  Ctrl+W", () => CloseDocument(_view)), ("Close all editors", CloseAllDocuments), ("Reset window layout", ResetLayout)]);
        AddMenu(menu, "Help", [("Keyboard shortcuts", ShowShortcuts), ("About ControlSpace", About)]);
        var menuScroll = HorizontalScroll(menu); Grid.SetRow(menuScroll, 1); root.Children.Add(menuScroll);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Padding = new Thickness(5, 2, 5, 2) };
        foreach (var (text, action, id) in new (string, Action, string)[] { ("▱  New", NewProject, "new"), ("▰  Open", () => _ = OpenAsync(), "open"), ("▣  Save project", () => _ = SaveAsync(), "save"), ("↶", () => Safe(_workspace.Undo), "undo"), ("↷", () => Safe(_workspace.Redo), "redo"), ("✓  Compile", Compile, "compile"), ("▶  Start simulation", Run, "run"), ("■  Stop", Stop, "stop"), ("▷  Single scan", Step, "step"), ("◉  Monitor", () => Navigate("watch"), "monitor") }) tools.Children.Add(Button(text, action, id));
        Grid.SetRow(tools, 2); var toolScroll = HorizontalScroll(tools); Grid.SetRow(toolScroll, 2); root.Children.Add(toolScroll);
        foreach (var w in new[] { 252d, 4, -1, 4, 248, 29 }) _body.ColumnDefinitions.Add(new() { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
        var navigator = new Grid(); navigator.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); navigator.RowDefinitions.Add(new() { Height = new GridLength(124) }); navigator.Children.Add(_tree);
        _details.TextWrapping = TextWrapping.Wrap; _details.Margin = new Thickness(8);
        var details = Pane("Details view", _details); Grid.SetRow(details, 1); navigator.Children.Add(details);
        _treePane = new WorkbenchPane("Project tree", navigator, "project-pane"); _treePane.ToggleRequested += ToggleProject; _treePane.PinRequested += ToggleProject; _body.Children.Add(_treePane);
        var leftSplitter = new WorkbenchSplitter(false, "project-splitter");
        leftSplitter.ResizeRequested += delta => SetLayout(_layout with { ProjectWidth = _layout.ProjectWidth + delta }); leftSplitter.ResetRequested += () => SetLayout(_layout with { ProjectWidth = 252 }); Grid.SetColumn(leftSplitter, 1); _body.Children.Add(leftSplitter);
        foreach (var h in new[] { 25d, 29, -1, 22, 4, 204 }) _middle.RowDefinitions.Add(new() { Height = h < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(h) });
        var editorHeader = new Grid { Background = Brush("3D3E5B"), Padding = new Thickness(7, 0, 0, 0) };
        editorHeader.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); editorHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); editorHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); editorHeader.Children.Add(_breadcrumb);
        var maximize = Button("□", ToggleMaximize, "maximize-editor", "Maximize / restore work area"); var close = Button("×", () => CloseDocument(_view), "close-editor", "Close active editor");
        Grid.SetColumn(maximize, 1); editorHeader.Children.Add(maximize); Grid.SetColumn(close, 2); editorHeader.Children.Add(close); _middle.Children.Add(editorHeader);
        var editorTools = HorizontalScroll(_editorTools); Grid.SetRow(editorTools, 1); _middle.Children.Add(editorTools); Grid.SetRow(_editor, 2); _middle.Children.Add(_editor);
        _editor.PointerPressed += (_, _) => { if (_projectDrawer || _taskDrawer || _layout.AutoHideTasks) { _projectDrawer = _taskDrawer = false; if (_layout.AutoHideTasks) _layout = _layout with { TasksVisible = false }; ApplyLayout(); } };
        var editorStatus = new Grid { Background = Brush("E3E3E8") }; editorStatus.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); editorStatus.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        editorStatus.Children.Add(Label("  Offline editing  ·  local project", 10, "626271")); Grid.SetColumn(_zoomLabel, 1); _zoomLabel.Margin = new Thickness(5, 0, 8, 0); editorStatus.Children.Add(_zoomLabel); Grid.SetRow(editorStatus, 3); _middle.Children.Add(editorStatus);
        var inspectorSplitter = new WorkbenchSplitter(true, "inspector-splitter"); inspectorSplitter.ResizeRequested += d => SetLayout(_layout with { InspectorHeight = _layout.InspectorHeight - d }); inspectorSplitter.ResetRequested += () => SetLayout(_layout with { InspectorHeight = 204 }); Grid.SetRow(inspectorSplitter, 4); _middle.Children.Add(inspectorSplitter);
        var inspector = BuildInspector(); Grid.SetRow(inspector, 5); _middle.Children.Add(inspector); Grid.SetColumn(_middle, 2); _body.Children.Add(_middle);
        var rightSplitter = new WorkbenchSplitter(false, "task-splitter"); rightSplitter.ResizeRequested += d => SetLayout(_layout with { TaskWidth = _layout.TaskWidth - d }); rightSplitter.ResetRequested += () => SetLayout(_layout with { TaskWidth = 248 }); Grid.SetColumn(rightSplitter, 3); _body.Children.Add(rightSplitter);
        var taskContent = new Grid(); taskContent.RowDefinitions.Add(new() { Height = GridLength.Auto }); taskContent.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var paletteSearch = new TextBox { PlaceholderText = "Find in task card", FontSize = 12, MinHeight = 27, Padding = new Thickness(5, 3, 5, 3), Margin = new Thickness(4) }; AutomationProperties.SetAutomationId(paletteSearch, "task-search"); paletteSearch.TextChanged += (_, _) => { if (_paletteFilter == paletteSearch.Text) return; _paletteFilter = paletteSearch.Text; ShowPalette(); }; taskContent.Children.Add(paletteSearch);
        var paletteScroll = new ScrollViewer { Content = _palette, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(paletteScroll, 1); taskContent.Children.Add(paletteScroll);
        _taskPane = new WorkbenchPane("Instructions", taskContent, "task-pane"); _taskPane.ToggleRequested += ToggleTasks; _taskPane.PinRequested += () => SetLayout(_layout with { AutoHideTasks = !_layout.AutoHideTasks }); Grid.SetColumn(_taskPane, 4); _body.Children.Add(_taskPane);
        var rail = new StackPanel { Background = Brush("C9C9D1"), Spacing = 2 };
        foreach (string card in new[] { "Instructions", "Libraries", "Testing" }) rail.Children.Add(VerticalTab(card, () => { _maximized = false; _projectDrawer = false; _taskDrawer = ActualWidth < 1100; SetLayout(_layout with { TaskCard = card, TasksVisible = true }); ShowPalette(); }));
        Grid.SetColumn(rail, 5); _body.Children.Add(rail);
        // Grid hit testing follows child order. Keep overlay-capable panes above the editor.
        _body.Children.Remove(_treePane); _body.Children.Remove(_taskPane);
        _body.Children.Add(_treePane); _body.Children.Add(_taskPane);
        Grid.SetRow(_body, 3); root.Children.Add(_body); Grid.SetRow(_portal, 3); root.Children.Add(_portal);
        var footer = new Grid(); footer.ColumnDefinitions.Add(new() { Width = new GridLength(252) }); footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        footer.Children.Add(Button("◀  Portal view", () => Navigate("portal"), "portal-view")); Grid.SetColumn(_editorBar, 1); footer.Children.Add(_editorBar); Grid.SetRow(footer, 4); root.Children.Add(footer);
        _editorBar.ActivateRequested += id => Safe(() => Navigate(id)); _editorBar.CloseRequested += CloseDocument; _editorBar.CloseAllRequested += CloseAllDocuments;
        _editorBar.CloseOthersRequested += id => Safe(() => { CommitSource(); _documents.CloseOthers(id); Navigate(id); }); _editorBar.MoveRequested += (id, delta) => { _documents.Move(id, delta); UpdateDocuments(); };
        var status = new Grid { Background = Brush("E4E4E9"), Padding = new Thickness(7, 0, 7, 0) }; status.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); status.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); status.Children.Add(_status); Grid.SetColumn(_metrics, 1); status.Children.Add(_metrics); Grid.SetRow(status, 5); root.Children.Add(status);
        Content = root; _layoutTimer.Tick += async (_, _) => { _layoutTimer.Stop(); await SaveLayoutAsync(); }; ShowGeneralProperties();
    }
    private static ScrollViewer HorizontalScroll(UIElement content) => new() { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private static Button VerticalTab(string text, Action action)
    {
        var area = new Canvas { Width = 25, Height = 113 };
        var label = Label(text, 12, "393A52", true); label.Width = 113; label.Height = 25; label.RenderTransform = new RotateTransform { Angle = 90 }; Canvas.SetLeft(label, 25); area.Children.Add(label);
        var b = Button(text, action, "task-card-" + text, text); b.Content = area; b.Padding = new Thickness(0); b.Width = 29; b.Height = 117; return b;
    }
    private UIElement BuildInspector()
    {
        var root = new Grid { Background = Brush("F7F7F9") }; root.RowDefinitions.Add(new() { Height = new GridLength(27) }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Background = Brush("CACAD2") };
        foreach (string tab in new[] { "Properties", "Info", "Diagnostics" }) { var b = Button(tab, () => SetInspector(tab), "inspector-" + tab); _inspectorTabs[tab] = b; tabs.Children.Add(b); }
        tabs.Children.Add(Button("⌄", ToggleInspector, "inspector-toggle", "Show / hide inspector")); root.Children.Add(tabs);
        var properties = new Grid(); properties.ColumnDefinitions.Add(new() { Width = new GridLength(145) }); properties.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var categories = new StackPanel { Background = Brush("E8E8ED") }; categories.Children.Add(Header("General", "C7D6EB")); categories.Children.Add(Label("  Object properties", 11)); properties.Children.Add(categories);
        var propertyScroll = new ScrollViewer { Content = _properties, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetColumn(propertyScroll, 1); properties.Children.Add(propertyScroll); _propertyHost = properties;
        var info = new Grid(); info.RowDefinitions.Add(new() { Height = new GridLength(28) }); info.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var infoTabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        infoTabs.Children.Add(Button("Compile", ShowCompilation, "info-compile")); infoTabs.Children.Add(Button("Cross-references", () => Navigate("references"), "info-references")); infoTabs.Children.Add(Button("Simulation inputs", ShowInputs, "info-inputs"));
        foreach (string filter in new[] { "All", "Error", "Warning", "Info" }) infoTabs.Children.Add(Button(filter, () => { _diagnosticFilter = filter; ShowCompilation(); }, "filter-" + filter));
        infoTabs.Children.Add(_compileCounts); info.Children.Add(HorizontalScroll(infoTabs)); var messages = new ScrollViewer { Content = _messages }; Grid.SetRow(messages, 1); info.Children.Add(messages); _infoHost = info;
        _inspectorContent.Content = info; Grid.SetRow(_inspectorContent, 1); root.Children.Add(_inspectorContent); return root;
    }
    private void BuildEditorTools()
    {
        void Add(string text, Action action, string id) => _editorTools.Children.Add(Button(text, action, id));
        if (_view.StartsWith("block:") && _workspace.Project.Blocks.FirstOrDefault(b => "block:" + b.Id == _view)?.Language == BlockLanguage.LAD)
        {
            Add("+ Network", AddNetwork, "add-network"); Add("NO", () => InsertContact(InstructionKind.Contact), "add-contact"); Add("NC", () => InsertContact(InstructionKind.NegatedContact), "add-nc-contact");
            Add("Branch", () => LadderCommand("branch-add"), "ladder-branch");
            Add("Collapse", () => _canvas.CollapseAll(true), "ladder-collapse-all"); Add("Expand", () => _canvas.CollapseAll(false), "ladder-expand-all");
            Add("↑", () => LadderCommand("network-up"), "ladder-network-up"); Add("↓", () => LadderCommand("network-down"), "ladder-network-down");
            Add("−", () => ZoomCanvas(-.1f), "zoom-out"); Add("+", () => ZoomCanvas(.1f), "zoom-in"); Add("100%", () => { _canvas.Zoom = 1; ZoomCanvas(0); }, "zoom-reset");
        }
        else if (_view is "tags" or "watch") { Add("+ Add tag", AddTag, "add-tag"); Add("Export CSV", () => _ = ExportTagsAsync(), "table-export"); Add("Release all forces", () => { _workspace.Controller?.ReleaseAll(); UpdateRuntime(); }, "release-forces"); }
        else if (IsHmiView) BuildHmiTools();
        else if (_view == "devices") { Add("Network view", () => Navigate("devices"), "network-view"); Add("+ Virtual I/O station", AddDevice, "add-device"); }
        else if (_source is not null || _view.StartsWith("block:")) Add("✓ Compile block / project", Compile, "compile-source");
        if (_view.StartsWith("block:")) Add("Block properties…", () => _ = EditBlockAsync(_view[6..]), "block-properties");
        _editorTools.Children.Add(Label("  " + ViewTitle(_view), 11, "666676"));
    }
    private void ZoomCanvas(float delta) { _canvas.ChangeView(zoom: _canvas.Zoom + delta); _zoomLabel.Text = $"{_canvas.Zoom * 100:0}%"; }
    private void SetInspector(string tab, bool reveal = true)
    {
        if (reveal) _maximized = false; _layout = _layout with { InspectorTab = tab, InspectorVisible = reveal || _layout.InspectorVisible };
        foreach (var item in _inspectorTabs) item.Value.Background = Brush(item.Key == tab ? "FFFFFF" : "D5D5DC");
        _inspectorContent.Content = tab == "Properties" ? _propertyHost : _infoHost;
        if (tab == "Diagnostics") { _messages.Children.Clear(); var c = _workspace.Controller; foreach (string text in new[] { "Virtual CPU / in-process simulation", "State: " + (c?.State.ToString() ?? "Not compiled"), "Fault: " + (c?.Fault ?? "None"), "Active forces: " + (c?.Forces.Count ?? 0), "No hardware connection. No real-time or safety certification." }) _messages.Children.Add(Label(text, 12)); }
        ApplyLayout(); QueueLayoutSave();
    }
    private void ShowGeneralProperties()
    {
        _properties.Children.Clear(); _properties.Children.Add(Label(ViewTitle(_view), 13, bold: true));
        _properties.Children.Add(PropertyRow("Project", Label(_workspace.Project.Name)));
        _properties.Children.Add(PropertyRow("Revision", Label(_workspace.Project.Revision.ToString())));
        var hint = Label("Select an instruction, device, tag or HMI object to edit its properties.", 11, "676776"); hint.TextWrapping = TextWrapping.Wrap; _properties.Children.Add(hint);
    }
    private static Grid PropertyRow(string name, FrameworkElement editor)
    {
        var row = new Grid { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch }; row.ColumnDefinitions.Add(new() { Width = new GridLength(172) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.Children.Add(Label(name, 11)); Grid.SetColumn(editor, 1); row.Children.Add(editor); return row;
    }
    private void SetLayout(WorkbenchLayout layout) { _layout = layout.Normalize(); ApplyLayout(); QueueLayoutSave(); }
    private void QueueLayoutSave() { _layoutTimer.Stop(); _layoutTimer.Start(); }
    private void ApplyLayout()
    {
        if (_treePane is null || _middle.RowDefinitions.Count == 0) return;
        double width = ActualWidth > 0 ? ActualWidth : 1600, height = ActualHeight > 0 ? ActualHeight : 1000;
        bool compactTree = width < 760, compactTasks = width < 1100;
        bool project = !_maximized && _layout.ProjectVisible && !compactTree, tasks = !_maximized && _layout.TasksVisible && !compactTasks;
        _body.ColumnDefinitions[0].Width = new GridLength(project ? Math.Min(_layout.ProjectWidth, width * .3) : 24); _body.ColumnDefinitions[1].Width = new GridLength(project ? 4 : 0);
        _body.ColumnDefinitions[4].Width = new GridLength(tasks ? Math.Min(_layout.TaskWidth, width * .3) : 0); _body.ColumnDefinitions[3].Width = new GridLength(tasks ? 4 : 0);
        bool projectOverlay = compactTree && _projectDrawer && !_maximized, taskOverlay = compactTasks && _taskDrawer && !_maximized;
        Grid.SetColumnSpan(_treePane, projectOverlay ? 6 : 1); _treePane.Width = projectOverlay ? Math.Min(_layout.ProjectWidth, width - 40) : double.NaN; _treePane.HorizontalAlignment = projectOverlay ? HorizontalAlignment.Left : HorizontalAlignment.Stretch; Canvas.SetZIndex(_treePane, projectOverlay ? 10 : 0); _treePane.SetExpanded(project || projectOverlay);
        Grid.SetColumn(_taskPane, taskOverlay ? 0 : 4); Grid.SetColumnSpan(_taskPane, taskOverlay ? 5 : 1); _taskPane.Width = taskOverlay ? Math.Min(_layout.TaskWidth, width - 40) : double.NaN; _taskPane.HorizontalAlignment = taskOverlay ? HorizontalAlignment.Right : HorizontalAlignment.Stretch; Canvas.SetZIndex(_taskPane, taskOverlay ? 11 : 0); _taskPane.Visibility = tasks || taskOverlay ? Visibility.Visible : Visibility.Collapsed; _taskPane.SetPinned(!_layout.AutoHideTasks);
        bool inspector = !_maximized && _layout.InspectorVisible;
        _middle.RowDefinitions[5].Height = new GridLength(inspector ? Math.Min(_layout.InspectorHeight, Math.Max(120, height * .38)) : 27); _middle.RowDefinitions[4].Height = new GridLength(inspector ? 4 : 0); _inspectorContent.Visibility = inspector ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ToggleProject() { _maximized = false; if (ActualWidth < 760) { _projectDrawer = !_projectDrawer; _taskDrawer = false; ApplyLayout(); } else SetLayout(_layout with { ProjectVisible = !_layout.ProjectVisible }); }
    private void ToggleTasks() { _maximized = false; if (ActualWidth < 1100) { _taskDrawer = !_taskDrawer; _projectDrawer = false; ApplyLayout(); } else SetLayout(_layout with { TasksVisible = !_layout.TasksVisible }); }
    private void ToggleInspector() { _maximized = false; SetLayout(_layout with { InspectorVisible = !_layout.InspectorVisible }); }
    private void ToggleMaximize() { _maximized = !_maximized; ApplyLayout(); }
    private void ResetLayout() { _maximized = _projectDrawer = _taskDrawer = false; SetLayout(new()); SetInspector("Info"); ShowPalette(); ShowCompilation(); }
    private async Task LoadLayoutAsync()
    {
        if (_files is not IWorkbenchPreferences preferences) return;
        try { _layout = WorkbenchLayout.Parse(await preferences.ReadLayoutAsync()); ApplyLayout(); ShowPalette(); var visible = _layout.InspectorVisible; SetInspector(_layout.InspectorTab); _layout = _layout with { InspectorVisible = visible }; ApplyLayout(); }
        catch (Exception ex) { _status.Text = "Window layout reset: " + ex.Message; }
    }
    private async Task SaveLayoutAsync()
    {
        if (_files is not IWorkbenchPreferences preferences) return;
        await _layoutWrite.WaitAsync(); try { await preferences.WriteLayoutAsync(_layout.Serialize()); } catch (Exception ex) { _status.Text = "Window layout could not be saved: " + ex.Message; } finally { _layoutWrite.Release(); }
    }
    private void CaptureEditorState()
    {
        if (_view.Length == 0) return;
        if (_source is not null) _editorStates[_view] = new(_sourceBase, _source.Text, _source.SelectionStart, 0, 1);
        else if (_canvas.Mode == EditorMode.Ladder && _editor.Content == _graphics) _editorStates[_view] = new("", "", 0, _canvas.ScrollOffset, _canvas.Zoom, _canvas.HorizontalOffset);
    }
    private void RestoreCanvasState() { var state = _editorStates.GetValueOrDefault(_view); _canvas.ScrollOffset = state?.Scroll ?? 0; _canvas.HorizontalOffset = state?.Horizontal ?? 0; _canvas.Zoom = state?.Zoom ?? 1; _zoomLabel.Text = $"{_canvas.Zoom * 100:0}%"; }
    private void UpdateDocuments() { if (_view.Length > 0 && IsValidView(_view)) _documents.Open(_view, ViewTitle(_view)); _editorBar.SetDocuments(_documents.Documents, _documents.ActiveId); }
    private bool IsValidView(string id) => id.StartsWith("block:") ? _workspace.Project.Blocks.Any(b => "block:" + b.Id == id) : id.StartsWith("hmi:") ? _workspace.Project.Screens.Any(s => "hmi:" + s.Id == id) : id is "tags" or "watch" or "devices" or "references" or "diagnostics" or "library" or "trace" or "blocks" or "screens";
    private string ViewTitle(string id) => id.StartsWith("block:") ? _workspace.Project.Blocks.FirstOrDefault(b => "block:" + b.Id == id) is ProgramBlock b ? b.Name + " [" + (b.Language == BlockLanguage.LAD ? "OB" : "FC") + b.Number + "]" : "Program block" : id.StartsWith("hmi:") ? _workspace.Project.Screens.FirstOrDefault(s => "hmi:" + s.Id == id)?.Name ?? "HMI screen" : id switch { "tags" => "Default tag table", "watch" => "Watch table_1", "devices" => "Devices & networks", "references" => "Cross-references", "diagnostics" => "Online & diagnostics", "trace" => "Trace", "library" => "Project library", "blocks" => "Program blocks", "screens" => "HMI screen overview", _ => "Work area" };
    private string ViewPath(string id) => id.StartsWith("block:") ? "PLC_1  ›  Program blocks  ›  " + ViewTitle(id) : IsHmiView ? "HMI_1  ›  Screens  ›  " + ViewTitle(id) : ViewTitle(id);
    private void CloseDocument(string id) => Safe(() => { CommitSource(); CaptureEditorState(); _documents.Close(id); _editorStates.Remove(id); var next = _documents.ActiveId ?? ""; _source = null; Navigate(next); });
    private void CloseAllDocuments() => Safe(() => { CommitSource(); _documents.Clear(); _editorStates.Clear(); _view = ""; _source = null; Navigate(""); });
    private void CycleDocument(int direction) => Safe(() => { CommitSource(); CaptureEditorState(); if (_documents.Cycle(direction) is string id) Navigate(id); });
    private void ShowEmptyEditor() { var p = new StackPanel { Padding = new Thickness(30), Spacing = 14 }; p.Children.Add(Label("No editor open", 22, "55566E")); p.Children.Add(Label("Open a program block, table or screen from the project tree.", 13)); p.Children.Add(Button("Open project tree", () => { _maximized = false; _projectDrawer = true; SetLayout(_layout with { ProjectVisible = true }); })); _editor.Content = p; }
    private void ShowPortal()
    {
        _body.Visibility = Visibility.Collapsed; _portal.Visibility = Visibility.Visible;
        var root = new Grid { Background = Brush("F8F8FA") }; root.ColumnDefinitions.Add(new() { Width = new GridLength(210) }); root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var portals = new StackPanel { Background = Brush("3D3E5B"), Padding = new Thickness(12), Spacing = 8 }; portals.Children.Add(Label("ControlSpace", 23, "FFFFFF", true)); portals.Children.Add(Label("Engineering portal", 12, "CDCFDE"));
        var content = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        void Page(string name, string subtitle, (string, Action)[] actions)
        {
            var p = new StackPanel { Padding = new Thickness(32), Spacing = 15 }; p.Children.Add(Label(name, 28, "3D3E5B", true)); p.Children.Add(Label(subtitle, 14));
            p.Children.Add(Label(_workspace.Project.Name, 19, "008C95", true)); foreach (var (caption, action) in actions) { var button = Button(caption, action, "portal-" + caption); button.HorizontalAlignment = HorizontalAlignment.Left; button.MinWidth = 230; button.MinHeight = 36; p.Children.Add(button); }
            var note = Label("Independent engineering preview. All controller operations are simulated.\nNative Siemens project files and hardware downloads are not supported.", 12, "76676F"); note.TextWrapping = TextWrapping.Wrap; p.Children.Add(note); content.Content = new ScrollViewer { Content = p };
        }
        foreach (var (name, subtitle, actions) in new (string, string, (string, Action)[])[]
        {
            ("Start", "Create, open or continue a project.", [("Create new project", NewProject), ("Open project…", () => _ = OpenAsync()), ("Continue in project view", () => Navigate(_view))]),
            ("Devices & networks", "Configure the offline device layout.", [("Open device configuration", () => Navigate("devices")), ("Add virtual I/O", AddDevice)]),
            ("PLC programming", "Edit program blocks and symbol tables.", [("Open program block", () => Navigate(_workspace.Project.Blocks.FirstOrDefault() is ProgramBlock b ? "block:" + b.Id : "")), ("Open PLC tags", () => Navigate("tags")), ("Compile project", Compile)]),
            ("Visualization", "Design and test your HMI screens.", [("Open HMI screen", () => Navigate("hmi"))]),
            ("Online & diagnostics", "Inspect the in-process virtual controller.", [("Open virtual CPU diagnostics", () => Navigate("diagnostics")), ("Open watch table", () => Navigate("watch"))])
        }) { var button = Button(name, () => Page(name, subtitle, actions), "portal-section-" + name); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.MinHeight = 40; portals.Children.Add(button); }
        portals.Children.Add(Button("Project view  ▶", () => Navigate(_view), "project-view")); root.Children.Add(portals); Grid.SetColumn(content, 1); root.Children.Add(content); _portal.Content = root;
        Page("Start", "Create, open or continue a project.", [("Create new project", NewProject), ("Open project…", () => _ = OpenAsync()), ("Continue in project view", () => Navigate(_view))]);
    }
    private void ConfigureShortcuts()
    {
        void Key(VirtualKey key, VirtualKeyModifiers modifiers, Action action, bool textOwnsUndo = false)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, e) => { if (textOwnsUndo && FocusManager.GetFocusedElement(XamlRoot) is TextBox) return; Safe(action); e.Handled = true; }; KeyboardAccelerators.Add(accelerator);
        }
        Key(VirtualKey.F7, VirtualKeyModifiers.None, Compile); Key(VirtualKey.F5, VirtualKeyModifiers.None, Run); Key(VirtualKey.Escape, VirtualKeyModifiers.None, Stop);
        Key(VirtualKey.S, VirtualKeyModifiers.Control, () => _ = SaveAsync()); Key(VirtualKey.O, VirtualKeyModifiers.Control, () => _ = OpenAsync());
        Key(VirtualKey.Z, VirtualKeyModifiers.Control, _workspace.Undo, true); Key(VirtualKey.Y, VirtualKeyModifiers.Control, _workspace.Redo, true);
        Key(VirtualKey.W, VirtualKeyModifiers.Control, () => CloseDocument(_view)); Key(VirtualKey.F6, VirtualKeyModifiers.Control, () => CycleDocument(1)); Key(VirtualKey.F6, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => CycleDocument(-1));
        Key(VirtualKey.Right, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, () => CycleDocument(1)); Key(VirtualKey.Left, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, () => CycleDocument(-1));
        Key(VirtualKey.Number1, VirtualKeyModifiers.Control, ToggleProject); Key(VirtualKey.Number2, VirtualKeyModifiers.Control, ToggleInspector); Key(VirtualKey.Number3, VirtualKeyModifiers.Control, ToggleTasks);
        Key(VirtualKey.F, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => { _projectDrawer = true; SetLayout(_layout with { ProjectVisible = true }); _tree.FocusSearch(); });
        Key(VirtualKey.F6, VirtualKeyModifiers.None, () => FocusArea(1)); Key(VirtualKey.F6, VirtualKeyModifiers.Shift, () => FocusArea(-1));
    }
    private void FocusArea(int delta)
    {
        _focusArea = (_focusArea + delta + 4) % 4;
        if (_focusArea == 0) { _projectDrawer = true; SetLayout(_layout with { ProjectVisible = true }); _tree.FocusSearch(); }
        else if (_focusArea == 1) { if (_source is not null) _source.Focus(FocusState.Keyboard); else _canvas.Focus(FocusState.Keyboard); }
        else if (_focusArea == 2) { _taskDrawer = true; SetLayout(_layout with { TasksVisible = true }); _taskPane.FocusPane(); }
        else { SetInspector(_layout.InspectorTab); _inspectorTabs[_layout.InspectorTab].Focus(FocusState.Keyboard); }
    }
    private void ShowShortcuts() => Message("F6 / Shift+F6: move between panes. Ctrl+F6 / Ctrl+Shift+F6: cycle editors. Ctrl+W: close editor. Ctrl+1/2/3: toggle project tree / inspector / task cards. Ctrl+Shift+F: project search. Ctrl+O/S: open/save. F7: compile. F5: simulate. Esc: stop simulation. Splitters: drag, arrow keys, Home to reset. Text editors retain their own undo shortcuts.");
}
