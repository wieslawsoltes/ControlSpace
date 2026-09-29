using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace ControlSpace.Controls.Uno;

/// <summary>Starts native data-package dragging from a button after a pointer threshold.
/// ButtonBase handles/captures input, so listen to handled pointer events as well.
/// Keyboard activation and ordinary clicks continue to use the button's existing action.</summary>
public static class PaletteDragSource
{
    public static void Attach(Button source, Action<DragStartingEventArgs> configure, Action<Exception>? reportError = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(configure);
        source.CanDrag = false; // This behavior owns initiation; never start two competing gestures.
        source.ManipulationMode = ManipulationModes.None;
        Point origin = default;
        uint? pointer = null;
        bool dragging = false;
        source.DragStarting += (_, e) => { e.AllowedOperations = DataPackageOperation.Copy; configure(e); };
        source.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            var point = e.GetCurrentPoint(source);
            if (!dragging && point.Properties.IsLeftButtonPressed) { pointer = e.Pointer.PointerId; origin = point.Position; }
        }), true);
        source.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(async (_, e) =>
        {
            if (dragging || pointer != e.Pointer.PointerId) return;
            var point = e.GetCurrentPoint(source);
            if (!point.Properties.IsLeftButtonPressed) { pointer = null; return; }
            if (Math.Abs(point.Position.X - origin.X) + Math.Abs(point.Position.Y - origin.Y) < 8) return;
            dragging = true; pointer = null;
            try
            {
                // Start synchronously while the native pointer is still pressed, then release
                // ButtonBase's capture so it cannot also activate when a drag ends over it.
                var operation = source.StartDragAsync(point);
                source.ReleasePointerCapture(e.Pointer);
                await operation;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { reportError?.Invoke(ex); }
            finally { dragging = false; }
        }), true);
        source.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => pointer = null), true);
        source.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, _) => pointer = null), true);
        source.Unloaded += (_, _) => pointer = null;
    }
}
