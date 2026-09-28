using ControlSpace.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using static ControlSpace.Controls.Uno.EngineeringTheme;
namespace ControlSpace.Controls.Uno;

/// <summary>Hierarchical project navigator. Search retains ancestors and never changes collapse state.</summary>
public sealed class ProjectTree : UserControl
{
    private sealed record Entry(string Id, string Caption, string? Target, int Depth, string Icon, bool Folder = false);
    private readonly StackPanel _rows = new();
    private readonly TextBox _search = new() { PlaceholderText = "Search in project", FontSize = 12, MinHeight = 27, Padding = new Thickness(5, 3, 5, 3), Margin = new Thickness(4) };
    private readonly HashSet<string> _collapsed = [];
    private readonly List<(Entry Entry, Button Button)> _visible = [];
    private ControlProject? _project;
    private string? _active, _selected;
    public event Action<string>? OpenRequested;
    public event Action<string>? SelectionChanged;
    public ProjectTree()
    {
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(Button("−", () => { foreach (var e in Entries().Where(e => e.Folder && e.Depth > 0)) _collapsed.Add(e.Id); Rebuild(); }, "tree-collapse-all", "Collapse all folders"));
        tools.Children.Add(Button("+", () => { _collapsed.Clear(); Rebuild(); }, "tree-expand-all", "Expand all folders"));
        tools.Children.Add(Label("  Devices", 11, bold: true)); root.Children.Add(tools);
        AutomationProperties.SetName(_search, "Search in project"); AutomationProperties.SetAutomationId(_search, "project-search");
        _search.TextChanged += (_, _) => Rebuild(); Grid.SetRow(_search, 1); root.Children.Add(_search);
        var scroll = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 2); root.Children.Add(scroll); Content = root;
    }
    public void SetProject(ControlProject project)
    {
        if (ReferenceEquals(_project, project)) return;
        if (_project?.Id != project.Id) { _collapsed.Clear(); _selected = null; }
        _project = project; Rebuild();
    }
    public void SetActive(string? target) { _active = target; UpdateSelection(); }
    public void FocusSearch() => _search.Focus(FocusState.Keyboard);
    private List<Entry> Entries()
    {
        var entries = new List<Entry>(); var p = _project; if (p is null) return entries;
        void Add(string id, string name, string? target, int depth, string icon, bool folder = false) => entries.Add(new(id, name, target, depth, icon, folder));
        Add("project", p.Name, null, 0, "▣", true);
        Add("devices", "Devices & networks", "devices", 1, "▦");
        var plc = p.Devices.FirstOrDefault(d => d.Kind == DeviceKind.Controller);
        Add("plc", (plc?.Name ?? "PLC_1") + " [virtual CPU]", null, 1, "▣", true);
        Add("config", "Device configuration", "devices", 2, "▦"); Add("online", "Online & diagnostics", "diagnostics", 2, "◉");
        Add("blocks", "Program blocks", null, 2, "▱", true);
        foreach (var b in p.Blocks) Add("block:" + b.Id, b.Name + " [" + (b.Language == BlockLanguage.LAD ? "OB" : "FC") + b.Number + "]", "block:" + b.Id, 3, b.Language == BlockLanguage.LAD ? "◇" : "≡");
        Add("tag-folder", "PLC tags", null, 2, "▱", true); Add("tags", "Default tag table [" + p.Tags.Count + "]", "tags", 3, "▤");
        Add("watch-folder", "Watch and force tables", null, 2, "▱", true); Add("watch", "Watch table_1", "watch", 3, "▤");
        Add("trace", "Traces", "trace", 2, "∿"); Add("references", "Cross-references", "references", 2, "↔");
        var hmi = p.Devices.FirstOrDefault(d => d.Kind == DeviceKind.Hmi);
        Add("hmi-folder", (hmi?.Name ?? "HMI_1") + " [virtual panel]", null, 1, "▣", true); Add("screens", "Screens", null, 2, "▱", true);
        foreach (var s in p.Screens) Add("hmi:" + s.Id, s.Name, "hmi:" + s.Id, 3, "▣");
        Add("library", "Project library", "library", 1, "▥"); return entries;
    }
    private void Rebuild()
    {
        _rows.Children.Clear(); _visible.Clear(); var entries = Entries(); string filter = _search.Text.Trim();
        var matches = new HashSet<string>();
        if (filter.Length > 0)
        {
            for (int i = 0; i < entries.Count; i++) if (entries[i].Caption.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(entries[i].Id); int depth = entries[i].Depth;
                for (int j = i - 1; j >= 0 && depth > 0; j--) if (entries[j].Depth < depth) { matches.Add(entries[j].Id); depth = entries[j].Depth; }
            }
        }
        int hiddenBelow = int.MaxValue;
        foreach (var entry in entries)
        {
            if (filter.Length > 0) { if (!matches.Contains(entry.Id)) continue; }
            else { if (entry.Depth > hiddenBelow) continue; hiddenBelow = _collapsed.Contains(entry.Id) ? entry.Depth : int.MaxValue; }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            row.Children.Add(Label(entry.Folder ? (_collapsed.Contains(entry.Id) && filter.Length == 0 ? "▸" : "▾") : " ", 11));
            row.Children.Add(Label(entry.Icon, 12, entry.Folder ? "8A792C" : "536EA3")); row.Children.Add(Label(entry.Caption, 12));
            var button = Button(entry.Caption, () => Activate(entry), "tree-" + entry.Id, entry.Caption);
            button.Content = row; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.BorderThickness = new Thickness(0); button.Padding = new Thickness(5 + entry.Depth * 14, 1, 5, 1); button.Height = 23; button.MinHeight = 23;
            button.GotFocus += (_, _) => { _selected = entry.Id; SelectionChanged?.Invoke(entry.Caption); UpdateSelection(); };
            button.KeyDown += (_, e) =>
            {
                int index = _visible.FindIndex(v => v.Entry.Id == entry.Id), next = index;
                if (e.Key == VirtualKey.Down) next++; else if (e.Key == VirtualKey.Up) next--;
                else if (e.Key == VirtualKey.Home) next = 0; else if (e.Key == VirtualKey.End) next = _visible.Count - 1;
                else if (e.Key == VirtualKey.Left && entry.Folder && !_collapsed.Contains(entry.Id)) { _collapsed.Add(entry.Id); Rebuild(); FocusEntry(entry.Id); e.Handled = true; return; }
                else if (e.Key == VirtualKey.Right && entry.Folder) { _collapsed.Remove(entry.Id); Rebuild(); FocusEntry(entry.Id); e.Handled = true; return; }
                else return;
                if (_visible.Count > 0) _visible[Math.Clamp(next, 0, _visible.Count - 1)].Button.Focus(FocusState.Keyboard); e.Handled = true;
            };
            _visible.Add((entry, button)); _rows.Children.Add(button);
        }
        if (_visible.Count == 0) _rows.Children.Add(Label("  No matching project objects", 11, "686875"));
        UpdateSelection();
    }
    private void FocusEntry(string id) => _visible.FirstOrDefault(v => v.Entry.Id == id).Button?.Focus(FocusState.Keyboard);
    private void Activate(Entry entry)
    {
        _selected = entry.Id; SelectionChanged?.Invoke(entry.Caption);
        if (entry.Folder) { if (!_collapsed.Add(entry.Id)) _collapsed.Remove(entry.Id); Rebuild(); FocusEntry(entry.Id); }
        else if (entry.Target is not null) OpenRequested?.Invoke(entry.Target);
        UpdateSelection();
    }
    private void UpdateSelection()
    {
        foreach (var (entry, button) in _visible) button.Background = Brush(entry.Id == _selected || entry.Target is not null && entry.Target == _active ? "CCDDF2" : "F2F2F4");
    }
}
