using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using static ControlSpace.Controls.Uno.EngineeringTheme;
namespace ControlSpace.Controls.Uno;

/// <summary>Reusable compact pane chrome; the host owns layout and persistence.</summary>
public sealed class WorkbenchPane : UserControl
{
    private readonly Grid _header = new() { Height = 24, Background = Brush("C8C9CD") };
    private readonly TextBlock _caption;
    private readonly Button _collapse, _pin;
    private readonly ContentControl _body;
    private bool _expanded = true;
    public event Action? ToggleRequested;
    public event Action? PinRequested;
    public WorkbenchPane(string title, UIElement content, string id)
    {
        _caption = Label(title, 12, bold: true); _caption.Margin = new Thickness(6, 0, 0, 0);
        _header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); _header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _header.Children.Add(_caption);
        _pin = Button("•", () => PinRequested?.Invoke(), id + "-pin", "Pin / collapse automatically");
        _collapse = Button("‹", () => ToggleRequested?.Invoke(), id + "-toggle", "Show / hide " + title);
        foreach (var b in new[] { _pin, _collapse }) { b.Width = 24; b.Height = 24; b.MinHeight = 24; b.Padding = new Thickness(0); b.BorderThickness = new Thickness(0); b.Background = Brush("C8C9CD"); }
        Grid.SetColumn(_pin, 1); Grid.SetColumn(_collapse, 2); _header.Children.Add(_pin); _header.Children.Add(_collapse);
        var grid = new Grid { Background = Brush("F2F2F4") };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); grid.Children.Add(_header);
        _body = new ContentControl { Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        Grid.SetRow(_body, 1); grid.Children.Add(_body); Content = grid;
        AutomationProperties.SetAutomationId(this, id); AutomationProperties.SetName(this, title);
        GotFocus += (_, _) => _header.Background = Brush("B8CBE1"); LostFocus += (_, _) => _header.Background = Brush("C8C9CD");
    }
    public void SetExpanded(bool expanded)
    {
        _expanded = expanded; _body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        _caption.Visibility = _pin.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        _collapse.Content = expanded ? "‹" : "›";
    }
    public void SetPinned(bool pinned) { _pin.Content = pinned ? "•" : "○"; ToolTipService.SetToolTip(_pin, pinned ? "Collapse automatically" : "Expand permanently"); }
    public void SetTitle(string title) => _caption.Text = title;
    public void FocusPane() { if (_expanded) _collapse.Focus(FocusState.Keyboard); }
}

/// <summary>Pointer/touch and keyboard splitter with a bounded delta supplied to the host.</summary>
public sealed class WorkbenchSplitter : UserControl
{
    private uint? _pointer;
    private double _previous;
    public event Action<double>? ResizeRequested;
    public event Action? ResetRequested;
    public WorkbenchSplitter(bool horizontal, string id)
    {
        IsTabStop = true; Background = Brush("ACADB4");
        Content = new Border { Background = Brush("ACADB4") };
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetAutomationId(this, id); AutomationProperties.SetName(this, horizontal ? "Resize inspector height" : "Resize pane width");
        ToolTipService.SetToolTip(this, "Drag to resize. Arrow keys: 10 pixels. Home: reset.");
        PointerPressed += (_, e) =>
        {
            if (_pointer is not null) return;
            Focus(FocusState.Pointer);
            if (!CapturePointer(e.Pointer)) return;
            var point = e.GetCurrentPoint(null).Position;
            _previous = horizontal ? point.Y : point.X; _pointer = e.Pointer.PointerId; e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (_pointer != e.Pointer.PointerId) return;
            var point = e.GetCurrentPoint(null).Position; double position = horizontal ? point.Y : point.X;
            double delta = position - _previous; _previous = position;
            if (delta != 0) ResizeRequested?.Invoke(delta); e.Handled = true;
        };
        PointerReleased += (_, e) => { if (_pointer != e.Pointer.PointerId) return; _pointer = null; ReleasePointerCapture(e.Pointer); e.Handled = true; };
        PointerCanceled += (_, _) => _pointer = null; PointerCaptureLost += (_, _) => _pointer = null;
        DoubleTapped += (_, e) => { ResetRequested?.Invoke(); e.Handled = true; };
        KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Home) { ResetRequested?.Invoke(); e.Handled = true; }
            else if (e.Key == (horizontal ? VirtualKey.Up : VirtualKey.Left)) { ResizeRequested?.Invoke(-10); e.Handled = true; }
            else if (e.Key == (horizontal ? VirtualKey.Down : VirtualKey.Right)) { ResizeRequested?.Invoke(10); e.Handled = true; }
        };
    }
}
