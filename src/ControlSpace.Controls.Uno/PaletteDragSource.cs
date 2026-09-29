using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace ControlSpace.Controls.Uno;

/// <summary>In-workbench pointer drag for a palette button. A captured gesture surface
/// prevents ButtonBase and native browser drag recognition from competing for input.
/// Points delivered to the host are relative to the supplied button, including outside it.</summary>
public static class PaletteDragSource
{
    public static void Attach(Button source, Action activate, Action started, Action<Point> moved,
        Action<Point> dropped, Action cancelled, Action<Exception>? reportError = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(activate);
        ArgumentNullException.ThrowIfNull(started);
        ArgumentNullException.ThrowIfNull(moved);
        ArgumentNullException.ThrowIfNull(dropped);
        ArgumentNullException.ThrowIfNull(cancelled);
        source.CanDrag = false;
        var surface = new UserControl
        {
            Content = source.Content, Padding = source.Padding, Background = source.Background,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            ManipulationMode = ManipulationModes.None, IsTabStop = false
        };
        source.Content = surface; source.Padding = new Thickness(0);
        source.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        source.VerticalContentAlignment = VerticalAlignment.Stretch;
        Point origin = default;
        uint? pointer = null;
        bool dragging = false;
        void Cancel()
        {
            bool notify = dragging; pointer = null; dragging = false;
            surface.ReleasePointerCaptures();
            if (notify) cancelled();
        }
        surface.PointerPressed += (_, e) =>
        {
            if (pointer is not null || !source.IsEnabled) return;
            var point = e.GetCurrentPoint(source);
            if (!point.Properties.IsLeftButtonPressed) return;
            source.Focus(FocusState.Pointer);
            if (!surface.CapturePointer(e.Pointer)) return;
            pointer = e.Pointer.PointerId; origin = point.Position; e.Handled = true;
        };
        surface.PointerMoved += (_, e) =>
        {
            if (pointer != e.Pointer.PointerId) return;
            var point = e.GetCurrentPoint(source);
            if (!point.Properties.IsLeftButtonPressed) { Cancel(); return; }
            try
            {
                if (!dragging && Math.Abs(point.Position.X - origin.X) + Math.Abs(point.Position.Y - origin.Y) >= 8)
                { dragging = true; started(); }
                if (dragging) moved(point.Position);
            }
            catch (Exception ex) { Cancel(); reportError?.Invoke(ex); }
            e.Handled = true;
        };
        surface.PointerReleased += (_, e) =>
        {
            if (pointer != e.Pointer.PointerId) return;
            var point = e.GetCurrentPoint(source).Position;
            bool drop = dragging; pointer = null; dragging = false;
            // Clear the gesture before callbacks: a committed drop may rebuild the palette.
            surface.ReleasePointerCapture(e.Pointer); e.Handled = true;
            try
            {
                if (drop) dropped(point);
                else if (point.X >= 0 && point.Y >= 0 && point.X < source.ActualWidth && point.Y < source.ActualHeight)
                    activate();
            }
            catch (Exception ex) { cancelled(); reportError?.Invoke(ex); }
        };
        surface.PointerCanceled += (_, _) => Cancel();
        surface.PointerCaptureLost += (_, _) => { if (pointer is not null) Cancel(); };
        source.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape && pointer is not null) { Cancel(); e.Handled = true; } };
        source.Unloaded += (_, _) => Cancel();
        source.IsEnabledChanged += (_, _) => { if (!source.IsEnabled) Cancel(); };
    }
}
