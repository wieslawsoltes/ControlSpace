using ControlSpace.Core;
using ControlSpace.Engineering;
using ControlSpace.Rendering.Skia;
using ControlSpace.Simulation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using Windows.Foundation;
using Windows.System;
namespace ControlSpace.Controls.Uno;

public sealed partial class EngineeringCanvas
{
    private void HmiPressed(PointerRoutedEventArgs e)
    {
        if (HmiScreen is not { } screen || _hmiPointer is not null) return;
        var current = e.GetCurrentPoint(this); if (current.Properties.IsRightButtonPressed) return;
        Focus(FocusState.Pointer); var point = HmiPoint(current.Position);
        if (current.Position.X < 22 || current.Position.Y < 22) return;
        if (HmiRuntime)
        {
            var id = HmiHit(point); var o = screen.Objects.FirstOrDefault(o => o.Id == id);
            PressHmiRuntime(o, e);
            e.Handled = true; return;
        }
        string? handle = HmiHandles().FirstOrDefault(h => h.Bounds.Contains(current.Position.X, current.Position.Y)).Handle;
        if (handle is null)
        {
            string? hit = HmiHit(point); bool add = Modifier(VirtualKey.Control) || Modifier(VirtualKey.Shift);
            if (hit is not null)
            {
                if (add) { var ids = _hmiSelection.ToList(); if (!ids.Remove(hit)) ids.Add(hit); SelectHmi(ids); }
                else if (!_hmiSelection.Contains(hit)) SelectHmi([hit]);
                if (!_hmiSelection.Contains(hit)) { e.Handled = true; return; }
            }
            else
            {
                _hmiMarqueeBase = add ? _hmiSelection.ToArray() : []; if (!add) SelectHmi([]);
                _hmiMarqueeStart = new(Math.Clamp(point.X, 0, screen.Width), Math.Clamp(point.Y, 0, screen.Height));
            }
        }
        if (Controller?.State == ControllerState.Running && _hmiMarqueeStart is null) { e.Handled = true; return; }
        if (!CapturePointer(e.Pointer)) { CancelHmiInteraction(); return; }
        _hmiPointer = e.Pointer.PointerId; _hmiExpected = Project; _hmiMoved = _hmiDragStarted = false; _hmiPressPoint = current.Position; _hmiGestureTransform = HmiTransform;
        if (_hmiMarqueeStart is null && _hmiSelection.Count > 0) _hmiGesture = new(screen, _hmiSelection, point, handle ?? "move");
        e.Handled = true;
    }
    private void HmiMoved(PointerRoutedEventArgs e)
    {
        if (_hmiPointer != e.Pointer.PointerId || HmiRuntime) return;
        if (!ReferenceEquals(_hmiExpected, Project) || HmiScreen is not { } screen) { CancelHmiInteraction(); return; }
        var viewPoint = e.GetCurrentPoint(this).Position;
        if (!_hmiDragStarted && Math.Abs(viewPoint.X - _hmiPressPoint.X) + Math.Abs(viewPoint.Y - _hmiPressPoint.Y) < 3) return;
        _hmiDragStarted = true;
        var p = _hmiGestureTransform.ToScreen(new(viewPoint.X, viewPoint.Y));
        if (_hmiMarqueeStart is PointD start)
        {
            double x = Math.Clamp(p.X, 0, screen.Width), y = Math.Clamp(p.Y, 0, screen.Height);
            _hmiMarquee = new(Math.Min(start.X, x), Math.Min(start.Y, y), Math.Abs(x - start.X), Math.Abs(y - start.Y)); _hmiMoved = true;
        }
        else if (_hmiGesture is not null)
        {
            if (Controller?.State == ControllerState.Running) { CancelHmiInteraction(); return; }
            _hmiBoxes = _hmiGesture.Preview(p, HmiSnap && !Modifier(VirtualKey.Menu), tolerance: 5 / HmiTransform.Scale);
            _hmiMoved = screen.Objects.Any(o => _hmiBoxes.TryGetValue(o.Id, out var b) && HmiEditor.Bounds(o) != b);
            _hmiPreview = screen with { Objects = screen.Objects.Select(o => _hmiBoxes.TryGetValue(o.Id, out var b) ? o with { X = b.X, Y = b.Y, Width = b.Width, Height = b.Height } : o).ToList() };
        }
        RequestRender(); e.Handled = true;
    }
    private void HmiReleased(PointerRoutedEventArgs e)
    {
        if (_hmiPointer != e.Pointer.PointerId) return;
        if (HmiRuntime) { ReleaseHmiRuntime(e); e.Handled = true; return; }
        HmiMoved(e);
        var expected = _hmiExpected; var boxes = _hmiBoxes; bool moved = _hmiMoved; string screenId = ScreenId;
        string[]? marqueeIds = null;
        if (_hmiMarquee is RectD box && HmiScreen is { } screen && moved)
            marqueeIds = _hmiMarqueeBase.Concat(screen.Objects.Where(o => box.Contains(o.X, o.Y) && box.Contains(o.X + o.Width, o.Y + o.Height)).Select(o => o.Id)).Distinct().ToArray();
        CancelHmiInteraction();
        if (marqueeIds is not null) SelectHmi(marqueeIds);
        else if (moved && boxes is not null && expected is not null) HmiTransformRequested?.Invoke(expected, screenId, boxes);
        e.Handled = true;
    }
    private RenderResult RenderHmi(SKCanvas canvas, float width, float height)
    {
        if (HmiScreen is not { } screen) { canvas.Clear(SKColors.White); return new([], height); }
        if (_hmiExpected is not null && !ReferenceEquals(_hmiExpected, Project)) CancelHmiInteraction();
        if (_hmiGesture is not null && Controller?.State == ControllerState.Running) CancelHmiInteraction();
        ClampHmiView();
        return _renderer.HmiDesigner(canvas, width, height, _hmiPreview ?? screen, Project.Tags, Controller?.ReadView, HmiTransform,
            _hmiSelection, HmiRuntime, HmiGrid, _hmiMarquee, _hmiGesture?.GuideX, _hmiGesture?.GuideY);
    }
}
