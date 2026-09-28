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
public sealed class WorkbenchView : UserControl, IDisposable
{
    private readonly Workspace _workspace = new(DemoProject.Create());
    private readonly IProjectFiles _files;
    private readonly ProjectTree _tree = new();
    private readonly EngineeringCanvas _canvas = new();
    private readonly EngineeringTable _table = new();
    private readonly ContentControl _editor = new();
    private readonly StackPanel _properties = new() { Spacing = 8, Padding = new Thickness(10) };
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
        _files = files; RequestedTheme = ElementTheme.Light;
        AutomationProperties.SetName(this, "ControlSpace engineering workspace");
        BuildShell();
        _workspace.Changed += WorkspaceChanged;
        _tree.OpenRequested += target => Safe(() => Navigate(target));
        _canvas.Selected += Select;
        _canvas.MoveRequested += MoveObject;
        _canvas.HmiInput += (tag, value) => Safe(() => { EnsureController(); _workspace.Controller!.SetInput(tag, value ? 1 : 0); UpdateRuntime(); });
        _table.TagSelected += ShowTag;
        _scan.Tick += (_, _) => { var controller = _workspace.Controller; if (controller?.State != ControllerState.Running) { _scan.Stop(); return; } controller.Step(TimeSpan.FromMilliseconds(100)); UpdateRuntime(); };
        _autosave.Tick += async (_, _) => await RecoverAsync(); _autosave.Start();
        KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.F7) { Compile(); e.Handled = true; }
            else if (e.Key == VirtualKey.F5) { Run(); e.Handled = true; }
            else if (e.Key == VirtualKey.Escape) { Stop(); e.Handled = true; }
        };
        SizeChanged += (_, _) => { _body.ColumnDefinitions[0].Width = new GridLength(ActualWidth < 900 ? 185 : 260); _body.ColumnDefinitions[2].Width = new GridLength(ActualWidth < 1100 ? 210 : 268); };
        Refresh();
    }
    private void BuildShell()
    {
        var root = new Grid { Background = Brush("D9DDE1") };
        foreach (var height in new[] { new GridLength(32), new GridLength(29), new GridLength(38), new GridLength(1, GridUnitType.Star), new GridLength(25) }) root.RowDefinitions.Add(new() { Height = height });
        var title = new Grid { Background = Brush("303E4A"), Padding = new Thickness(10, 0, 12, 0) };
        title.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); title.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); title.Children.Add(_title);
        var badge = Label("INDEPENDENT ENGINEERING ENVIRONMENT  /  PREVIEW", 10, "BFCFD8"); Grid.SetColumn(badge, 1); title.Children.Add(badge); root.Children.Add(title);
        var menu = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Background = Brush("ECEEEF") };
        AddMenu(menu, "Project", [("New project", NewProject), ("Open…", () => _ = OpenAsync()), ("Save as…", () => _ = SaveAsync()), ("Export tag CSV…", () => _ = ExportTagsAsync()), ("Portal view", () => Navigate("portal"))]);
        AddMenu(menu, "Edit", [("Undo", () => Safe(_workspace.Undo)), ("Redo", () => Safe(_workspace.Redo)), ("Add network", AddNetwork), ("Add tag", AddTag)]);
        AddMenu(menu, "View", [("Project view", () => Navigate("block:main")), ("Devices & networks", () => Navigate("devices")), ("PLC tags", () => Navigate("tags")), ("HMI screens", () => Navigate("hmi")), ("Watch table", () => Navigate("watch")), ("Trace", () => Navigate("trace"))]);
        AddMenu(menu, "Insert", [("LAD network", AddNetwork), ("HMI lamp", () => AddHmi(HmiKind.Lamp)), ("HMI button", () => AddHmi(HmiKind.Button)), ("HMI numeric display", () => AddHmi(HmiKind.Numeric))]);
        AddMenu(menu, "Simulation", [("Compile", Compile), ("Run", Run), ("Stop", Stop), ("Single scan", Step), ("Cold reset", ResetController)]);
        AddMenu(menu, "Help", [("Compatibility and safety", About)]); Grid.SetRow(menu, 1); root.Children.Add(menu);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Padding = new Thickness(6, 3, 6, 3) };
        foreach (var (text, command, id) in new (string, Action, string)[] { ("▱  New", NewProject, "new"), ("▰  Open", () => _ = OpenAsync(), "open"), ("▣  Save", () => _ = SaveAsync(), "save"), ("↶", () => Safe(_workspace.Undo), "undo"), ("↷", () => Safe(_workspace.Redo), "redo"), ("✓  Compile", Compile, "compile"), ("▶  Start simulation", Run, "run"), ("■  Stop", Stop, "stop"), ("▷  Single scan", Step, "step"), ("◉  Monitor", () => Navigate("watch"), "monitor") }) tools.Children.Add(Button(text, command, id));
        var toolScroll = new ScrollViewer { Content = tools, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(toolScroll, 2); root.Children.Add(toolScroll);
        _body.ColumnDefinitions.Add(new() { Width = new GridLength(260) }); _body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _body.ColumnDefinitions.Add(new() { Width = new GridLength(268) });
        var treePane = Pane("Project tree                              ▾", _tree); _body.Children.Add(treePane);
        var middle = new Grid { Margin = new Thickness(3, 0, 3, 0) };
        foreach (var h in new[] { new GridLength(27), new GridLength(30), new GridLength(1, GridUnitType.Star), new GridLength(31), new GridLength(170) }) middle.RowDefinitions.Add(new() { Height = h });
        middle.Children.Add(new Border { Background = Brush("3F5368"), Padding = new Thickness(8, 3, 4, 3), Child = _breadcrumb });
        var editorTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        editorTools.Children.Add(Button("+ Network", AddNetwork, "add-network")); editorTools.Children.Add(Button("─| |─", () => InsertContact(InstructionKind.Contact), "add-contact", "Insert normally open contact in selected network")); editorTools.Children.Add(Button("─|/|─", () => InsertContact(InstructionKind.NegatedContact), "add-nc-contact"));
        editorTools.Children.Add(Button("+ Tag", AddTag, "add-tag")); editorTools.Children.Add(Button("HMI runtime", () => { _hmiRuntime = !_hmiRuntime; Navigate("hmi"); }, "hmi-runtime"));
        editorTools.Children.Add(Button("−", () => { _canvas.Zoom = Math.Max(.5f, _canvas.Zoom - .1f); _canvas.Invalidate(); })); editorTools.Children.Add(Button("+", () => { _canvas.Zoom = Math.Min(2, _canvas.Zoom + .1f); _canvas.Invalidate(); }));
        Grid.SetRow(editorTools, 1); middle.Children.Add(editorTools); Grid.SetRow(_editor, 2); middle.Children.Add(_editor);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        foreach (var (caption, view) in new[] { ("Main [OB1]", "block:main"), ("PLC tags", "tags"), ("Devices", "devices"), ("Overview", "hmi"), ("Speed [SCL]", "block:speed"), ("Watch", "watch"), ("Trace", "trace") }) { string target = view; tabs.Children.Add(Button(caption, () => Navigate(target), "tab-" + view)); }
        var tabScroll = new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(tabScroll, 3); middle.Children.Add(tabScroll);
        var bottom = new Grid(); bottom.RowDefinitions.Add(new() { Height = GridLength.Auto }); bottom.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var infoTabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 }; infoTabs.Children.Add(Button("Compile", ShowCompilation)); infoTabs.Children.Add(Button("Diagnostics", () => Navigate("diagnostics"))); infoTabs.Children.Add(Button("Cross-references", () => Navigate("references"))); infoTabs.Children.Add(Button("Simulation inputs", ShowInputs)); bottom.Children.Add(infoTabs);
        var messages = new ScrollViewer { Content = _messages, Background = Brush("FFFFFF") }; Grid.SetRow(messages, 1); bottom.Children.Add(messages); Grid.SetRow(bottom, 4); middle.Children.Add(bottom);
        Grid.SetColumn(middle, 1); _body.Children.Add(middle);
        var right = Pane("Instructions / properties", new ScrollViewer { Content = _properties }); Grid.SetColumn(right, 2); _body.Children.Add(right);
        Grid.SetRow(_body, 3); root.Children.Add(_body);
        var status = new Grid { Background = Brush("E9EDEF"), Padding = new Thickness(8, 0, 8, 0) }; status.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); status.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); status.Children.Add(_status); Grid.SetColumn(_metrics, 1); status.Children.Add(_metrics); Grid.SetRow(status, 4); root.Children.Add(status);
        Content = root;
    }
    private static void AddMenu(StackPanel panel, string caption, (string, Action)[] items)
    {
        var button = Button(caption, () => { }); button.BorderThickness = new Thickness(0); button.MinHeight = 25; button.Padding = new Thickness(9, 2, 9, 2);
        var flyout = new MenuFlyout(); foreach (var (text, action) in items) { var item = new MenuFlyoutItem { Text = text }; item.Click += (_, _) => action(); flyout.Items.Add(item); } button.Flyout = flyout; panel.Children.Add(button);
    }
    private void WorkspaceChanged(object? sender, EventArgs e) => Refresh();
    private void Refresh()
    {
        _tree.SetProject(_workspace.Project); _title.Text = "ControlSpace — " + _workspace.Project.Name + (_workspace.IsDirty ? " *" : "");
        _canvas.Project = _workspace.Project; _canvas.Controller = _workspace.Controller; _canvas.HmiRuntime = _hmiRuntime;
        ShowView(); ShowCompilation(); ShowPalette(); UpdateRuntime();
    }
    public void Navigate(string view)
    {
        if (_view.StartsWith("block:") && _source is not null) CommitSource();
        _view = view; _selection = ""; _canvas.Selection = null; _canvas.ScrollOffset = 0; ShowView(); ShowPalette();
    }
    private void ShowView()
    {
        _source = null;
        _breadcrumb.Text = _workspace.Project.Name + "  ›  PLC_1  ›  " + _view.Replace("block:", "Program blocks / ");
        if (_view.StartsWith("block:"))
        {
            var block = _workspace.Project.Blocks.Find(b => b.Id == _view[6..]);
            if (block is null) { _editor.Content = Label("Block not found."); return; }
            if (block.Language == BlockLanguage.SCL)
            {
                var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
                grid.Children.Add(Header("SCL subset: assignments, IF / ELSE, arithmetic and Boolean expressions"));
                _source = new TextBox { IsReadOnly = _workspace.Controller?.State == ControllerState.Running, Text = block.Source, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 14, Padding = new Thickness(14), VerticalAlignment = VerticalAlignment.Stretch };
                AutomationProperties.SetName(_source, "SCL source editor"); Grid.SetRow(_source, 1); grid.Children.Add(_source); _editor.Content = grid;
            }
            else { _canvas.Mode = EditorMode.Ladder; _canvas.BlockId = block.Id; _editor.Content = _canvas; }
        }
        else if (_view is "tags" or "watch") { _table.SetTags(_workspace.Project.Tags, _workspace.Controller); _editor.Content = _table; }
        else if (_view == "references") { _table.SetReferences(_workspace.CrossReferences()); _editor.Content = _table; }
        else if (_view == "portal") ShowPortal();
        else if (_view == "diagnostics") ShowDiagnostics();
        else if (_view is "library" or "blocks") ShowLibrary();
        else { _canvas.Mode = _view switch { "devices" => EditorMode.Devices, "hmi" => EditorMode.Hmi, _ => EditorMode.Trace }; _editor.Content = _canvas; }
        _canvas.Invalidate();
    }
    private void CommitSource()
    {
        if (_source is null || !_view.StartsWith("block:")) return;
        var source = _source.Text; string id = _view[6..]; var block = _workspace.Project.Blocks.Find(b => b.Id == id);
        if (block is null || block.Source == source) return;
        _workspace.Edit("Edit SCL source", p => { int i = p.Blocks.FindIndex(b => b.Id == id); p.Blocks[i] = p.Blocks[i] with { Source = source }; });
    }
    private void ShowPortal()
    {
        var panel = new StackPanel { Spacing = 18, Padding = new Thickness(36) };
        panel.Children.Add(Label("ControlSpace", 36, "008C95", true)); panel.Children.Add(Label("Automation engineering. One workspace.", 21));
        panel.Children.Add(Label("Configure devices, program logic, design screens and simulate your project.", 14));
        panel.Children.Add(Button("Open project view", () => Navigate("block:main"))); panel.Children.Add(Button("Create a new project", NewProject)); panel.Children.Add(Button("Open a ControlSpace project…", () => _ = OpenAsync()));
        panel.Children.Add(Label("0.1.0 preview · Independent implementation · Simulation only", 12, "667C89"));
        panel.Children.Add(Label("Native Siemens projects, PLC downloads and certified safety engineering are not supported.", 12, "8B5347")); _editor.Content = new ScrollViewer { Content = panel };
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
    private void ShowPalette()
    {
        _properties.Children.Clear(); _properties.Children.Add(Label("Basic instructions", 14, "26333E", true));
        foreach (var kind in new[] { InstructionKind.Contact, InstructionKind.NegatedContact, InstructionKind.RisingEdge, InstructionKind.FallingEdge, InstructionKind.Greater, InstructionKind.Less, InstructionKind.Equal }) { var current = kind; _properties.Children.Add(Button(kind.ToString(), () => InsertContact(current))); }
        _properties.Children.Add(Label("HMI objects", 14, bold: true)); foreach (var kind in new[] { HmiKind.Label, HmiKind.Button, HmiKind.Lamp, HmiKind.Numeric, HmiKind.Tank, HmiKind.Gauge }) { var current = kind; _properties.Children.Add(Button(kind.ToString(), () => AddHmi(current))); }
        _properties.Children.Add(Label("Hardware", 14, bold: true)); _properties.Children.Add(Button("Add virtual I/O station", AddDevice));
        _properties.Children.Add(Label("Select an instruction, tag or object to edit its properties.", 11, "657A8A"));
    }
    private TextBox Field(string caption, string value)
    {
        var field = new TextBox { Header = caption, Text = value, FontSize = 12, MinHeight = 30 }; AutomationProperties.SetName(field, caption); _properties.Children.Add(field); return field;
    }
    private ComboBox Choice<T>(string caption, T selected) where T : struct, Enum
    {
        var choice = new ComboBox { Header = caption, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var option in Enum.GetValues<T>()) choice.Items.Add(option); choice.SelectedItem = selected; _properties.Children.Add(choice); return choice;
    }
    private void Select(string id)
    {
        _selection = id; _properties.Children.Clear();
        foreach (var block in _workspace.Project.Blocks) foreach (var network in block.Networks)
        {
            var instruction = network.Branches.SelectMany(x => x).Append(network.Output).FirstOrDefault(x => x.Id == id); if (instruction is null) continue;
            _properties.Children.Add(Label("Instruction properties", 14, bold: true));
            var kind = Choice("Instruction", instruction.Kind); var tag = Field("Operand tag", instruction.Tag); var parameter = Field("Parameter / preset (ms)", instruction.Parameter.ToString(CultureInfo.InvariantCulture)); var auxiliary = Field("Elapsed / reset tag", instruction.Auxiliary);
            _properties.Children.Add(Button("Apply changes", () => Safe(() =>
            {
                var next = instruction with { Kind = (InstructionKind)kind.SelectedItem, Tag = tag.Text, Parameter = double.Parse(parameter.Text, CultureInfo.InvariantCulture), Auxiliary = auxiliary.Text };
                _workspace.Edit("Edit instruction", p => { var n = p.Blocks.SelectMany(b => b.Networks).First(n => n.Id == network.Id); if (n.Output.Id == id) { var b = p.Blocks.First(b => b.Networks.Any(n => n.Id == network.Id)); int index = b.Networks.FindIndex(n => n.Id == network.Id); b.Networks[index] = n with { Output = next }; } else foreach (var path in n.Branches) { int index = path.FindIndex(i => i.Id == id); if (index >= 0) path[index] = next; } });
            }), "apply-instruction"));
            _properties.Children.Add(Button("Add parallel path", () => Safe(() => _workspace.Edit("Add branch", p => p.Blocks.SelectMany(b => b.Networks).First(n => n.Id == network.Id).Branches.Add([Instruction.Create(InstructionKind.Contact, "Start_PB")])))));
            _properties.Children.Add(Button("Delete network", () => Safe(() => _workspace.Edit("Delete network", p => p.Blocks.First(b => b.Id == block.Id).Networks.RemoveAll(n => n.Id == network.Id)))));
            return;
        }
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
        var tag = _workspace.Project.Tags.First(t => t.Name == name); _properties.Children.Clear(); _properties.Children.Add(Label("PLC tag", 15, bold: true));
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
    private void AddNetwork() => Safe(() =>
    {
        string id = _view.StartsWith("block:") ? _view[6..] : "main";
        _workspace.Edit("Add LAD network", p => { var block = p.Blocks.Find(b => b.Id == id && b.Language == BlockLanguage.LAD) ?? p.Blocks.First(b => b.Language == BlockLanguage.LAD); block.Networks.Add(LadderNetwork.Create("New network", [[Instruction.Create(InstructionKind.Contact, p.Tags.First(t => t.Type == PlcType.Bool).Name)]], Instruction.Create(InstructionKind.Coil, p.Tags.First(t => t.Type == PlcType.Bool && !PlcValues.IsInput(t.Address)).Name))); }); Navigate("block:" + id);
    });
    private void InsertContact(InstructionKind kind) => Safe(() =>
    {
        _workspace.Edit("Insert contact", p =>
        {
            var network = p.Blocks.SelectMany(b => b.Networks).FirstOrDefault(n => n.Output.Id == _selection || n.Branches.SelectMany(b => b).Any(i => i.Id == _selection)) ?? p.Blocks.SelectMany(b => b.Networks).First();
            bool number = kind is InstructionKind.Greater or InstructionKind.Less or InstructionKind.Equal;
            network.Branches[0].Add(Instruction.Create(kind, p.Tags.First(t => number ? t.Type != PlcType.Bool : t.Type == PlcType.Bool).Name));
        });
    });
    private void AddTag() => Safe(() => _workspace.Edit("Add tag", p =>
    {
        int number = 1; while (p.Tags.Any(t => t.Name == "Tag_" + number)) number++;
        int byteAddress = 100; while (p.Tags.Any(t => t.Address.StartsWith("%M" + byteAddress + ".", StringComparison.OrdinalIgnoreCase))) byteAddress++;
        p.Tags.Add(new("Tag_" + number, PlcType.Bool, "%M" + byteAddress + ".0", 0, "User tag"));
    }));
    private void AddDevice() => Safe(() => _workspace.Edit("Add virtual device", p =>
    {
        int n = 10; while (p.Devices.Any(d => d.IpAddress == "192.168.0." + n)) n++;
        p.Devices.Add(new(Guid.NewGuid().ToString("N"), "IO_" + n, DeviceKind.RemoteIo, "Generic remote I/O", "192.168.0." + n, 80, 380, ["IM", "DI 8"]));
    }));
    private void AddHmi(HmiKind kind) => Safe(() => { _workspace.Edit("Insert HMI object", p => p.Screens[0].Objects.Add(new(Guid.NewGuid().ToString("N"), kind, kind.ToString(), kind == HmiKind.Label ? "" : kind == HmiKind.Button ? "Start_PB" : kind == HmiKind.Lamp ? "Motor_Run" : "Speed_Actual", 80, 100, 180, 70))); Navigate("hmi"); });
    private void Compile() => Safe(() => { CommitSource(); _workspace.Compile(); ShowCompilation(); });
    private void EnsureController() { CommitSource(); if (_workspace.Controller is null) { if (!_workspace.Compile().Success) throw new InvalidOperationException("Compilation failed. Review diagnostics."); } }
    private void Run() => Safe(() => { EnsureController(); _workspace.Controller!.Run(); _scan.Start(); ShowInputs(); UpdateRuntime(); });
    private void Stop() { _scan.Stop(); _workspace.Controller?.Stop(); UpdateRuntime(); }
    private void Step() => Safe(() => { EnsureController(); _workspace.Controller!.Step(TimeSpan.FromMilliseconds(100), true); UpdateRuntime(); });
    private void ResetController() { _scan.Stop(); _workspace.Controller?.Reset(); UpdateRuntime(); }
    private void UpdateRuntime()
    {
        var c = _workspace.Controller; if (_source is not null) _source.IsReadOnly = c?.State == ControllerState.Running; _canvas.Controller = c; _canvas.Invalidate(); _table.UpdateValues(_workspace.Project.Tags, c);
        _metrics.Text = $"{c?.State.ToString().ToUpperInvariant() ?? "STOP"}  ·  {c?.Cycle ?? 0} scans  ·  CPU {c?.LastCpuMilliseconds ?? 0:0.000} ms";
        _status.Text = c?.Fault is string fault ? "Simulation fault: " + fault : "Simulation only · No hardware connection · Uno / Skia host renderer";
    }
    private void ShowCompilation()
    {
        _messages.Children.Clear(); var diagnostics = _workspace.Compilation?.Diagnostics;
        if (diagnostics is null) { _messages.Children.Add(Label("Project ready. Compile before simulation. F7: compile · F5: run · Esc: stop.", 12)); return; }
        foreach (var d in diagnostics) _messages.Children.Add(Label($"{d.Severity}  {d.Code}  {d.Message}  {d.Location}" + (d.Line > 0 ? $" ({d.Line}:{d.Column})" : ""), 12, d.Severity == Severity.Error ? "A53530" : d.Severity == Severity.Warning ? "8F6B0B" : "277D51"));
    }
    private void ShowInputs()
    {
        _messages.Children.Clear(); _messages.Children.Add(Label("Virtual inputs — click to toggle. STOP is a simulated process input, not a safety function.", 11, "586C7A"));
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
        try { string? json = await _files.ReadRecoveryAsync(); if (!string.IsNullOrWhiteSpace(json)) { _workspace.Load(ProjectStorage.Deserialize(json)); _lastRecovery = json; _status.Text = "Recovered local project. Simulation remains stopped."; } }
        catch (Exception ex) { Message("Recovery was not loaded: " + ex.Message); }
    }
    private async Task OpenAsync()
    {
        try { CommitSource(); if (!await ConfirmReplaceAsync()) return; var json = await _files.OpenAsync(); if (json is null) return; var project = ProjectStorage.Deserialize(json); Stop(); _workspace.Load(project); Navigate(project.Blocks.Count > 0 ? "block:" + project.Blocks[0].Id : "portal"); }
        catch (Exception ex) { Message("Open failed: " + ex.Message); }
    }
    private async Task SaveAsync()
    {
        try { CommitSource(); if (await _files.SaveAsync(ProjectStorage.Serialize(_workspace.Project), _workspace.Project.Name + ".controlspace")) { _workspace.MarkSaved(); _status.Text = "Project exported successfully."; } }
        catch (Exception ex) { Message("Save failed: " + ex.Message); }
    }
    private async Task ExportTagsAsync()
    {
        try { await _files.SaveAsync(ProjectStorage.ExportTagsCsv(_workspace.Project.Tags), _workspace.Project.Name + "-tags", ".csv"); }
        catch (Exception ex) { Message("CSV export failed: " + ex.Message); }
    }
    private async Task RecoverAsync()
    {
        if (_savingRecovery) return;
        string json = ProjectStorage.Serialize(_workspace.Project); if (json == _lastRecovery) return; _savingRecovery = true;
        try { await _files.WriteRecoveryAsync(json); _lastRecovery = json; }
        catch (Exception ex) { _status.Text = "Recovery unavailable: " + ex.Message; }
        finally { _savingRecovery = false; }
    }
    private void About() => Message("ControlSpace 0.1.0 is an independent TIA Portal-style engineering preview, not Siemens software. Native .ap/.zap projects, S7 code generation and online protocols, WinCC runtimes, safety, motion, drives, certified timing, enterprise services and exact UI parity are not implemented. Never use this simulator to operate real machinery.");
    private void Safe(Action action) { try { action(); } catch (Exception ex) { Message(ex.Message); } }
    private void Message(string message) { _messages.Children.Clear(); var text = Label(message, 12, "924937"); text.TextWrapping = TextWrapping.Wrap; _messages.Children.Add(text); _status.Text = message; }
    public void Dispose() { _scan.Stop(); _autosave.Stop(); _workspace.Changed -= WorkspaceChanged; _canvas.Dispose(); }
}
