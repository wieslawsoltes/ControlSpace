using ControlSpace.Core;
using ControlSpace.Engineering;
using ControlSpace.Simulation;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using static ControlSpace.Controls.Uno.EngineeringTheme;
namespace ControlSpace.Controls.Uno;

/// <summary>Viewport-recycled PLC tag grid. Header, selection and edits do not enter project history.</summary>
public sealed class EngineeringTable : UserControl
{
    private const double RowHeight = 24, Gutter = 36;
    private sealed class RowVisual
    {
        public Grid Root { get; } = new() { Height = RowHeight };
        public TextBlock Number { get; } = Label("", 11, "68707A");
        public List<Border> Cells { get; } = [];
        public List<TextBlock> Text { get; } = [];
        public int Index;
        public string Name = "";
    }
    private readonly Grid _root = new() { Background = Brush("FFFFFF") };
    private readonly Grid _header = new() { Height = 25, Background = Brush("D8D9DF") };
    private readonly Canvas _canvas = new() { Background = Brush("FFFFFF") };
    private readonly ScrollViewer _scroll;
    private readonly TextBox _search = new() { PlaceholderText = "Filter tags", Width = 185, MinHeight = 26, FontSize = 12, Padding = new Thickness(5, 2, 5, 2) };
    private readonly TextBlock _status = Label("", 11, "586574");
    private readonly List<RowVisual> _pool = [];
    private readonly List<TagColumn> _order = [TagColumn.Name, TagColumn.DataType, TagColumn.Address, TagColumn.Retain, TagColumn.InitialValue, TagColumn.Comment, TagColumn.MonitorValue];
    private readonly HashSet<TagColumn> _hidden = [TagColumn.InitialValue, TagColumn.MonitorValue];
    private readonly Dictionary<TagColumn, double> _widths = new()
    { [TagColumn.Name] = 190, [TagColumn.DataType] = 90, [TagColumn.Address] = 110, [TagColumn.Retain] = 64, [TagColumn.InitialValue] = 104, [TagColumn.Comment] = 310, [TagColumn.MonitorValue] = 115 };
    private IReadOnlyList<PlcTag> _tags = [], _rows = [];
    private List<TagColumn> _columns = [];
    private Workspace? _workspace;
    private VirtualPlc? _controller;
    private TagColumn? _sort;
    private bool _descending, _rendering, _references, _watch;
    private int _row = -1, _column, _anchorRow = -1, _anchorColumn, _selectionVersion;
    private TextBox? _editing;
    private RowVisual? _editingRow;
    private TagColumn _editingColumn;
    private long _editRevision;
    private string _editProjectId = "";
    public event Action<string>? TagSelected;
    public string StatusText => _status.Text;
    public int RealizedRowCount => _pool.Count(r => r.Root.Visibility == Visibility.Visible);
    public string? ActiveTagName => _row >= 0 && _row < _rows.Count ? _rows[_row].Name : null;
    public EngineeringTable()
    {
        IsTabStop = true; AutomationProperties.SetAutomationId(this, "tag-table"); AutomationProperties.SetName(this, "PLC tag table");
        foreach (double h in new[] { 30d, 25, -1, 23 }) _root.RowDefinitions.Add(new() { Height = h < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(h) });
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Padding = new Thickness(3, 2, 3, 2), Background = Brush("ECECF0") };
        AutomationProperties.SetAutomationId(_search, "tag-filter"); _search.TextChanged += (_, _) => { CancelEdit(); Query(); }; tools.Children.Add(_search);
        tools.Children.Add(Button("+ Add", () => AddTag(), "tag-add")); tools.Children.Add(Button("Duplicate", Duplicate, "tag-duplicate")); tools.Children.Add(Button("Delete", Delete, "tag-delete"));
        tools.Children.Add(Button("Copy", () => Copy(), "tag-copy")); tools.Children.Add(Button("Paste", () => _ = PasteAsync(), "tag-paste"));
        tools.Children.Add(Button("Monitor all", () => { CancelEdit(); if (!_hidden.Add(TagColumn.MonitorValue)) _hidden.Remove(TagColumn.MonitorValue); BuildColumns(); }, "tag-monitor"));
        var columns = Button("Columns", () => { }, "tag-columns"); var flyout = new MenuFlyout();
        foreach (var c in _order)
        {
            var item = new ToggleMenuFlyoutItem { Text = Title(c), IsChecked = !_hidden.Contains(c) };
            item.Click += (_, _) => { CancelEdit(); if (item.IsChecked) _hidden.Remove(c); else _hidden.Add(c); if (_hidden.Count == _order.Count) { _hidden.Remove(c); item.IsChecked = true; } BuildColumns(); };
            flyout.Items.Add(item);
        }
        flyout.Opening += (_, _) => { for (int i = 0; i < _order.Count; i++) ((ToggleMenuFlyoutItem)flyout.Items[i]).IsChecked = !_hidden.Contains((TagColumn)i); };
        columns.Flyout = flyout; tools.Children.Add(columns);
        var toolbar = new ScrollViewer { Content = tools, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _root.Children.Add(toolbar);
        var headerClip = new Grid { Background = Brush("D8D9DF") }; headerClip.Children.Add(_header);
        headerClip.SizeChanged += (_, _) => headerClip.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, headerClip.ActualWidth, 25) };
        Grid.SetRow(headerClip, 1); _root.Children.Add(headerClip);
        _scroll = new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top };
        _scroll.ViewChanged += (_, _) => { _header.RenderTransform = new TranslateTransform { X = -_scroll.HorizontalOffset }; RenderRows(); };
        _scroll.SizeChanged += (_, _) => RenderRows(); Grid.SetRow(_scroll, 2); _root.Children.Add(_scroll);
        _status.Margin = new Thickness(7, 0, 7, 0); AutomationProperties.SetAutomationId(_status, "tag-table-status"); Grid.SetRow(_status, 3); _root.Children.Add(_status);
        Content = _root; BuildColumns();
        KeyDown += OnKey;
        void Shortcut(VirtualKey key, Action action)
        {
            var a = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control };
            a.Invoked += (_, e) => { if (_references || _editing is not null || XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) == _search) return; action(); e.Handled = true; };
            KeyboardAccelerators.Add(a);
        }
        Shortcut(VirtualKey.C, Copy); Shortcut(VirtualKey.V, () => _ = PasteAsync());
        Shortcut(VirtualKey.A, () => { if (_rows.Count == 0) return; _anchorRow = 0; _anchorColumn = 0; _row = _rows.Count - 1; _column = _columns.Count - 1; RenderRows(); });
    }
    public void Bind(Workspace workspace) => _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    public void SetTags(IReadOnlyList<PlcTag> tags, VirtualPlc? controller = null, bool watch = false)
    {
        string? selected = ActiveTagName; bool changed = !ReferenceEquals(_tags, tags);
        _references = false; Content = _root; _tags = tags; _controller = controller;
        if (_watch != watch) { _watch = watch; if (watch) _hidden.Remove(TagColumn.MonitorValue); else _hidden.Add(TagColumn.MonitorValue); BuildColumns(); }
        if (changed) { CancelEdit(); Query(selected); } else RenderRows();
    }
    public void UpdateValues(IReadOnlyList<PlcTag> tags, VirtualPlc? controller)
    {
        _controller = controller; if (_references) return;
        int column = _columns.IndexOf(TagColumn.MonitorValue); if (column < 0) return;
        // Only realized cells are visited; no O(total tags) work on each simulation tick.
        foreach (var row in _pool.Where(r => r.Root.Visibility == Visibility.Visible))
        {
            string value = Monitor(_rows[row.Index]); if (row.Text[column].Text != value) row.Text[column].Text = value;
        }
    }
    private string Monitor(PlcTag tag) => _controller is null ? "—" : PlcValues.Format(tag.Type, _controller.Read(tag.Name));
    private static string Title(TagColumn c) => c switch { TagColumn.DataType => "Data type", TagColumn.InitialValue => "Start value", TagColumn.MonitorValue => "Monitor value", _ => c.ToString() };
    private void Query(string? selected = null)
    {
        selected ??= ActiveTagName; _selectionVersion++;
        _rows = TagTableEditor.Query(_tags, _search.Text, _sort, _descending);
        _row = selected is null ? (_rows.Count == 0 ? -1 : 0) : _rows.ToList().FindIndex(t => t.Name == selected);
        if (_row < 0 && _rows.Count > 0) _row = 0;
        _anchorRow = _row; _anchorColumn = _column; RenderRows(); Status();
    }
    private void BuildColumns()
    {
        CancelEdit(); _selectionVersion++;
        _columns = _order.Where(c => !_hidden.Contains(c)).ToList(); _column = Math.Clamp(_column, 0, Math.Max(0, _columns.Count - 1)); _anchorColumn = _column;
        _header.Children.Clear(); _header.ColumnDefinitions.Clear(); _header.HorizontalAlignment = HorizontalAlignment.Left;
        _header.ColumnDefinitions.Add(new() { Width = new GridLength(Gutter) });
        var number = Label("#", 11, "596675"); number.Margin = new Thickness(10, 0, 0, 0); _header.Children.Add(number);
        for (int i = 0; i < _columns.Count; i++)
        {
            var c = _columns[i]; _header.ColumnDefinitions.Add(new() { Width = new GridLength(_widths[c]) });
            var cell = new Grid { BorderBrush = Brush("AEB1BA"), BorderThickness = new Thickness(0, 0, 1, 1) };
            var title = Button(Title(c) + (_sort == c ? (_descending ? " ↓" : " ↑") : ""), () => { if (c == TagColumn.MonitorValue || !TryCommitEdit()) return; _descending = _sort == c && !_descending; _sort = c; Query(); BuildColumns(); }, "tag-header-" + c);
            title.BorderThickness = new Thickness(0); title.Background = Brush("D8D9DF"); title.HorizontalAlignment = HorizontalAlignment.Stretch; title.HorizontalContentAlignment = HorizontalAlignment.Left; title.Padding = new Thickness(6, 1, 6, 1); title.MinHeight = 24; cell.Children.Add(title);
            var resize = new WorkbenchSplitter(false, "tag-resize-" + c) { Width = 4, HorizontalAlignment = HorizontalAlignment.Right };
            resize.ResizeRequested += delta => { _widths[c] = Math.Clamp(_widths[c] + delta, 52, 700); ResizeColumns(); }; cell.Children.Add(resize);
            var menu = new MenuFlyout();
            foreach (int direction in new[] { -1, 1 }) { int d = direction; var move = new MenuFlyoutItem { Text = d < 0 ? "Move column left" : "Move column right" }; move.Click += (_, _) => { int index = _order.IndexOf(c), next = Math.Clamp(index + d, 0, _order.Count - 1); _order.RemoveAt(index); _order.Insert(next, c); BuildColumns(); }; menu.Items.Add(move); }
            title.ContextFlyout = menu; Grid.SetColumn(cell, i + 1); _header.Children.Add(cell);
        }
        _canvas.Children.Clear(); _pool.Clear(); ResizeColumns();
    }
    private void ResizeColumns()
    {
        for (int i = 0; i < _columns.Count; i++) _header.ColumnDefinitions[i + 1].Width = new GridLength(_widths[_columns[i]]);
        double width = Gutter + _columns.Sum(c => _widths[c]); _header.Width = _canvas.Width = width;
        foreach (var row in _pool) { row.Root.Width = width; for (int i = 0; i < _columns.Count; i++) row.Root.ColumnDefinitions[i + 1].Width = new GridLength(_widths[_columns[i]]); }
        RenderRows();
    }
    private RowVisual CreateRow()
    {
        var row = new RowVisual(); row.Root.Width = _canvas.Width; row.Root.ColumnDefinitions.Add(new() { Width = new GridLength(Gutter) });
        var number = new Border { Background = Brush("E5E6EC"), BorderBrush = Brush("BFC2CA"), BorderThickness = new Thickness(0, 0, 1, 1), Child = row.Number };
        row.Number.Margin = new Thickness(6, 0, 4, 0); row.Root.Children.Add(number);
        number.Tapped += (_, e) => { Select(row.Index, 0, Shift()); _anchorColumn = 0; _column = _columns.Count - 1; RenderRows(); e.Handled = true; };
        for (int i = 0; i < _columns.Count; i++)
        {
            int column = i; row.Root.ColumnDefinitions.Add(new() { Width = new GridLength(_widths[_columns[i]]) });
            var text = Label("", 12); text.Margin = new Thickness(6, 0, 5, 0);
            var cell = new Border { BorderBrush = Brush("D4D7DE"), BorderThickness = new Thickness(0, 0, 1, 1), Child = text };
            cell.Tapped += (_, e) => { Select(row.Index, column, Shift()); e.Handled = true; };
            cell.DoubleTapped += (_, e) => { Select(row.Index, column, false); BeginEdit(); e.Handled = true; };
            Grid.SetColumn(cell, i + 1); row.Root.Children.Add(cell); row.Cells.Add(cell); row.Text.Add(text);
        }
        _canvas.Children.Add(row.Root); return row;
    }
    private void RenderRows()
    {
        if (_rendering || _references || _scroll is null) return;
        _rendering = true;
        try
        {
            _canvas.Height = Math.Max(1, _rows.Count * RowHeight);
            var range = TagTableEditor.VisibleRange(_rows.Count, _scroll.VerticalOffset, Math.Max(1, _scroll.ActualHeight));
            // Editing keeps a row alive only while it remains in the visible pool.
            if (_editingRow is not null && (_editingRow.Index < range.First || _editingRow.Index >= range.First + range.Count)) CancelEdit();
            while (_pool.Count < range.Count) _pool.Add(CreateRow());
            for (int i = 0; i < _pool.Count; i++)
            {
                var row = _pool[i]; row.Root.Visibility = i < range.Count ? Visibility.Visible : Visibility.Collapsed;
                if (i >= range.Count) continue;
                int index = range.First + i;
                if (_editingRow == row && row.Index != index) CancelEdit();
                row.Index = index; var tag = _rows[index]; row.Name = tag.Name; row.Number.Text = (index + 1).ToString(); Canvas.SetTop(row.Root, index * RowHeight);
                for (int c = 0; c < _columns.Count; c++)
                {
                    bool selected = index >= Math.Min(_row, _anchorRow) && index <= Math.Max(_row, _anchorRow) && c >= Math.Min(_column, _anchorColumn) && c <= Math.Max(_column, _anchorColumn);
                    bool active = index == _row && c == _column;
                    var cell = row.Cells[c]; cell.Background = Brush(selected ? "DCE8F7" : index % 2 == 0 ? "FFFFFF" : "F7F8FA");
                    cell.BorderBrush = Brush(active ? "487BB2" : "D4D7DE"); cell.BorderThickness = active ? new Thickness(1) : new Thickness(0, 0, 1, 1);
                    row.Text[c].Text = _columns[c] == TagColumn.MonitorValue ? Monitor(tag) : TagTableEditor.Cell(tag, _columns[c]);
                    AutomationProperties.SetAutomationId(cell, "tag-cell-" + tag.Name + "-" + _columns[c]); AutomationProperties.SetName(cell, tag.Name + ", " + Title(_columns[c]) + ": " + row.Text[c].Text);
                }
            }
        }
        finally { _rendering = false; }
    }
    private static bool Shift() => (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
    private void Select(int row, int column, bool extend)
    {
        if (!TryCommitEdit()) return;
        _selectionVersion++; _row = Math.Clamp(row, 0, Math.Max(0, _rows.Count - 1)); _column = column;
        if (!extend || _anchorRow < 0) { _anchorRow = _row; _anchorColumn = _column; }
        Focus(FocusState.Programmatic); RenderRows(); Status(); if (ActiveTagName is string name) TagSelected?.Invoke(name);
    }
    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (_references || _editing is not null || _rows.Count == 0 || XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) == _search) return;
        int row = Math.Max(0, _row), column = _column;
        switch (e.Key)
        {
            case VirtualKey.F2: case VirtualKey.Enter: BeginEdit(); break;
            case VirtualKey.Delete: Delete(); break;
            case VirtualKey.Up: row--; goto case VirtualKey.Home;
            case VirtualKey.Down: row++; goto case VirtualKey.Home;
            case VirtualKey.Left: column--; goto case VirtualKey.Home;
            case VirtualKey.Right: column++; goto case VirtualKey.Home;
            case VirtualKey.PageUp: row -= Math.Max(1, (int)(_scroll.ActualHeight / RowHeight)); goto case VirtualKey.Home;
            case VirtualKey.PageDown: row += Math.Max(1, (int)(_scroll.ActualHeight / RowHeight)); goto case VirtualKey.Home;
            case VirtualKey.End: row = _rows.Count - 1; goto case VirtualKey.Home;
            case VirtualKey.Home:
                if (e.Key == VirtualKey.Home) row = 0;
                Select(Math.Clamp(row, 0, _rows.Count - 1), Math.Clamp(column, 0, _columns.Count - 1), Shift()); EnsureVisible(); break;
            default: return;
        }
        e.Handled = true;
    }
    private void EnsureVisible()
    {
        double top = Math.Max(0, _row) * RowHeight;
        if (top < _scroll.VerticalOffset) _scroll.ChangeView(null, top, null, true);
        else if (top + RowHeight > _scroll.VerticalOffset + _scroll.ActualHeight - 16) _scroll.ChangeView(null, top + RowHeight - Math.Max(1, _scroll.ActualHeight - 16), null, true);
        double left = Gutter + _columns.Take(_column).Sum(c => _widths[c]), right = left + _widths[_columns[_column]];
        if (left < _scroll.HorizontalOffset) _scroll.ChangeView(left, null, null, true);
        else if (right > _scroll.HorizontalOffset + _scroll.ActualWidth - 16) _scroll.ChangeView(right - Math.Max(1, _scroll.ActualWidth - 16), null, null, true);
    }
    private void BeginEdit(string? draft = null)
    {
        if (_workspace is null) { Error("This table is read-only until a Workspace is bound."); return; }
        if (_controller?.State == ControllerState.Running) { Error("Stop simulation before editing tag declarations."); return; }
        if (_row < 0 || _columns[_column] == TagColumn.MonitorValue) return;
        var row = _pool.FirstOrDefault(r => r.Index == _row && r.Root.Visibility == Visibility.Visible); if (row is null) return;
        CancelEdit(); _editingColumn = _columns[_column]; _editingRow = row;
        var editor = new TextBox { AcceptsReturn = _editingColumn == TagColumn.Comment, Text = draft ?? TagTableEditor.Cell(_rows[_row], _editingColumn), FontSize = 12, MinHeight = 22, Padding = new Thickness(4, 0, 4, 0), BorderThickness = new Thickness(1), MaxLength = 16384 };
        _editRevision = _workspace.Project.Revision; _editProjectId = _workspace.Project.Id;
        _editing = editor; AutomationProperties.SetAutomationId(editor, "tag-cell-editor"); AutomationProperties.SetName(editor, "Edit " + Title(_editingColumn)); row.Cells[_column].Child = editor;
        editor.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { CancelEdit(); Focus(FocusState.Keyboard); e.Handled = true; } else if (e.Key is VirtualKey.Enter or VirtualKey.Tab) { CommitEdit(e.Key == VirtualKey.Tab); e.Handled = true; } };
        editor.Loaded += (_, _) => { editor.Focus(FocusState.Keyboard); editor.SelectAll(); };
        editor.Focus(FocusState.Keyboard); editor.SelectAll();
        _status.Text = Title(_editingColumn) + ": Enter applies · Esc cancels · Data type and address must agree. Paste both cells to change them together.";
    }
    private void CommitEdit(bool advanceColumn) => TryCommitEdit(advanceColumn, !advanceColumn);
    public bool TryCommitEdit(bool advanceColumn = false, bool advanceRow = false)
    {
        if (_editing is null || _workspace is null || ActiveTagName is not string name) return true;
        string value = _editing.Text; long revision = _editRevision; string projectId = _editProjectId; var column = _editingColumn; int targetRow = _row, targetColumn = _column;
        CancelEdit();
        try
        {
            new TagTableEditor(_workspace).Apply([new(name, column, value)], revision, projectId);
            Query(column == TagColumn.Name ? value.Trim() : name);
            if (advanceColumn) { targetColumn = (_column + 1) % _columns.Count; if (targetColumn == 0) targetRow++; }
            else if (advanceRow) targetRow++;
            if (advanceColumn || advanceRow) { Select(Math.Min(targetRow, _rows.Count - 1), targetColumn, false); EnsureVisible(); }
            else { Focus(FocusState.Keyboard); RenderRows(); }
            return true;
        }
        catch (Exception ex) { BeginEdit(value); Error(ex.Message); return false; }
    }
    private void CancelEdit()
    {
        if (_editingRow is not null)
        {
            int c = _columns.IndexOf(_editingColumn); if (c >= 0 && c < _editingRow.Cells.Count) _editingRow.Cells[c].Child = _editingRow.Text[c];
        }
        _editing = null; _editingRow = null;
    }
    public void AddTag() => Execute(() => { string name = new TagTableEditor(RequireWorkspace()).Add(); _search.Text = ""; Query(name); EnsureVisible(); });
    private void Duplicate() => Execute(() => { if (ActiveTagName is not string selected) return; string name = new TagTableEditor(RequireWorkspace()).Add(duplicateName: selected); _search.Text = ""; Query(name); EnsureVisible(); });
    private IEnumerable<PlcTag> SelectionRows() => _rows.Skip(Math.Max(0, Math.Min(_row, _anchorRow))).Take(Math.Abs(_row - _anchorRow) + 1);
    private void Delete() => Execute(() => { if (ActiveTagName is null) return; new TagTableEditor(RequireWorkspace()).Delete(SelectionRows().Select(t => t.Name).ToArray()); });
    private Workspace RequireWorkspace() => _workspace ?? throw new InvalidOperationException("No editable workspace is bound.");
    private void Copy() => Execute(() =>
    {
        if (ActiveTagName is null) return;
        var columns = _columns.Skip(Math.Min(_column, _anchorColumn)).Take(Math.Abs(_column - _anchorColumn) + 1).ToArray();
        var data = new DataPackage(); data.SetText(TableClipboard.Write(SelectionRows().Select(t => columns.Select(c => c == TagColumn.MonitorValue ? Monitor(t) : TagTableEditor.Cell(t, c)))));
        Clipboard.SetContent(data); _status.Text = "Selected cells copied.";
    });
    private async Task PasteAsync()
    {
        try
        {
            var workspace = RequireWorkspace(); if (ActiveTagName is null) return;
            long revision = workspace.Project.Revision; string projectId = workspace.Project.Id; int version = _selectionVersion;
            int first = Math.Min(_row, _anchorRow), column = Math.Min(_column, _anchorColumn); var targets = _rows.ToArray(); var columns = _columns.ToArray();
            var data = Clipboard.GetContent(); if (!data.Contains(StandardDataFormats.Text)) return;
            var matrix = TableClipboard.Read(await data.GetTextAsync());
            if (version != _selectionVersion || _references) throw new InvalidOperationException("Selection changed while reading the clipboard. Retry paste.");
            if (first + matrix.Count > targets.Length || column + matrix[0].Count > columns.Length) throw new ArgumentException("The pasted rectangle exceeds existing rows or visible columns. Add tags or show more columns first.");
            var edits = matrix.SelectMany((r, y) => r.Select((v, x) => new TagCellEdit(targets[first + y].Name, columns[column + x], v))).ToArray();
            new TagTableEditor(workspace).Apply(edits, revision, projectId); _status.Text = $"Pasted {edits.Length} cells in one undoable transaction.";
        }
        catch (Exception ex) { Error(ex.Message); }
    }
    private void Execute(Action action) { try { if (!TryCommitEdit()) return; action(); } catch (Exception ex) { Error(ex.Message); } }
    private void Error(string message) { _status.Text = message; ToolTipService.SetToolTip(_status, message); }
    private void Status() => _status.Text = $"{_rows.Count} of {_tags.Count} tags · F2 / double-click: edit · Shift: extend selection · Ctrl+C/V: copy/paste · Virtual monitoring only";
    public void SetReferences(IEnumerable<SymbolReference> references)
    {
        CancelEdit(); _selectionVersion++; _references = true;
        var rows = new StackPanel(); rows.Children.Add(Header("Tag                     Access          Block                    Instruction / network"));
        foreach (var r in references) rows.Children.Add(Label($"{r.Tag}    {(r.Write ? "Write" : "Read")}    {r.Block}    {r.Instruction}    {r.Network}", 12));
        Content = new ScrollViewer { Content = rows };
    }
}
