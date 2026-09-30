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
    private void HmiKey(KeyRoutedEventArgs e)
    {
        bool control = Modifier(VirtualKey.Control), shift = Modifier(VirtualKey.Shift);
        if (e.Key == VirtualKey.Escape) { CancelHmiInteraction(); if (HmiRuntime) HmiCommandRequested?.Invoke("stop"); e.Handled = true; return; }
        if (HmiRuntime) return;
        string? command = e.Key switch
        {
            VirtualKey.A when control => "select-all", VirtualKey.C when control => "copy", VirtualKey.X when control => "cut", VirtualKey.V when control => "paste", VirtualKey.D when control => "duplicate",
            VirtualKey.Delete => "delete", VirtualKey.F2 or VirtualKey.Enter => "properties", _ => null
        };
        if (command is not null) { HmiCommandRequested?.Invoke(command); e.Handled = true; return; }
        if (!control && e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
        {
            if (HmiScreen is { } s && _hmiSelection.Count > 0 && Controller?.State != ControllerState.Running)
            {
                var selected = _hmiSelection.ToHashSet(StringComparer.Ordinal);
                double step = shift ? 10 : 1; var b = HmiEditor.Bounds(s.Objects.Where(o => selected.Contains(o.Id)));
                double dx = e.Key == VirtualKey.Left ? -step : e.Key == VirtualKey.Right ? step : 0, dy = e.Key == VirtualKey.Up ? -step : e.Key == VirtualKey.Down ? step : 0;
                dx = Math.Clamp(dx, -b.X, s.Width - b.X - b.Width); dy = Math.Clamp(dy, -b.Y, s.Height - b.Y - b.Height);
                var boxes = s.Objects.Where(o => selected.Contains(o.Id)).ToDictionary(o => o.Id, o => new RectD(o.X + dx, o.Y + dy, o.Width, o.Height));
                HmiTransformRequested?.Invoke(Project, s.Id, boxes);
            }
            e.Handled = true;
        }
    }
    private void HmiWheel(PointerRoutedEventArgs e)
    {
        double delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        if (Modifier(VirtualKey.Control)) ChangeHmiView(zoom: HmiTransform.Scale * (delta > 0 ? 1.1 : 1 / 1.1));
        else if (!HmiFit) { if (Modifier(VirtualKey.Shift)) ChangeHmiView(horizontal: _hmiScrollX - delta / HmiZoom); else ChangeHmiView(vertical: _hmiScrollY - delta / HmiZoom); }
        e.Handled = true;
    }
}
