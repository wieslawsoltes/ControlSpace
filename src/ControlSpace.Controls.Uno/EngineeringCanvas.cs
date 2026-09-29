using ControlSpace.Core;
using ControlSpace.Engineering;
using Microsoft.UI.Xaml;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
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
    private readonly HashSet<string> _collapsed = [];
    private int _foldVersion, _layoutVersion = -1;
    private ProgramBlock? _layoutBlock;
    private LadderLayout? _layout;
    private double _layoutWidth;
    private bool _fontLoading, _disposed;
    private (double, double, float, float, float, double, double, EditorMode) _lastMetrics;
    public double ContentWidth => _layout?.Width ?? ActualWidth;
    public double ContentHeight => _contentHeight;
    public string RendererFont => _renderer.FontFamily;
    public IReadOnlyList<HitRegion> HitRegions => _hits;
    public IReadOnlySet<string> CollapsedNetworks => _collapsed;
    public float HorizontalOffset { get; set; }
    public event Action? ViewportChanged;
    public event Action<string>? EditRequested;
    public event Action<string>? ShortcutRequested;
    public event Action<string, Point>? ContextMenuRequested;
    public event Action<InstructionKind, string?, ControlProject>? InstructionDropped;
    public void ResetProjectView() { _collapsed.Clear(); _foldVersion++; Selection = null; ScrollOffset = HorizontalOffset = 0; }
    private LadderLayout? GetLayout()
    {
        var b = Project.Blocks.FirstOrDefault(b => b.Id == BlockId && b.Language == BlockLanguage.LAD);
        if (b is null) return _layout = null;
        double width = Math.Max(160, ActualWidth) / Math.Clamp(Zoom, .5f, 2);
        if (!ReferenceEquals(b, _layoutBlock) || width != _layoutWidth || _layoutVersion != _foldVersion)
        {
            _layoutBlock = b; _layoutWidth = width; _layoutVersion = _foldVersion;
            _layout = new(b, width, _collapsed);
        }
        return _layout;
    }
    public void ChangeView(float? vertical = null, float? horizontal = null, float? zoom = null)
    {
        if (zoom is float z) Zoom = float.IsFinite(z) ? Math.Clamp(z, .5f, 2) : 1;
        if (vertical is float v) ScrollOffset = float.IsFinite(v) ? v : 0;
        if (horizontal is float h) HorizontalOffset = float.IsFinite(h) ? h : 0;
        ClampView(); Invalidate();
    }
    private void ClampView()
    {
        var layout = GetLayout(); if (layout is null) return;
        Zoom = float.IsFinite(Zoom) ? Math.Clamp(Zoom, .5f, 2) : 1;
        ScrollOffset = (float)Math.Clamp(float.IsFinite(ScrollOffset) ? ScrollOffset : 0, 0, Math.Max(0, layout.Height - ActualHeight / Zoom));
        HorizontalOffset = (float)Math.Clamp(float.IsFinite(HorizontalOffset) ? HorizontalOffset : 0, 0, Math.Max(0, layout.Width - ActualWidth / Zoom));
    }
    public void ToggleNetwork(string id)
    {
        if (!_collapsed.Add(id)) _collapsed.Remove(id); _foldVersion++; ChangeView();
    }
    public void CollapseAll(bool collapse)
    {
        var b = Project.Blocks.FirstOrDefault(b => b.Id == BlockId); if (b is null) return;
        foreach (var n in b.Networks) { if (collapse) _collapsed.Add(n.Id); else _collapsed.Remove(n.Id); }
        _foldVersion++; ChangeView();
    }
    public void Reveal(string id)
    {
        var b = Project.Blocks.FirstOrDefault(b => b.Id == BlockId); if (b is null) return;
        var n = ProgramEditor.FindNetwork(b, id); if (n is null) return;
        if (id != n.Id && _collapsed.Remove(n.Id)) _foldVersion++;
        var layout = GetLayout()!; var row = layout.Networks.First(r => r.Id == n.Id);
        double y = row.Y, targetHeight = row.Collapsed ? 26 : Math.Min(row.Height, ActualHeight / Zoom);
        if (y < ScrollOffset) ScrollOffset = (float)y;
        else if (y + targetHeight > ScrollOffset + ActualHeight / Zoom) ScrollOffset = (float)(y + targetHeight - ActualHeight / Zoom);
        int column = n.Branches.SelectMany(path => path.Select((i, c) => (i, c))).FirstOrDefault(x => x.i.Id == id).c;
        if (id != n.Id)
        {
            var path = n.Branches.FindIndex(path => path.Any(i => i.Id == id));
            double cy = row.Y + 104 + Math.Max(0, path) * LadderLayout.BranchSpacing;
            if (cy - 45 < ScrollOffset) ScrollOffset = (float)(cy - 45);
            else if (cy + 48 > ScrollOffset + ActualHeight / Zoom) ScrollOffset = (float)(cy + 48 - ActualHeight / Zoom);
            double x = id == n.Output.Id ? layout.Width - 147 : LadderLayout.Contact(column, cy).X;
            if (x < HorizontalOffset) HorizontalOffset = (float)x;
            else if (x + 124 > HorizontalOffset + ActualWidth / Zoom) HorizontalOffset = (float)(x + 124 - ActualWidth / Zoom);
        }
        Selection = id; ChangeView();
    }
    private HitRegion? Hit(Point point)
    {
        double x = point.X, y = point.Y;
        if (Mode == EditorMode.Ladder) { x = x / Zoom + HorizontalOffset; y = y / Zoom + ScrollOffset; }
        return _hits.LastOrDefault(h => h.Bounds.Contains(x, y));
    }
    private async Task LoadFontsAsync()
    {
        if (_fontLoading || _disposed) return; _fontLoading = true;
        try
        {
            var regular = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Uno.Fonts.OpenSans/Fonts/OpenSans-Regular.ttf"));
            var bold = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Uno.Fonts.OpenSans/Fonts/OpenSans-SemiBold.ttf"));
            using var normalStream = await regular.OpenStreamForReadAsync(); using var boldStream = await bold.OpenStreamForReadAsync();
            if (_disposed) return; _renderer.SetFonts(normalStream, boldStream); Invalidate();
        }
        catch (Exception ex) { Console.WriteLine("[ControlSpace] Renderer font fallback: " + ex.Message); }
    }
    public ControlProject Project { get; set; } = DemoProject.Create();
    public VirtualPlc? Controller { get; set; }
    public EditorMode Mode { get; set; }
    public string BlockId { get; set; } = "main";
    public string ScreenId { get; set; } = "";
    private HmiScreen? CurrentScreen => Project.Screens.FirstOrDefault(s => s.Id == ScreenId) ?? Project.Screens.FirstOrDefault();
    public string? Selection { get; set; }
    public bool HmiRuntime { get; set; }
    public float ScrollOffset { get; set; }
    public float Zoom { get; set; } = 1;
    public event Action<string>? Selected;
    public event Action<string, PointD>? MoveRequested;
    public event Action<string, bool>? HmiInput;
    public EngineeringCanvas()
    {
        MinHeight = 100; MinWidth = 160; IsTabStop = true;
        PointerPressed += Pressed; PointerReleased += Released; PointerCanceled += Cancelled;
        PointerCaptureLost += Cancelled; PointerWheelChanged += Wheel;
        SizeChanged += (_, _) => Invalidate(); Loaded += async (_, _) => await LoadFontsAsync();
        DoubleTapped += (_, e) => { if (Mode == EditorMode.Ladder && Hit(e.GetPosition(this)) is HitRegion hit) { EditRequested?.Invoke(hit.Id); e.Handled = true; } };
        RightTapped += (_, e) => { if (Mode == EditorMode.Ladder && Hit(e.GetPosition(this)) is HitRegion hit) { Selection = hit.Id; Selected?.Invoke(hit.Id); ContextMenuRequested?.Invoke(hit.Id, e.GetPosition(this)); e.Handled = true; } };
        KeyDown += LadderKey;
        AllowDrop = true;
        DragOver += (_, e) => { if (Mode == EditorMode.Ladder && e.DataView.Properties.TryGetValue("ControlSpace.Instruction", out var value) && value is string text && Enum.TryParse<InstructionKind>(text, out var kind) && Enum.IsDefined(kind)) { e.AcceptedOperation = DataPackageOperation.Copy; e.Handled = true; } };
        Drop += (_, e) =>
        {
            if (Mode != EditorMode.Ladder || !e.DataView.Properties.TryGetValue("ControlSpace.Instruction", out var value) || value is not string text || !Enum.TryParse<InstructionKind>(text, out var kind) || !Enum.IsDefined(kind)) return;
            InstructionDropped?.Invoke(kind, Hit(e.GetPosition(this))?.Id, Project); e.Handled = true;
        };
    }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        float width = (float)area.Width, height = (float)area.Height;
        var snapshot = Controller?.Snapshot(); RenderResult? result = null;
        switch (Mode)
        {
            case EditorMode.Ladder:
                var block = Project.Blocks.Find(b => b.Id == BlockId && b.Language == BlockLanguage.LAD);
                ClampView();
                if (block is not null) result = _renderer.Ladder(canvas, width, height, block, Project.Tags, snapshot, Selection, ScrollOffset, Zoom, HorizontalOffset, GetLayout());
                else canvas.Clear(SKColors.White);
                break;
            case EditorMode.Devices: result = _renderer.Devices(canvas, width, height, Project, Selection); break;
            case EditorMode.Hmi:
                canvas.Clear(SKColor.Parse("#E1E5E8"));
                if (CurrentScreen is HmiScreen screen) result = _renderer.Hmi(canvas, width, height, screen, Project.Tags, snapshot, Selection, HmiRuntime);
                break;
            case EditorMode.Trace: _renderer.Trace(canvas, width, height, Controller?.Trace.Read() ?? [], Project.Tags); break;
        }
        _hits = result?.Hits ?? []; _contentHeight = result?.ContentHeight ?? height;
        var metrics = (ContentWidth, ContentHeight, ScrollOffset, HorizontalOffset, Zoom, ActualWidth, ActualHeight, Mode);
        if (metrics != _lastMetrics) { _lastMetrics = metrics; DispatcherQueue.TryEnqueue(() => { if (!_disposed) ViewportChanged?.Invoke(); }); }
    }
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        var point = e.GetCurrentPoint(this).Position;
        var hit = Hit(point);
        Selection = hit?.Id; if (hit is null) { Invalidate(); return; }
        if (Mode == EditorMode.Ladder && hit.Kind == "network-toggle" && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) ToggleNetwork(hit.Id);
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
            if (Mode == EditorMode.Hmi && CurrentScreen is HmiScreen screen) scale = Math.Max(.1, Math.Min((ActualWidth - 48) / screen.Width, (ActualHeight - 48) / screen.Height));
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
        float delta = -e.GetCurrentPoint(this).Properties.MouseWheelDelta * .5f;
        bool shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (shift) ChangeView(horizontal: HorizontalOffset + delta); else ChangeView(vertical: ScrollOffset + delta);
        e.Handled = true;
    }
    private void LadderKey(object sender, KeyRoutedEventArgs e)
    {
        if (Mode != EditorMode.Ladder) return;
        bool shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        bool control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        string? command = e.Key switch { VirtualKey.F9 when !shift => "contact", VirtualKey.F10 => "negated", VirtualKey.F2 or VirtualKey.Enter => "edit", VirtualKey.Insert => "network-add", VirtualKey.Delete => "delete", VirtualKey.F8 when shift => "branch-add", VirtualKey.D when control => "network-duplicate", _ => null };
        if (command is not null) { ShortcutRequested?.Invoke(command); e.Handled = true; return; }
        if (e.Key is VirtualKey.PageDown or VirtualKey.PageUp) { ChangeView(vertical: ScrollOffset + (e.Key == VirtualKey.PageDown ? 1 : -1) * (float)ActualHeight / Zoom * .8f); e.Handled = true; return; }
        var block = Project.Blocks.FirstOrDefault(b => b.Id == BlockId); if (block is null) return;
        var order = new List<string>();
        foreach (var n in block.Networks) { order.Add(n.Id); if (!_collapsed.Contains(n.Id)) { order.AddRange(n.Branches.SelectMany(b => b).Select(i => i.Id)); order.Add(n.Output.Id); } }
        if (order.Count == 0) return;
        int index = Math.Max(0, order.IndexOf(Selection ?? "")), next = index;
        if (e.Key is VirtualKey.Down or VirtualKey.Right) next++;
        else if (e.Key is VirtualKey.Up or VirtualKey.Left) next--;
        else if (e.Key == VirtualKey.Home) next = 0; else if (e.Key == VirtualKey.End) next = order.Count - 1; else return;
        string id = order[Math.Clamp(next, 0, order.Count - 1)]; Reveal(id); Selected?.Invoke(id); e.Handled = true;
    }
    public void Dispose() { _disposed = true; _renderer.Dispose(); }
}
