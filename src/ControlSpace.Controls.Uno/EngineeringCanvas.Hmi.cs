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
    private readonly List<string> _hmiSelection = [];
    private readonly HmiMomentaryInput _hmiInput = new();
    private ControlProject? _hmiExpected;
    private HmiTransformSession? _hmiGesture;
    private IReadOnlyDictionary<string, RectD>? _hmiBoxes;
    private HmiScreen? _hmiPreview;
    private PointD? _hmiMarqueeStart;
    private RectD? _hmiMarquee;
    private string[] _hmiMarqueeBase = [];
    private uint? _hmiPointer;
    private string? _hmiBoundScreen;
    private bool _hmiMoved, _hmiDragStarted;
    private Point _hmiPressPoint;
    private HmiViewportTransform _hmiGestureTransform;
    private double _hmiScrollX, _hmiScrollY;
    public bool HmiFit { get; private set; } = true;
    public double HmiZoom { get; private set; } = 1;
    public bool HmiGrid { get; set; } = true;
    public bool HmiSnap { get; set; } = true;
    public IReadOnlyList<string> HmiSelection => _hmiSelection;
    public IReadOnlyDictionary<string, RectD>? HmiPreviewBoxes => _hmiBoxes;
    public bool HmiGestureActive => _hmiPointer is not null;
    public int HmiDrawnObjects => _renderer.LastHmiDrawnObjects;
    private HmiScreen? HmiScreen => Project.Screens.FirstOrDefault(s => s.Id == ScreenId);
    public HmiViewportTransform HmiTransform => HmiScreen is { } screen ? HmiViewportTransform.Create(screen, ActualWidth, ActualHeight, HmiFit, HmiZoom, _hmiScrollX, _hmiScrollY) : new(1, 28, 28);
    public double HmiScrollX => _hmiScrollX;
    public double HmiScrollY => _hmiScrollY;
    public double HmiExtentWidth => (HmiScreen?.Width ?? 0) + 56 / HmiTransform.Scale;
    public double HmiExtentHeight => (HmiScreen?.Height ?? 0) + 56 / HmiTransform.Scale;
    public event Action<IReadOnlyList<string>>? HmiSelectionChanged;
    public event Action<ControlProject, string, IReadOnlyDictionary<string, RectD>>? HmiTransformRequested;
    public event Action<string>? HmiCommandRequested;
    public event Action<string>? HmiEditRequested;
    public event Action<Point>? HmiContextRequested;
    public event Action? HmiRuntimeChanged;
    private static bool Modifier(VirtualKey key) => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    public void BindHmiScreen()
    {
        if (_hmiBoundScreen != ScreenId)
        {
            CancelHmiInteraction(); _hmiBoundScreen = ScreenId; _hmiSelection.Clear(); HmiFit = true; _hmiScrollX = _hmiScrollY = 0;
        }
        if (_hmiExpected is not null && !ReferenceEquals(_hmiExpected, Project)) CancelHmiInteraction();
        var ids = HmiScreen?.Objects.Select(o => o.Id).ToHashSet() ?? [];
        _hmiSelection.RemoveAll(id => !ids.Contains(id)); RequestRender();
    }
    public void SelectHmi(IEnumerable<string> ids, bool notify = true)
    {
        var valid = HmiScreen?.Objects.Select(o => o.Id).ToHashSet() ?? [];
        var selection = ids.Where(valid.Contains).Distinct().ToArray();
        _hmiSelection.Clear(); _hmiSelection.AddRange(selection); Selection = _hmiSelection.FirstOrDefault();
        RequestRender(); if (notify) HmiSelectionChanged?.Invoke(_hmiSelection.ToArray());
    }
    public void ChangeHmiView(double? zoom = null, bool? fit = null, double? horizontal = null, double? vertical = null)
    {
        CancelHmiInteraction();
        if (zoom is double z) { HmiZoom = double.IsFinite(z) ? Math.Clamp(z, .1, 4) : 1; HmiFit = false; }
        if (fit is bool f) HmiFit = f;
        if (horizontal is double x) _hmiScrollX = double.IsFinite(x) ? x : 0;
        if (vertical is double y) _hmiScrollY = double.IsFinite(y) ? y : 0;
        ClampHmiView(); RequestRender(); ViewportChanged?.Invoke();
    }
    private void ClampHmiView()
    {
        if (HmiFit) { _hmiScrollX = _hmiScrollY = 0; return; }
        _hmiScrollX = Math.Clamp(_hmiScrollX, 0, Math.Max(0, HmiExtentWidth - ActualWidth / HmiZoom));
        _hmiScrollY = Math.Clamp(_hmiScrollY, 0, Math.Max(0, HmiExtentHeight - ActualHeight / HmiZoom));
    }
    public void CancelHmiInteraction()
    {
        bool active = _hmiPointer is not null, pressed = _hmiInput.IsPressed;
        _hmiPointer = null; _hmiExpected = null; _hmiGesture = null; _hmiBoxes = null; _hmiPreview = null; _hmiMarqueeStart = null; _hmiMarquee = null; _hmiMoved = _hmiDragStarted = false;
        _hmiInput.Release(); if (active) ReleasePointerCaptures();
        if (pressed) HmiRuntimeChanged?.Invoke(); if (active) RequestRender();
    }
    private PointD HmiPoint(Point point) => HmiTransform.ToScreen(new(point.X, point.Y));
    private string? HmiHit(PointD point) => HmiScreen?.Objects.LastOrDefault(o => HmiEditor.Bounds(o).Contains(point.X, point.Y))?.Id;
    public IReadOnlyList<(string Handle, RectD Bounds)> HmiHandles()
    {
        if (HmiRuntime || HmiScreen is not { } screen || _hmiSelection.Count == 0) return [];
        var chosen = (_hmiPreview ?? screen).Objects.Where(o => _hmiSelection.Contains(o.Id)).ToArray(); if (chosen.Length == 0) return [];
        var bounds = HmiTransform.ToView(HmiEditor.Bounds(chosen)); string[] names = ["nw", "n", "ne", "e", "se", "s", "sw", "w"];
        return EngineeringRenderer.HandlePoints(bounds).Select((p, i) => (names[i], new RectD(p.X - 5, p.Y - 5, 10, 10))).ToArray();
    }
    private void HmiPressed(PointerRoutedEventArgs e)
    {
        if (HmiScreen is not { } screen || _hmiPointer is not null) return;
        var current = e.GetCurrentPoint(this); if (current.Properties.IsRightButtonPressed) return;
        Focus(FocusState.Pointer); var point = HmiPoint(current.Position);
        if (current.Position.X < 22 || current.Position.Y < 22) return;
        if (HmiRuntime)
        {
            var id = HmiHit(point); var o = screen.Objects.FirstOrDefault(o => o.Id == id);
            if (o?.Kind == HmiKind.Button && _hmiInput.Press(Controller, o.Tag))
            { if (CapturePointer(e.Pointer)) _hmiPointer = e.Pointer.PointerId; else _hmiInput.Release(); HmiRuntimeChanged?.Invoke(); RequestRender(); }
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
        if (!HmiRuntime) HmiMoved(e);
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
    private void HmiKey(KeyRoutedEventArgs e)
    {
        bool control = Modifier(VirtualKey.Control), shift = Modifier(VirtualKey.Shift);
        if (e.Key == VirtualKey.Escape) { CancelHmiInteraction(); e.Handled = true; return; }
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
                double step = shift ? 10 : 1; var b = HmiEditor.Bounds(s.Objects.Where(o => _hmiSelection.Contains(o.Id)));
                double dx = e.Key == VirtualKey.Left ? -step : e.Key == VirtualKey.Right ? step : 0, dy = e.Key == VirtualKey.Up ? -step : e.Key == VirtualKey.Down ? step : 0;
                dx = Math.Clamp(dx, -b.X, s.Width - b.X - b.Width); dy = Math.Clamp(dy, -b.Y, s.Height - b.Y - b.Height);
                var boxes = s.Objects.Where(o => _hmiSelection.Contains(o.Id)).ToDictionary(o => o.Id, o => new RectD(o.X + dx, o.Y + dy, o.Width, o.Height));
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
