using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
using static ControlSpace.Controls.Uno.EngineeringTheme;

namespace ControlSpace.Controls.Uno;

/// <summary>A reusable palette item with click, keyboard and captured-pointer drag input.
/// The explicit Border owns the entire row's hit area; there is no nested ButtonBase
/// or native browser drag recognizer competing for capture.</summary>
public sealed class PaletteDragSource : UserControl
{
    private readonly Border _surface;
    private readonly Action _activate;
    private Point _origin;
    private uint? _pointer;
    private bool _dragging;
    public static string LastPointerEvent { get; private set; } = "idle";
    public event Action? DragStarted;
    public event Action<Point>? DragMoved;
    public event Action<Point>? Dropped;
    public event Action? DragCancelled;
    public event Action<Exception>? Failed;
    public PaletteDragSource(string caption, Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate); _activate = activate;
        IsTabStop = true; MinHeight = 24; HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        ManipulationMode = ManipulationModes.None;
        _surface = new Border { Background = Brush("F2F2F4"), Padding = new Thickness(17, 2, 5, 2), Child = Label(caption) };
        Content = _surface;
        AutomationProperties.SetName(this, caption); AutomationProperties.SetAutomationId(this, "palette-" + caption);
        PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released;
        PointerCanceled += (_, _) => Cancel(); PointerCaptureLost += (_, _) => { if (_pointer is not null) Cancel(); };
        Unloaded += (_, _) => Cancel(); IsEnabledChanged += (_, _) => { if (!IsEnabled) Cancel(); };
        GotFocus += (_, _) => _surface.Background = Brush("E5EDF7");
        LostFocus += (_, _) => _surface.Background = Brush("F2F2F4");
        KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape && _pointer is not null) { Cancel(); e.Handled = true; }
            else if ((e.Key is VirtualKey.Enter or VirtualKey.Space) && _pointer is null)
            { _activate(); e.Handled = true; }
        };
    }
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        LastPointerEvent = "pressed:" + e.Pointer.PointerId;
        if (_pointer is not null || !IsEnabled) return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        Focus(FocusState.Pointer);
        if (!CapturePointer(e.Pointer)) { LastPointerEvent = "capture rejected"; return; }
        _pointer = e.Pointer.PointerId; _origin = point.Position; e.Handled = true;
        LastPointerEvent = "captured:" + _pointer;
    }
    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointer != e.Pointer.PointerId) return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) { Cancel(); return; }
        try
        {
            if (!_dragging && Math.Abs(point.Position.X - _origin.X) + Math.Abs(point.Position.Y - _origin.Y) >= 8)
            { _dragging = true; LastPointerEvent = "dragging"; DragStarted?.Invoke(); }
            if (_dragging) DragMoved?.Invoke(point.Position);
        }
        catch (Exception ex) { Cancel(); Failed?.Invoke(ex); }
        e.Handled = true;
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (_pointer != e.Pointer.PointerId) return;
        var point = e.GetCurrentPoint(this).Position; bool drop = _dragging;
        // A drop may rebuild the entire palette. Clear state before the host callback.
        _pointer = null; _dragging = false; ReleasePointerCapture(e.Pointer); e.Handled = true;
        LastPointerEvent = drop ? "dropped" : "clicked";
        try
        {
            if (drop) Dropped?.Invoke(point);
            else if (point.X >= 0 && point.Y >= 0 && point.X < ActualWidth && point.Y < ActualHeight) _activate();
        }
        catch (Exception ex) { DragCancelled?.Invoke(); Failed?.Invoke(ex); }
    }
    private void Cancel()
    {
        bool notify = _dragging; bool pressed = _pointer is not null;
        _pointer = null; _dragging = false; ReleasePointerCaptures();
        if (pressed) LastPointerEvent = "cancelled";
        if (notify) DragCancelled?.Invoke();
    }
}
