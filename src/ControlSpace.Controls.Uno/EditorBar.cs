using ControlSpace.Engineering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using static ControlSpace.Controls.Uno.EngineeringTheme;
namespace ControlSpace.Controls.Uno;

/// <summary>Retained open-document bar: selection changes never recreate tab controls.</summary>
public sealed class EditorBar : UserControl
{
    private sealed record TabVisual(Grid Root, Button Activate, Button Close)
    {
        public string Title { get; set; } = "";
        public bool? Selected { get; set; }
    }
    private readonly Dictionary<string, TabVisual> _items = new(StringComparer.Ordinal);
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
    private readonly ScrollViewer _scroll;
    private readonly Button _left, _right;
    private string? _active;
    private bool _revealPending;
    public long TabCreations { get; private set; }
    public event Action<string>? ActivateRequested;
    public event Action<string>? CloseRequested;
    public event Action<string>? CloseOthersRequested;
    public event Action? CloseAllRequested;
    public event Action<string, int>? MoveRequested;
    public EditorBar()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); root.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _scroll = new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _left = Button("‹", () => _scroll.ChangeView(Math.Max(0, _scroll.HorizontalOffset - 180), null, null), "editors-scroll-left");
        _right = Button("›", () => _scroll.ChangeView(_scroll.HorizontalOffset + 180, null, null), "editors-scroll-right");
        root.Children.Add(_left); Grid.SetColumn(_scroll, 1); root.Children.Add(_scroll); Grid.SetColumn(_right, 2); root.Children.Add(_right);
        _scroll.SizeChanged += (_, _) => { UpdateOverflow(); RevealActive(); };
        _scroll.ViewChanged += (_, _) => UpdateOverflow();
        _tabs.SizeChanged += (_, _) => { UpdateOverflow(); RevealActive(); }; Content = root;
    }
    public void SetDocuments(IReadOnlyList<EditorDocument> documents, string? activeId)
    {
        var ids = documents.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string id in _items.Keys.ToArray()) if (!ids.Contains(id))
        { _tabs.Children.Remove(_items[id].Root); _items.Remove(id); }
        for (int index = 0; index < documents.Count; index++)
        {
            var document = documents[index];
            if (!_items.TryGetValue(document.Id, out var item))
            { item = Create(document.Id); _items.Add(document.Id, item); TabCreations++; }
            if (index >= _tabs.Children.Count || _tabs.Children[index] != item.Root)
            { _tabs.Children.Remove(item.Root); _tabs.Children.Insert(index, item.Root); }
            if (item.Title != document.Title)
            {
                item.Title = document.Title; item.Activate.Content = Label(document.Title);
                AutomationProperties.SetName(item.Activate, document.Title); ToolTipService.SetToolTip(item.Activate, document.Title);
                AutomationProperties.SetName(item.Close, "Close " + document.Title); ToolTipService.SetToolTip(item.Close, "Close " + document.Title);
            }
            bool selected = document.Id == activeId;
            if (item.Selected != selected)
            {
                item.Selected = selected;
                item.Root.Background = Brush(selected ? "FFFFFF" : "D8D8DF");
                item.Root.BorderBrush = Brush(selected ? "477DB0" : "ACADB4");
                item.Root.BorderThickness = new Thickness(1, selected ? 2 : 1, 1, 0);
                item.Activate.Background = item.Close.Background = item.Root.Background;
            }
        }
        if (_active != activeId) { _active = activeId; _revealPending = true; }
        UpdateOverflow(); RevealActive();
    }
    private TabVisual Create(string id)
    {
        var tab = new Grid(); tab.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); tab.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var button = Button("", () => ActivateRequested?.Invoke(id), "tab-" + id);
        button.MinHeight = 25; button.MaxWidth = 210; button.Padding = new Thickness(8, 2, 8, 2); button.BorderThickness = new Thickness(0);
        var close = Button("×", () => CloseRequested?.Invoke(id), "close-" + id);
        close.MinHeight = 25; close.Width = 24; close.Padding = new Thickness(0); close.BorderThickness = new Thickness(0);
        var menu = new MenuFlyout();
        void Item(string text, Action action) { var item = new MenuFlyoutItem { Text = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("Close", () => CloseRequested?.Invoke(id)); Item("Close other editors", () => CloseOthersRequested?.Invoke(id)); Item("Close all editors", () => CloseAllRequested?.Invoke());
        Item("Move left", () => MoveRequested?.Invoke(id, -1)); Item("Move right", () => MoveRequested?.Invoke(id, 1));
        button.ContextFlyout = menu; tab.Children.Add(button); Grid.SetColumn(close, 1); tab.Children.Add(close);
        return new(tab, button, close);
    }
    private void RevealActive()
    {
        if (!_revealPending || _active is null || !_items.TryGetValue(_active, out var item) || item.Root.ActualWidth <= 0 || _scroll.ViewportWidth <= 0) return;
        double x = 0;
        foreach (FrameworkElement tab in _tabs.Children)
        {
            if (tab == item.Root) break;
            if (tab.ActualWidth <= 0) return;
            x += tab.ActualWidth + _tabs.Spacing;
        }
        _revealPending = false;
        double target = x < _scroll.HorizontalOffset ? x : x + item.Root.ActualWidth > _scroll.HorizontalOffset + _scroll.ViewportWidth
            ? x + item.Root.ActualWidth - _scroll.ViewportWidth : _scroll.HorizontalOffset;
        if (Math.Abs(target - _scroll.HorizontalOffset) > 1) _scroll.ChangeView(Math.Max(0, target), null, null, true);
    }
    private void UpdateOverflow()
    {
        bool overflow = _scroll.ScrollableWidth > 1;
        _left.Visibility = _right.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
        _left.IsEnabled = _scroll.HorizontalOffset > 1; _right.IsEnabled = _scroll.HorizontalOffset < _scroll.ScrollableWidth - 1;
    }
}
