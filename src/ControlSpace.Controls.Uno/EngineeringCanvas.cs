using ControlSpace.Core;
using ControlSpace.Rendering.Skia;
using ControlSpace.Simulation;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
namespace ControlSpace.Controls.Uno;

public enum EditorMode { Ladder, Devices, Hmi, Trace }

/// <summary>Shared host-backed Skia editor. No CPU bitmap uploads or private render loop.</summary>
public sealed class EngineeringCanvas : SKCanvasElement, IDisposable
{
    private readonly EngineeringRenderer _renderer = new();
    private IReadOnlyList<HitRegion> _hits = [];
    private Point? _dragStart;
    private string? _dragId, _pressedTag;
    private PointD _original;
    private float _contentHeight;
    public ControlProject Project { get; set; } = DemoProject.Create();
    public VirtualPlc? Controller { get; set; }
    public EditorMode Mode { get; set; }
    public string BlockId { get; set; } = "main";
    public string? Selection { get; set; }
    public bool HmiRuntime { get; set; }
    public float ScrollOffset { get; set; }
    public float Zoom { get; set; } = 1;
    public event Action<string>? Selected;
    public event Action<string, PointD>? MoveRequested;
    public event Action<string, bool>? HmiInput;
    public EngineeringCanvas()
    {
        MinHeight = 220; MinWidth = 320;
        PointerPressed += Pressed; PointerReleased += Released; PointerCanceled += Cancelled;
        PointerCaptureLost += Cancelled; PointerWheelChanged += Wheel;
        SizeChanged += (_, _) => Invalidate();
    }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        float width = (float)area.Width, height = (float)area.Height;
        var snapshot = Controller?.Snapshot(); RenderResult? result = null;
        switch (Mode)
        {
            case EditorMode.Ladder:
                var block = Project.Blocks.Find(b => b.Id == BlockId) ?? Project.Blocks.FirstOrDefault(b => b.Language == BlockLanguage.LAD);
                if (block is not null) result = _renderer.Ladder(canvas, width, height, block, Project.Tags, snapshot, Selection, ScrollOffset, Zoom);
                else canvas.Clear(SKColors.White);
                break;
            case EditorMode.Devices: result = _renderer.Devices(canvas, width, height, Project, Selection); break;
            case EditorMode.Hmi:
                canvas.Clear(SKColor.Parse("#E1E5E8"));
                if (Project.Screens.Count > 0) result = _renderer.Hmi(canvas, width, height, Project.Screens[0], Project.Tags, snapshot, Selection, HmiRuntime);
                break;
            case EditorMode.Trace: _renderer.Trace(canvas, width, height, Controller?.Trace.Read() ?? [], Project.Tags); break;
        }
        _hits = result?.Hits ?? []; _contentHeight = result?.ContentHeight ?? height;
    }
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this).Position;
        double x = point.X, y = point.Y;
        if (Mode == EditorMode.Ladder) { x /= Zoom; y = y / Zoom + ScrollOffset; }
        var hit = _hits.LastOrDefault(h => h.Bounds.Contains(x, y));
        Selection = hit?.Id; if (hit is null) { Invalidate(); return; }
        Selected?.Invoke(hit.Id); Invalidate();
        if (Mode == EditorMode.Hmi && HmiRuntime)
        {
            var item = Project.Screens.SelectMany(s => s.Objects).FirstOrDefault(o => o.Id == hit.Id);
            if (item?.Kind == HmiKind.Button && !string.IsNullOrEmpty(item.Tag)) { _pressedTag = item.Tag; HmiInput?.Invoke(item.Tag, true); CapturePointer(e.Pointer); }
        }
        else if ((Mode == EditorMode.Devices || Mode == EditorMode.Hmi) && Controller?.State != ControllerState.Running)
        {
            _dragId = hit.Id; _dragStart = point;
            if (Mode == EditorMode.Devices) { var d = Project.Devices.First(o => o.Id == hit.Id); _original = new(d.X, d.Y); }
            else { var h = Project.Screens.SelectMany(s => s.Objects).First(o => o.Id == hit.Id); _original = new(h.X, h.Y); }
            CapturePointer(e.Pointer);
        }
        e.Handled = true;
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedTag is not null) { HmiInput?.Invoke(_pressedTag, false); _pressedTag = null; }
        if (_dragStart is Point start && _dragId is string id)
        {
            var end = e.GetCurrentPoint(this).Position; double scale = 1;
            if (Mode == EditorMode.Hmi && Project.Screens.Count > 0) scale = Math.Max(.1, Math.Min((ActualWidth - 48) / Project.Screens[0].Width, (ActualHeight - 48) / Project.Screens[0].Height));
            if (Math.Abs(end.X - start.X) + Math.Abs(end.Y - start.Y) > 4)
                MoveRequested?.Invoke(id, new(Math.Max(0, Math.Round((_original.X + (end.X - start.X) / scale) / 10) * 10), Math.Max(0, Math.Round((_original.Y + (end.Y - start.Y) / scale) / 10) * 10)));
        }
        _dragId = null; _dragStart = null; ReleasePointerCapture(e.Pointer);
    }
    private void Cancelled(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedTag is not null) HmiInput?.Invoke(_pressedTag, false); _pressedTag = null; _dragId = null; _dragStart = null;
    }
    private void Wheel(object sender, PointerRoutedEventArgs e)
    {
        if (Mode != EditorMode.Ladder) return;
        ScrollOffset = Math.Clamp(ScrollOffset - e.GetCurrentPoint(this).Properties.MouseWheelDelta * .5f, 0, Math.Max(0, _contentHeight - (float)ActualHeight / Zoom));
        Invalidate(); e.Handled = true;
    }
    public void Dispose() => _renderer.Dispose();
}
