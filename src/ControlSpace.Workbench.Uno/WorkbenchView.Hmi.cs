using System.Globalization;
using ControlSpace.Core;
using ControlSpace.Controls.Uno;
using ControlSpace.Engineering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using static ControlSpace.Controls.Uno.EngineeringTheme;
namespace ControlSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private HmiEditor Hmi => new(_workspace);
    private readonly HmiExplorer _hmiScreens = new();
    private readonly HmiExplorer _hmiObjects = new(true) { Height = 350 };
    private bool _hmiObjectCard;
    private Grid? _hmiDirectory;
    private (bool Runtime, bool Fit, bool Grid, bool Snap, int Selection, bool Running, double Scale)? _hmiChromeKey;
    private readonly Dictionary<string, Button> _hmiTools = [];
    private HmiScreen ActiveHmi => IsHmiView ? HmiEditor.Screen(_workspace.Project, _view[4..]) : throw new InvalidOperationException("Open an HMI screen first.");
    private void ConfigureHmi()
    {
        _canvas.HmiSelectionChanged += ids => { _hmiObjects.Select(ids); ShowHmiProperties(); };
        _canvas.HmiTransformRequested += (expected, id, boxes) => Safe(() => { if (!_hmiRuntime && _view == "hmi:" + id) { Hmi.Transform(expected, id, boxes); ShowHmiProperties(); } });
        _canvas.HmiCommandRequested += HmiCommand;
        _canvas.HmiEditRequested += id => { _canvas.SelectHmi([id]); _ = EditHmiObjectAsync(); };
        _canvas.HmiRuntimeChanged += UpdateRuntime;
        _canvas.ViewportChanged += UpdateHmiChrome;
        _canvas.HmiContextRequested += position =>
        {
            var menu = new MenuFlyout();
            foreach (var (text, command) in new[] { ("Properties…", "properties"), ("Copy", "copy"), ("Cut", "cut"), ("Paste", "paste"), ("Duplicate", "duplicate"), ("Delete", "delete"), ("Select all", "select-all"), ("Bring to front", "order:Front"), ("Send to back", "order:Back") })
            { var item = new MenuFlyoutItem { Text = text }; item.Click += (_, _) => HmiCommand(command); menu.Items.Add(item); }
            menu.ShowAt(_canvas, new FlyoutShowOptions { Position = position });
        };
        _hmiScreens.CommandRequested += (command, ids) =>
        {
            if (ids.FirstOrDefault() is not string id) return;
            if (command == "open") Safe(() => Navigate("hmi:" + id));
            else { var s = HmiEditor.Screen(_workspace.Project, id); _details.Text = $"{s.Name}\nHMI screen · {s.Width:0} × {s.Height:0}\n{s.Objects.Count} objects"; }
        };
        _hmiObjects.CommandRequested += (command, ids) =>
        {
            if (!IsHmiView) return; _canvas.SelectHmi(ids);
            if (command == "properties") _ = EditHmiObjectAsync();
        };
    }
    public void SuspendHmiInput() => _canvas.CancelHmiInteraction();
    private void SetHmiRuntime(bool runtime) => Safe(() =>
    {
        _canvas.CancelHmiInteraction(); if (runtime) EnsureController();
        _hmiRuntime = runtime; _canvas.HmiRuntime = runtime; _canvas.RequestRender(); UpdateHmiChrome();
        _status.Text = runtime ? "HMI runtime preview · momentary BOOL input buttons · simulation only" : "HMI design · Ctrl/Shift for multiple selection · drag handles to resize · Alt bypasses snapping";
    });
    private void BuildHmiTools()
    {
        _hmiTools.Clear(); _hmiChromeKey = null;
        void Add(string text, string command, string id) { var button = Button(text, () => HmiCommand(command), id); _hmiTools[id] = button; _editorTools.Children.Add(button); }
        Add("Screens", "screens", "hmi-screens"); Add("+ Screen", "screen-new", "hmi-screen-new"); Add("Screen…", "screen-properties", "hmi-screen-properties");
        Add("Design", "design", "hmi-design"); Add("Runtime", "runtime", "hmi-runtime");
        Add("Fit", "fit", "hmi-fit"); Add("100%", "100", "hmi-100"); Add("−", "zoom-out", "hmi-zoom-out"); Add("+", "zoom-in", "hmi-zoom-in");
        Add("Grid", "grid", "hmi-grid"); Add("Snap", "snap", "hmi-snap");
        Add("Objects", "objects", "hmi-objects"); Add("Properties…", "properties", "hmi-properties");
        var arrange = Button("Align / size", () => { }, "hmi-arrange"); var flyout = new MenuFlyout();
        foreach (var op in Enum.GetValues<HmiArrange>()) { var item = new MenuFlyoutItem { Text = ArrangeCaption(op) }; item.Click += (_, _) => HmiCommand("arrange:" + op); AutomationProperties.SetAutomationId(item, "hmi-arrange-" + op); flyout.Items.Add(item); } arrange.Flyout = flyout; _editorTools.Children.Add(arrange);
        var order = Button("Order", () => { }, "hmi-order"); var orderFlyout = new MenuFlyout();
        foreach (var op in Enum.GetValues<HmiOrder>()) { var item = new MenuFlyoutItem { Text = op.ToString() }; item.Click += (_, _) => HmiCommand("order:" + op); orderFlyout.Items.Add(item); } order.Flyout = orderFlyout; _editorTools.Children.Add(order);
        Add("Duplicate", "duplicate", "hmi-duplicate"); Add("Delete", "delete", "hmi-delete"); UpdateHmiChrome();
    }
    private void UpdateHmiChrome()
    {
        if (!IsHmiView) return;
        var key = (_hmiRuntime, _canvas.HmiFit, _canvas.HmiGrid, _canvas.HmiSnap, _canvas.HmiSelection.Count,
            _workspace.Controller?.State == ControlSpace.Simulation.ControllerState.Running, _canvas.HmiTransform.Scale);
        if (_hmiChromeKey == key) return; _hmiChromeKey = key;
        foreach (var (id, button) in _hmiTools)
        {
            bool active = id switch { "hmi-design" => !_hmiRuntime, "hmi-runtime" => _hmiRuntime, "hmi-fit" => _canvas.HmiFit, "hmi-grid" => _canvas.HmiGrid, "hmi-snap" => _canvas.HmiSnap, _ => false };
            button.Background = Brush(active ? "C9DCF0" : "E7E7EA");
            if (id is "hmi-duplicate" or "hmi-delete" or "hmi-properties") button.IsEnabled = !_hmiRuntime && _canvas.HmiSelection.Count > 0 && _workspace.Controller?.State != ControlSpace.Simulation.ControllerState.Running;
        }
        _zoomLabel.Text = $"{_canvas.HmiTransform.Scale * 100:0}%";
    }
    private static string ArrangeCaption(HmiArrange op) => op switch
    {
        HmiArrange.Left => "Align left", HmiArrange.Right => "Align right", HmiArrange.Top => "Align top", HmiArrange.Bottom => "Align bottom",
        HmiArrange.CenterX => "Align horizontal centers", HmiArrange.CenterY => "Align vertical centers", HmiArrange.SameWidth => "Same width", HmiArrange.SameHeight => "Same height", HmiArrange.SameSize => "Same size",
        HmiArrange.DistributeX => "Distribute horizontally", HmiArrange.DistributeY => "Distribute vertically", HmiArrange.ScreenCenterX => "Center horizontally in screen", _ => "Center vertically in screen"
    };
    private void ShowHmiDirectory()
    {
        _hmiScreens.SetProject(_workspace.Project);
        if (_hmiDirectory is not null) { _editor.Content = _hmiDirectory; return; }
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Padding = new Thickness(6) };
        foreach (var (caption, command) in new[] { ("+ Add screen", "screen-new"), ("Open", "screen-open"), ("Properties…", "screen-properties"), ("Duplicate", "screen-duplicate"), ("Delete…", "screen-delete") })
            tools.Children.Add(Button(caption, () => HmiCommand(command), "hmi-directory-" + command));
        grid.Children.Add(HorizontalScroll(tools)); Grid.SetRow(_hmiScreens, 1); grid.Children.Add(_hmiScreens); _hmiDirectory = grid; _editor.Content = grid;
    }
    private void ShowHmiPalette()
    {
        var switches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        switches.Children.Add(Button("Toolbox", () => { _hmiObjectCard = false; ShowPalette(); }, "hmi-toolbox"));
        switches.Children.Add(Button("Objects", () => { _hmiObjectCard = true; ShowPalette(); }, "hmi-object-card")); _palette.Children.Add(switches);
        if (_hmiObjectCard)
        {
            _palette.Children.Add(Header("Objects · front to back")); _hmiObjects.SetScreen(ActiveHmi); _hmiObjects.Select(_canvas.HmiSelection); _palette.Children.Add(_hmiObjects);
            _palette.Children.Add(Button("Select all", () => HmiCommand("select-all"), "hmi-select-all"));
            _palette.Children.Add(Button("Bring to front", () => HmiCommand("order:Front"), "hmi-front")); _palette.Children.Add(Button("Send to back", () => HmiCommand("order:Back"), "hmi-back"));
            foreach (var op in new[] { HmiArrange.Left, HmiArrange.Top, HmiArrange.DistributeX, HmiArrange.SameSize }) _palette.Children.Add(Button(ArrangeCaption(op), () => HmiCommand("arrange:" + op), "hmi-layout-" + op));
        }
        else
        {
            _palette.Children.Add(Header("Basic objects"));
            foreach (var kind in Enum.GetValues<HmiKind>())
            {
                if (_paletteFilter.Length > 0 && !kind.ToString().Contains(_paletteFilter, StringComparison.OrdinalIgnoreCase)) continue;
                var k = kind; var item = Button("+  " + k, () => AddHmi(k), "hmi-insert-" + k); item.HorizontalAlignment = HorizontalAlignment.Stretch; item.HorizontalContentAlignment = HorizontalAlignment.Left; _palette.Children.Add(item);
            }
            var hint = Label("Ctrl/Shift: select multiple objects.\nDrag a selection rectangle on the screen.\nAlt: bypass grid and snap lines.\nCtrl+C / X / V: copy / cut / paste.\nArrow keys: 1 px; Shift: 10 px.", 11, "626775"); hint.TextWrapping = TextWrapping.Wrap; hint.Margin = new Thickness(5, 12, 5, 8); _palette.Children.Add(hint);
        }
        _palette.Children.Add(Button("Screen overview…", () => Navigate("screens"), "hmi-palette-screens"));
    }
    private void HmiCommand(string command)
    {
        if (command == "stop") { Stop(); return; }
        if (command == "screen-new") { _ = EditHmiScreenAsync(null); return; }
        string? screenId = IsHmiView ? _view[4..] : _hmiScreens.SelectedIds.FirstOrDefault();
        if (command == "screen-properties") { if (screenId is not null) _ = EditHmiScreenAsync(screenId); return; }
        if (command == "screen-delete") { if (screenId is not null) _ = DeleteHmiScreenAsync(screenId); return; }
        if (command == "properties") { _ = EditHmiObjectAsync(); return; }
        if (command is "copy" or "cut" or "paste") { _ = HmiClipboardAsync(command); return; }
        Safe(() =>
        {
            _canvas.CancelHmiInteraction();
            if (command == "screens") { Navigate("screens"); return; }
            if (command == "screen-open") { if (screenId is not null) Navigate("hmi:" + screenId); return; }
            if (command == "screen-duplicate") { if (screenId is not null) { RequireHmiDesign(); CommitSource(); string id = Hmi.DuplicateScreen(_workspace.Project, screenId); Navigate("hmi:" + id); } return; }
            var screen = ActiveHmi; var ids = _canvas.HmiSelection.ToArray();
            switch (command)
            {
                case "design": SetHmiRuntime(false); return;
                case "runtime": SetHmiRuntime(true); return;
                case "fit": _canvas.ChangeHmiView(fit: true); return;
                case "100": _canvas.ChangeHmiView(zoom: 1); return;
                case "zoom-in": _canvas.ChangeHmiView(zoom: _canvas.HmiTransform.Scale * 1.2); return;
                case "zoom-out": _canvas.ChangeHmiView(zoom: _canvas.HmiTransform.Scale / 1.2); return;
                case "grid": _canvas.HmiGrid = !_canvas.HmiGrid; _canvas.RequestRender(); UpdateHmiChrome(); return;
                case "snap": _canvas.HmiSnap = !_canvas.HmiSnap; UpdateHmiChrome(); _status.Text = "HMI snapping " + (_canvas.HmiSnap ? "enabled" : "disabled"); return;
                case "objects": _hmiObjectCard = true; _layout = _layout with { TasksVisible = true, TaskCard = "Instructions" }; _taskDrawer = ActualWidth < 1100; ShowPalette(); ApplyLayout(); return;
                case "select-all": _canvas.SelectHmi(screen.Objects.Select(o => o.Id)); return;
            }
            RequireHmiDesign(); CommitSource(); var expected = _workspace.Project;
            if (command == "duplicate") _canvas.SelectHmi(Hmi.DuplicateObjects(expected, screen.Id, ids));
            else if (command == "delete") { Hmi.DeleteObjects(expected, screen.Id, ids); _canvas.SelectHmi([]); }
            else if (command.StartsWith("arrange:")) Hmi.Arrange(expected, screen.Id, ids, Enum.Parse<HmiArrange>(command[8..]));
            else if (command.StartsWith("order:")) Hmi.Reorder(expected, screen.Id, ids, Enum.Parse<HmiOrder>(command[6..]));
            ShowHmiProperties();
        });
    }
    private void RequireHmiDesign()
    {
        if (_hmiRuntime) throw new InvalidOperationException("Switch to HMI Design before editing.");
        if (_workspace.Controller?.State == ControlSpace.Simulation.ControllerState.Running) throw new InvalidOperationException("Stop simulation before editing the HMI screen.");
    }
    private async Task HmiClipboardAsync(string command)
    {
        try
        {
            var s = ActiveHmi; _canvas.CancelHmiInteraction(); CommitSource(); var expected = _workspace.Project; var ids = _canvas.HmiSelection.ToArray();
            if (command == "paste")
            {
                RequireHmiDesign(); var content = Clipboard.GetContent(); if (!content.Contains(StandardDataFormats.Text)) throw new ArgumentException("The clipboard has no HMI object data.");
                string text = await content.GetTextAsync(); RequireHmiDesign(); if (_view != "hmi:" + s.Id) throw new InvalidOperationException("The active screen changed while reading the clipboard.");
                _canvas.SelectHmi(Hmi.Paste(expected, s.Id, text));
            }
            else
            {
                if (command == "cut") RequireHmiDesign();
                string text = HmiEditor.Copy(expected, s.Id, ids); var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy }; data.SetText(text); Clipboard.SetContent(data);
                if (command == "cut") { Hmi.DeleteObjects(expected, s.Id, ids); _canvas.SelectHmi([]); }
                _status.Text = $"Copied {ids.Length} HMI objects.";
            }
        }
        catch (Exception ex) { Message(ex.Message); }
    }
    private void ShowHmiProperties()
    {
        if (!IsHmiView) return; UpdateHmiChrome();
        var screen = ActiveHmi; var ids = _canvas.HmiSelection; var objects = ids.Select(id => screen.Objects.FirstOrDefault(o => o.Id == id)).OfType<HmiObject>().ToArray();
        _selection = objects.FirstOrDefault()?.Id ?? ""; SetInspector("Properties", false); _properties.Children.Clear();
        _properties.Children.Add(Label(objects.Length == 0 ? screen.Name : objects.Length == 1 ? objects[0].Kind + " · " + objects[0].Text : objects.Length + " objects selected", 13, bold: true));
        if (objects.Length == 0)
        {
            _properties.Children.Add(PropertyRow("Screen dimensions", Label($"{screen.Width:0} × {screen.Height:0} px")));
            _properties.Children.Add(Button("Screen properties…", () => _ = EditHmiScreenAsync(screen.Id), "hmi-inspector-screen")); return;
        }
        var b = HmiEditor.Bounds(objects); _properties.Children.Add(PropertyRow("Position / size", Label($"X {b.X:0.##}   Y {b.Y:0.##}   W {b.Width:0.##}   H {b.Height:0.##}")));
        _properties.Children.Add(PropertyRow(objects.Length == 1 ? "Tag binding" : "Alignment reference", Label(objects.Length == 1 ? objects[0].Tag : objects[0].Text)));
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        tools.Children.Add(Button("Properties…", () => _ = EditHmiObjectAsync(), "hmi-inspector-properties")); tools.Children.Add(Button("Duplicate", () => HmiCommand("duplicate"), "hmi-inspector-duplicate")); tools.Children.Add(Button("Delete", () => HmiCommand("delete"), "hmi-inspector-delete")); _properties.Children.Add(tools);
        if (objects.Length > 1)
        {
            var align = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (var op in new[] { HmiArrange.Left, HmiArrange.Top, HmiArrange.SameSize, HmiArrange.DistributeX }) align.Children.Add(Button(ArrangeCaption(op), () => HmiCommand("arrange:" + op), "hmi-inspector-" + op)); _properties.Children.Add(HorizontalScroll(align));
        }
    }
    private async Task EditHmiScreenAsync(string? id)
    {
        try
        {
            _canvas.CancelHmiInteraction(); RequireHmiDesign(); CommitSource(); var expected = _workspace.Project; var screen = id is null ? null : HmiEditor.Screen(expected, id);
            var panel = new StackPanel { Spacing = 7, MinWidth = 360, MaxWidth = 480 };
            var name = DialogField(panel, "Screen name", screen?.Name ?? HmiEditor.NextName(expected), "hmi-screen-name");
            var width = DialogField(panel, "Width (px)", (screen?.Width ?? 960).ToString(CultureInfo.InvariantCulture), "hmi-screen-width");
            var height = DialogField(panel, "Height (px)", (screen?.Height ?? 540).ToString(CultureInfo.InvariantCulture), "hmi-screen-height");
            var note = Label("Objects must fit inside the screen. Resizing never silently crops or moves existing objects.", 11); note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
            await ProgramDialogAsync(screen is null ? "Add HMI screen" : "HMI screen properties", panel, screen is null ? "Create" : "Apply", () =>
            {
                RequireHmiDesign(); double w = double.Parse(width.Text, CultureInfo.InvariantCulture), h = double.Parse(height.Text, CultureInfo.InvariantCulture);
                if (screen is null) { string created = Hmi.AddScreen(expected, name.Text, w, h); Navigate("hmi:" + created); }
                else Hmi.UpdateScreen(expected, screen.Id, name.Text, w, h);
            });
        }
        catch (Exception ex) { Message(ex.Message); }
    }
    private async Task DeleteHmiScreenAsync(string id)
    {
        try
        {
            RequireHmiDesign(); CommitSource(); var expected = _workspace.Project; var screen = HmiEditor.Screen(expected, id); var panel = new StackPanel { Spacing = 10, MinWidth = 350 };
            panel.Children.Add(Label($"Delete {screen.Name} and its {screen.Objects.Count} objects?\nThe deletion can be undone.", 13));
            await ProgramDialogAsync("Delete HMI screen", panel, "Delete", () => { RequireHmiDesign(); Hmi.DeleteScreen(expected, id); Navigate("screens"); });
        }
        catch (Exception ex) { Message(ex.Message); }
    }
    private async Task EditHmiObjectAsync()
    {
        try
        {
            RequireHmiDesign(); _canvas.CancelHmiInteraction(); CommitSource(); var expected = _workspace.Project; var s = ActiveHmi;
            var id = _canvas.HmiSelection.FirstOrDefault() ?? throw new InvalidOperationException("Select an object first."); var o = s.Objects.First(o => o.Id == id);
            var panel = new StackPanel { Spacing = 6, MinWidth = 380, MaxWidth = 510 };
            if (_canvas.HmiSelection.Count > 1) panel.Children.Add(Label("Editing the first selected object. Use alignment tools for the whole selection.", 11));
            var text = DialogField(panel, "Text", o.Text, "hmi-object-text", true);
            panel.Children.Add(Label("Tag binding", 12, bold: true));
            var binding = new AutoSuggestBox { Text = o.Tag, PlaceholderText = "Unbound", MinHeight = 30 }; AutomationProperties.SetAutomationId(binding, "hmi-object-tag"); panel.Children.Add(binding);
            binding.TextChanged += (_, _) => binding.ItemsSource = expected.Tags.Where(t => HmiEditor.Accepts(o.Kind, t) && t.Name.Contains(binding.Text, StringComparison.OrdinalIgnoreCase)).Take(30).Select(t => t.Name).ToArray();
            binding.SuggestionChosen += (_, e) => binding.Text = e.SelectedItem?.ToString() ?? "";
            var geometry = new Grid { ColumnSpacing = 8 }; geometry.ColumnDefinitions.Add(new()); geometry.ColumnDefinitions.Add(new());
            var left = new StackPanel { Spacing = 6 }; var right = new StackPanel { Spacing = 6 }; geometry.Children.Add(left); Grid.SetColumn(right, 1); geometry.Children.Add(right); panel.Children.Add(geometry);
            var x = DialogField(left, "X", o.X.ToString(CultureInfo.InvariantCulture), "hmi-object-x"); var y = DialogField(right, "Y", o.Y.ToString(CultureInfo.InvariantCulture), "hmi-object-y");
            var w = DialogField(left, "Width", o.Width.ToString(CultureInfo.InvariantCulture), "hmi-object-width"); var h = DialogField(right, "Height", o.Height.ToString(CultureInfo.InvariantCulture), "hmi-object-height");
            var color = DialogField(panel, "Color (#RRGGBB)", o.Color, "hmi-object-color");
            await ProgramDialogAsync(o.Kind + " properties", panel, "Apply", () =>
            {
                RequireHmiDesign(); double N(TextBox f) => double.Parse(f.Text, CultureInfo.InvariantCulture);
                Hmi.UpdateObject(expected, s.Id, o with { Text = text.Text, Tag = binding.Text.Trim(), X = N(x), Y = N(y), Width = N(w), Height = N(h), Color = color.Text.Trim() });
                ShowHmiProperties();
            });
        }
        catch (Exception ex) { Message(ex.Message); }
    }
}
