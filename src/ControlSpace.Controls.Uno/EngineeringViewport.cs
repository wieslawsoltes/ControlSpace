using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
namespace ControlSpace.Controls.Uno;

/// <summary>Host-managed scrollbars around the reusable vector canvas. No giant backing bitmap.</summary>
public sealed class EngineeringViewport : UserControl
{
    private readonly EngineeringCanvas _canvas;
    private readonly ScrollBar _vertical = new() { Orientation = Orientation.Vertical, Width = 14, SmallChange = 24 };
    private readonly ScrollBar _horizontal = new() { Orientation = Orientation.Horizontal, Height = 14, SmallChange = 40 };
    private bool _updating;
    public EngineeringViewport(EngineeringCanvas canvas)
    {
        _canvas = canvas;
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.Children.Add(canvas); Grid.SetColumn(_vertical, 1); grid.Children.Add(_vertical); Grid.SetRow(_horizontal, 1); grid.Children.Add(_horizontal); Content = grid;
        AutomationProperties.SetAutomationId(_vertical, "ladder-scroll-vertical"); AutomationProperties.SetName(_vertical, "Ladder vertical scroll");
        AutomationProperties.SetAutomationId(_horizontal, "ladder-scroll-horizontal"); AutomationProperties.SetName(_horizontal, "Ladder horizontal scroll");
        _vertical.ValueChanged += (_, e) => { if (!_updating) _canvas.ChangeView(vertical: (float)e.NewValue); };
        _horizontal.ValueChanged += (_, e) => { if (!_updating) _canvas.ChangeView(horizontal: (float)e.NewValue); };
        canvas.ViewportChanged += Refresh; Loaded += (_, _) => Refresh();
    }
    public void Refresh()
    {
        _updating = true;
        try
        {
            bool ladder = _canvas.Mode == EditorMode.Ladder;
            _vertical.Visibility = ladder ? Visibility.Visible : Visibility.Collapsed;
            _horizontal.Visibility = ladder ? Visibility.Visible : Visibility.Collapsed;
            double zoom = Math.Max(.5, _canvas.Zoom);
            _vertical.ViewportSize = _canvas.ActualHeight / zoom; _vertical.LargeChange = _vertical.ViewportSize * .8;
            _vertical.Maximum = Math.Max(0, _canvas.ContentHeight - _vertical.ViewportSize); _vertical.Value = _canvas.ScrollOffset;
            _horizontal.ViewportSize = _canvas.ActualWidth / zoom; _horizontal.LargeChange = _horizontal.ViewportSize * .8;
            _horizontal.Maximum = Math.Max(0, _canvas.ContentWidth - _horizontal.ViewportSize); _horizontal.Value = _canvas.HorizontalOffset;
        }
        finally { _updating = false; }
    }
}
