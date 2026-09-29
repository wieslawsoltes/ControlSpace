using ControlSpace.Core;
using ControlSpace.Engineering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using static ControlSpace.Controls.Uno.EngineeringTheme;
namespace ControlSpace.Controls.Uno;

/// <summary>Viewport-recycled project navigator. Only visible rows and a small overscan
/// own controls; search, expansion and selection live in the independent index.</summary>
public sealed class ProjectTree : UserControl
{
    private const double RowHeight = 23;
    private sealed class Row
    {
        public Button Button = null!;
        public TextBlock Arrow = Label("", 11), Icon = Label("", 12), Caption = Label("", 12);
        public ProjectNavigationEntry? Entry;
        public bool Selected;
        public bool? Expanded;
    }
    private readonly ProjectNavigator _navigator = new();
    private readonly Canvas _rows = new() { Background = Brush("F2F2F4"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBox _search = new() { PlaceholderText = "Search in project", FontSize = 12, MinHeight = 27, Padding = new Thickness(5, 3, 5, 3) };
    private readonly TextBlock _summary = Label("", 10, "667080");
    private readonly List<Row> _pool = [];
    private readonly ScrollViewer _scroll;
    private bool _rendering;
    private string? _pendingFocus;
    public event Action<string>? OpenRequested;
    public event Action<string>? SelectionChanged;
    public event Action<string, string?>? BlockCommandRequested;
    public long RowCreations { get; private set; }
    public long IndexBuilds => _navigator.IndexBuilds;
    public long ProjectionBuilds => _navigator.ProjectionBuilds;
    public int RealizedRowCount => _pool.Count(r => r.Button.Visibility == Visibility.Visible);
    public int VisibleEntryCount => _navigator.VisibleEntries.Count;
    public string? SelectedId => _navigator.SelectedId;
    public string Filter => _navigator.Filter;
    public int SelectedVisibleIndex => _navigator.VisibleIndexOf(_navigator.SelectedId);
    public string? FocusedId => _pool.FirstOrDefault(r => r.Button.FocusState != FocusState.Unfocused)?.Entry?.Id;
    public double VerticalOffset => _scroll.VerticalOffset;
    public double ViewportHeight => _scroll.ViewportHeight;
    public ProjectTree()
    {
        IsTabStop = true;
        AutomationProperties.SetAutomationId(this, "project-navigator"); AutomationProperties.SetName(this, "Project tree");
        var root = new Grid();
        foreach (double h in new[] { 26d, 35, -1, 19 }) root.RowDefinitions.Add(new() { Height = h < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(h) });
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        tools.Children.Add(Button("−", () => ExpandAll(false), "tree-collapse-all", "Collapse all folders"));
        tools.Children.Add(Button("+", () => ExpandAll(true), "tree-expand-all", "Expand all folders"));
        tools.Children.Add(Button("◉", RevealActive, "tree-reveal-active", "Reveal active editor in project tree"));
        tools.Children.Add(Label("  Devices", 11, bold: true)); root.Children.Add(tools);
        var search = new Grid { Margin = new Thickness(4) };
        search.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); search.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        AutomationProperties.SetName(_search, "Search in project"); AutomationProperties.SetAutomationId(_search, "project-search");
        _search.TextChanged += (_, _) => { if (_navigator.SetFilter(_search.Text)) RefreshProjection(true); };
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Down && _navigator.VisibleEntries.Count > 0) { FocusEntry(_navigator.VisibleEntries[0].Id); e.Handled = true; }
            else if (e.Key == VirtualKey.Escape) { _search.Text = ""; e.Handled = true; }
        };
        search.Children.Add(_search);
        var clear = Button("×", () => { _search.Text = ""; _search.Focus(FocusState.Keyboard); }, "tree-clear-search", "Clear project search");
        clear.Width = 25; clear.Padding = new Thickness(0); Grid.SetColumn(clear, 1); search.Children.Add(clear);
        Grid.SetRow(search, 1); root.Children.Add(search);
        _scroll = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top };
        AutomationProperties.SetAutomationId(_scroll, "project-tree-scroll");
        _scroll.ViewChanged += (_, _) => RenderRows(); _scroll.SizeChanged += (_, _) => RenderRows();
        Grid.SetRow(_scroll, 2); root.Children.Add(_scroll);
        _summary.Margin = new Thickness(7, 0, 5, 0); AutomationProperties.SetAutomationId(_summary, "tree-summary"); Grid.SetRow(_summary, 3); root.Children.Add(_summary);
        Content = root; Loaded += (_, _) => RenderRows();
        // Handle navigation before ScrollViewer consumes Home/End/Page keys as
        // viewport-only scrolling. Selection and focus must move together.
        _rows.KeyDown += OnKey;
        KeyDown += OnKey;
    }
    public void SetProject(ControlProject project)
    {
        string? anchor = EntryAtOffset(); double fraction = _scroll.VerticalOffset % RowHeight;
        if (!_navigator.SetProject(project)) return;
        RefreshProjection(false);
        int index = _navigator.VisibleIndexOf(anchor);
        if (index >= 0) _scroll.ChangeView(null, index * RowHeight + fraction, null, true);
        PublishSelection();
    }
    public void SetActive(string? target) { _navigator.SetActive(target); RenderRows(); }
    public void FocusSearch() => _search.Focus(FocusState.Keyboard);
    private string? EntryAtOffset()
    {
        int index = (int)(_scroll.VerticalOffset / RowHeight);
        return index >= 0 && index < _navigator.VisibleEntries.Count ? _navigator.VisibleEntries[index].Id : null;
    }
    private void ExpandAll(bool expanded) { _navigator.ExpandAll(expanded); RefreshProjection(false); }
    private void RevealActive()
    {
        // Explicit reveal may clear a search; passive editor activation never does.
        if (_search.Text.Length > 0) _search.Text = "";
        if (_navigator.RevealActive() is string id) { RefreshProjection(false); FocusEntry(id); }
    }
    private void RefreshProjection(bool reset)
    {
        _pendingFocus = null;
        _rows.Height = _navigator.VisibleEntries.Count * RowHeight;
        _summary.Text = _navigator.VisibleEntries.Count == 0 ? "No matching project objects"
            : _navigator.VisibleEntries.Count + (_navigator.Filter.Length > 0 ? " matching rows · ancestors included" : " project objects");
        if (reset) _scroll.ChangeView(null, 0, null, true);
        RenderRows();
    }
    private Row CreateRow()
    {
        var row = new Row();
        var content = new Grid();
        content.ColumnDefinitions.Add(new() { Width = new GridLength(13) }); content.ColumnDefinitions.Add(new() { Width = new GridLength(17) }); content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(row.Arrow); Grid.SetColumn(row.Icon, 1); content.Children.Add(row.Icon); Grid.SetColumn(row.Caption, 2); content.Children.Add(row.Caption);
        row.Button = Button("", () => { if (row.Entry is { } entry) Activate(entry); });
        row.Button.Content = content; row.Button.Height = row.Button.MinHeight = RowHeight; row.Button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        row.Button.BorderThickness = new Thickness(0); row.Button.Background = Brush("F2F2F4"); row.Button.IsTabStop = false;
        row.Button.GotFocus += (_, _) => { if (!_rendering && row.Entry is { } entry && _navigator.Select(entry.Id)) { PublishSelection(); RenderRows(); } };
        row.Button.ContextRequested += (_, e) =>
        {
            if (row.Entry?.Target?.StartsWith("block:", StringComparison.Ordinal) != true) return;
            string blockId = row.Entry.Target[6..]; var menu = new MenuFlyout();
            foreach (var (caption, command) in new[] { ("Open", "open"), ("Properties…", "properties"), ("Duplicate", "duplicate"), ("Delete…", "delete"), ("Move up in scan order", "up"), ("Move down in scan order", "down") })
            { var item = new MenuFlyoutItem { Text = caption }; item.Click += (_, _) => BlockCommandRequested?.Invoke(command, blockId); menu.Items.Add(item); }
            menu.ShowAt(row.Button); e.Handled = true;
        };
        _rows.Children.Add(row.Button); RowCreations++; return row;
    }
    private void RenderRows()
    {
        if (_rendering || !IsLoaded) return;
        _rendering = true;
        try
        {
            double height = Math.Max(0, _scroll.ActualHeight), width = Math.Max(24, _scroll.ViewportWidth);
            if (height == 0) return;
            _rows.Width = width;
            var entries = _navigator.VisibleEntries;
            int first = Math.Max(0, (int)(_scroll.VerticalOffset / RowHeight) - 2);
            first = Math.Min(first, Math.Max(0, entries.Count - 1));
            int count = Math.Min(entries.Count - first, (int)Math.Ceiling(height / RowHeight) + 5);
            // Trim after a large resize/filter too: detached surplus controls are not retained.
            while (_pool.Count > count) { var row = _pool[^1]; if (row.Button.FocusState != FocusState.Unfocused) Focus(FocusState.Programmatic); _rows.Children.Remove(row.Button); _pool.RemoveAt(_pool.Count - 1); }
            while (_pool.Count < count) _pool.Add(CreateRow());
            for (int n = 0; n < count; n++)
            {
                var row = _pool[n]; var entry = entries[first + n];
                if (row.Entry != entry)
                {
                    if (row.Button.FocusState != FocusState.Unfocused && row.Entry?.Id != entry.Id) Focus(FocusState.Programmatic);
                    row.Entry = entry; row.Expanded = null; row.Caption.Text = entry.Caption;
                    var icon = Label(entry.Icon, 12, entry.Folder ? "8A792C" : "536EA3");
                    row.Icon.Text = icon.Text; row.Icon.FontFamily = icon.FontFamily; row.Icon.Foreground = icon.Foreground;
                    row.Button.Padding = new Thickness(5 + entry.Depth * 14, 1, 5, 1);
                    AutomationProperties.SetAutomationId(row.Button, "tree-" + entry.Id); AutomationProperties.SetName(row.Button, entry.Caption);
                    ToolTipService.SetToolTip(row.Button, entry.Caption + (entry.Description.Length > 0 ? "\n" + entry.Description : ""));
                }
                bool expanded = _navigator.IsExpanded(entry.Id);
                if (row.Expanded != expanded)
                {
                    row.Expanded = expanded;
                    var arrow = Label(entry.Folder ? (expanded ? "▾" : "▸") : " ", 11);
                    row.Arrow.Text = arrow.Text; row.Arrow.FontFamily = arrow.FontFamily;
                }
                bool selected = entry.Id == _navigator.SelectedId || _navigator.SelectedId is null && entry.Target is not null && entry.Target == _navigator.ActiveTarget;
                if (row.Selected != selected || row.Button.Background is null) { row.Selected = selected; row.Button.Background = Brush(selected ? "CCDDF2" : "F2F2F4"); }
                row.Button.Width = width; row.Button.IsTabStop = selected || _navigator.SelectedId is null && first + n == 0;
                Canvas.SetTop(row.Button, (first + n) * RowHeight);
            }
        }
        finally { _rendering = false; }
        if (_pendingFocus is string id && _pool.FirstOrDefault(r => r.Entry?.Id == id) is Row focus)
        { _pendingFocus = null; focus.Button.Focus(FocusState.Keyboard); }
    }
    private void FocusEntry(string id)
    {
        int index = _navigator.VisibleIndexOf(id); if (index < 0) return;
        _navigator.Select(id); PublishSelection();
        double top = index * RowHeight, bottom = top + RowHeight;
        if (top < _scroll.VerticalOffset) _scroll.ChangeView(null, top, null, true);
        else if (bottom > _scroll.VerticalOffset + _scroll.ViewportHeight) _scroll.ChangeView(null, bottom - _scroll.ViewportHeight, null, true);
        _pendingFocus = id; RenderRows();
    }
    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) == _search) return;
        bool commandModifier = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)
            || Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (commandModifier) return;
        ProjectNavigationMove? move = e.Key switch
        {
            VirtualKey.Up => ProjectNavigationMove.Previous, VirtualKey.Down => ProjectNavigationMove.Next,
            VirtualKey.Home => ProjectNavigationMove.First, VirtualKey.End => ProjectNavigationMove.Last,
            VirtualKey.Left => ProjectNavigationMove.Parent, VirtualKey.Right => ProjectNavigationMove.Child, _ => null
        };
        if (move is not null)
        {
            string? id = _navigator.Move(move.Value); RefreshProjection(false); if (id is not null) FocusEntry(id); e.Handled = true;
        }
        else if (e.Key is VirtualKey.PageDown or VirtualKey.PageUp)
        {
            int page = Math.Max(1, (int)(_scroll.ViewportHeight / RowHeight) - 1);
            int index = Math.Clamp(_navigator.VisibleIndexOf(_navigator.SelectedId) + (e.Key == VirtualKey.PageDown ? page : -page), 0, Math.Max(0, _navigator.VisibleEntries.Count - 1));
            if (_navigator.VisibleEntries.Count > 0) FocusEntry(_navigator.VisibleEntries[index].Id); e.Handled = true;
        }
        else if (e.Key is >= VirtualKey.A and <= VirtualKey.Z or >= VirtualKey.Number0 and <= VirtualKey.Number9)
        {
            // Match the project tree's initial-letter navigation. Do not alter the
            // search text, open a document, or intercept modified app shortcuts.
            if (_navigator.SelectByInitial((char)e.Key) is string id) FocusEntry(id);
            e.Handled = true;
        }
        else if ((e.Key is VirtualKey.Enter or VirtualKey.Space) && FocusState != FocusState.Unfocused && _navigator.Selected is { } selected)
        { Activate(selected); e.Handled = true; }
    }
    private void Activate(ProjectNavigationEntry entry)
    {
        _navigator.Select(entry.Id); PublishSelection();
        if (entry.Folder)
        {
            _navigator.SetExpanded(entry.Id, !_navigator.IsExpanded(entry.Id)); RefreshProjection(false); FocusEntry(entry.Id);
        }
        else if (entry.Target is not null) OpenRequested?.Invoke(entry.Target);
        RenderRows();
    }
    private void PublishSelection() => SelectionChanged?.Invoke(_navigator.DescribeSelection());
}
