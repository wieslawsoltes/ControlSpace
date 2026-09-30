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
    private string? HmiHit(PointD point)
    {
        if (HmiScreen is not { } screen) return null;
        double tolerance = 4 / HmiTransform.Scale;
        for (int i = screen.Objects.Count - 1; i >= 0; i--)
            if (HmiShapeGeometry.Contains(screen.Objects[i], point, tolerance)) return screen.Objects[i].Id;
        return null;
    }
    public IReadOnlyList<(string Handle, RectD Bounds)> HmiHandles()
    {
        if (HmiRuntime || HmiScreen is not { } screen || _hmiSelection.Count == 0) return [];
        var selected = _hmiSelection.ToHashSet(StringComparer.Ordinal);
        var chosen = (_hmiPreview ?? screen).Objects.Where(o => selected.Contains(o.Id)).ToArray(); if (chosen.Length == 0) return [];
        var bounds = HmiTransform.ToView(HmiEditor.Bounds(chosen)); string[] names = ["nw", "n", "ne", "e", "se", "s", "sw", "w"];
        return EngineeringRenderer.HandlePoints(bounds).Select((p, i) => (names[i], new RectD(p.X - 5, p.Y - 5, 10, 10))).ToArray();
    }
}
