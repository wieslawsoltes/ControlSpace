using ControlSpace.Engineering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static ControlSpace.Controls.Uno.EngineeringTheme;
namespace ControlSpace.Controls.Uno;

/// <summary>Open-document bar with overflow, selection, close and ordering commands.</summary>
public sealed class EditorBar : UserControl
{
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
    private readonly ScrollViewer _scroll;
    private readonly Button _left, _right;
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
        _scroll.SizeChanged += (_, _) => UpdateOverflow(); _scroll.ViewChanged += (_, _) => UpdateOverflow(); _tabs.SizeChanged += (_, _) => UpdateOverflow(); Content = root;
    }
    public void SetDocuments(IReadOnlyList<EditorDocument> documents, string? activeId)
    {
        _tabs.Children.Clear();
        foreach (var document in documents)
        {
            var tab = new Grid { Background = Brush(document.Id == activeId ? "FFFFFF" : "D8D8DF"), BorderBrush = Brush(document.Id == activeId ? "477DB0" : "ACADB4"), BorderThickness = new Thickness(1, document.Id == activeId ? 2 : 1, 1, 0) };
            tab.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); tab.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var button = Button(document.Title, () => ActivateRequested?.Invoke(document.Id), "tab-" + document.Id, document.Title);
            button.MinHeight = 25; button.MaxWidth = 210; button.Padding = new Thickness(8, 2, 8, 2); button.BorderThickness = new Thickness(0); button.Background = tab.Background;
            var close = Button("×", () => CloseRequested?.Invoke(document.Id), "close-" + document.Id, "Close " + document.Title);
            close.MinHeight = 25; close.Width = 24; close.Padding = new Thickness(0); close.BorderThickness = new Thickness(0); close.Background = tab.Background;
            var menu = new MenuFlyout();
            void Item(string text, Action action) { var item = new MenuFlyoutItem { Text = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
            Item("Close", () => CloseRequested?.Invoke(document.Id)); Item("Close other editors", () => CloseOthersRequested?.Invoke(document.Id)); Item("Close all editors", () => CloseAllRequested?.Invoke());
            Item("Move left", () => MoveRequested?.Invoke(document.Id, -1)); Item("Move right", () => MoveRequested?.Invoke(document.Id, 1));
            button.ContextFlyout = menu; tab.Children.Add(button); Grid.SetColumn(close, 1); tab.Children.Add(close); _tabs.Children.Add(tab);
        }
        UpdateOverflow();
    }
    private void UpdateOverflow()
    {
        bool overflow = _scroll.ScrollableWidth > 1;
        _left.Visibility = _right.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
        _left.IsEnabled = _scroll.HorizontalOffset > 1; _right.IsEnabled = _scroll.HorizontalOffset < _scroll.ScrollableWidth - 1;
    }
}
