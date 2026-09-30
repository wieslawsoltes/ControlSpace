#if __WASM__
using System.Text.Json;
using System.Runtime.InteropServices.JavaScript;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
namespace ControlSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private DispatcherTimer? _verificationTimer;
    /// <summary>Opt-in, read-only UI telemetry for pointer-driven browser tests. Never enabled by default.</summary>
    public void StartVerificationProbe()
    {
        if (_verificationTimer is not null) return;
        _verificationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _verificationTimer.Tick += (_, _) =>
        {
            var controls = new List<object>();
            void Visit(DependencyObject node)
            {
                if (node is FrameworkElement element)
                {
                    if (element.Visibility != Visibility.Visible) return;
                    string id = AutomationProperties.GetAutomationId(element);
                    if (!string.IsNullOrEmpty(id) && element.ActualWidth > 0 && element.ActualHeight > 0)
                    {
                        var point = element.TransformToVisual(this).TransformPoint(new Point(0, 0));
                        controls.Add(new { id, x = point.X, y = point.Y, width = element.ActualWidth, height = element.ActualHeight, text = element is Microsoft.UI.Xaml.Controls.TextBox field ? field.Text : element is Microsoft.UI.Xaml.Controls.AutoSuggestBox suggest ? suggest.Text : null });
                    }
                }
                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++) Visit(VisualTreeHelper.GetChild(node, i));
            }
            Visit(this);
            if (_programDialog is not null) Visit(_programDialog);
            bool canvasVisible = _editor.Content == _graphics && _body.Visibility == Visibility.Visible;
            var canvasOrigin = canvasVisible ? _canvas.TransformToVisual(this).TransformPoint(new Point(0, 0)) : new Point(0, 0);
            var ladderHits = (canvasVisible && _canvas.Mode == ControlSpace.Controls.Uno.EditorMode.Ladder ? _canvas.HitRegions : Array.Empty<ControlSpace.Rendering.Skia.HitRegion>()).Select(h => new { id = h.Id, kind = h.Kind, x = canvasOrigin.X + (h.Bounds.X - _canvas.HorizontalOffset) * _canvas.Zoom, y = canvasOrigin.Y + (h.Bounds.Y - _canvas.ScrollOffset) * _canvas.Zoom, width = h.Bounds.Width * _canvas.Zoom, height = h.Bounds.Height * _canvas.Zoom }).ToArray();
            var hmiHits = (canvasVisible && _canvas.Mode == ControlSpace.Controls.Uno.EditorMode.Hmi ? _canvas.HitRegions : Array.Empty<ControlSpace.Rendering.Skia.HitRegion>()).Select(h => new { id = h.Id, x = canvasOrigin.X + h.Bounds.X, y = canvasOrigin.Y + h.Bounds.Y, width = h.Bounds.Width, height = h.Bounds.Height }).ToArray();
            var hmiHandles = _canvas.HmiHandles().Select(h => new { handle = h.Handle, x = canvasOrigin.X + h.Bounds.X, y = canvasOrigin.Y + h.Bounds.Y, width = h.Bounds.Width, height = h.Bounds.Height }).ToArray();
            var hmiTransform = _canvas.HmiTransform;
            var json = JsonSerializer.Serialize(new { hmiHits, hmiHandles, hmiSelection = _canvas.HmiSelection.ToArray(), hmiRuntime = _hmiRuntime,
                hmiObjects = _workspace.Project.Screens.FirstOrDefault(s => "hmi:" + s.Id == _view)?.Objects.Take(200).ToArray(),
                hmiScreens = _workspace.Project.Screens.Take(100).Select(s => new { s.Id, s.Name, s.Width, s.Height, count = s.Objects.Count }).ToArray(),
                hmiView = new { x = canvasOrigin.X + hmiTransform.X, y = canvasOrigin.Y + hmiTransform.Y, scale = hmiTransform.Scale, fit = _canvas.HmiFit, grid = _canvas.HmiGrid, snap = _canvas.HmiSnap, preview = _canvas.HmiPreviewBoxes, gesture = _canvas.HmiGestureActive, drawnObjects = _canvas.HmiDrawnObjects }, navigation = new { rows = _tree.RealizedRowCount, creations = _tree.RowCreations, visible = _tree.VisibleEntryCount, selected = _tree.SelectedId, focused = _tree.FocusedId, selectedIndex = _tree.SelectedVisibleIndex, offset = _tree.VerticalOffset, viewport = _tree.ViewportHeight, filter = _tree.Filter, indexBuilds = _tree.IndexBuilds, projectionBuilds = _tree.ProjectionBuilds }, performance = new {
                paintCount = _canvas.PaintCount, renderRequests = _canvas.RenderRequests, renderSubmissions = _canvas.RenderSubmissions,
                lastPaintMilliseconds = _canvas.LastPaintMilliseconds, snapshotCopies = _workspace.Controller?.SnapshotCopies ?? 0,
                visualVersion = _workspace.Controller?.VisualVersion ?? 0, recoverySerializations = RecoverySerializations,
                editorBuilds = EditorBuilds, paletteBuilds = PaletteBuilds, toolbarBuilds = ToolbarBuilds,
                tabCreations = _editorBar.TabCreations, textRuns = _canvas.TextRunCreations,
                drawnInstructions = _canvas.LastDrawnInstructions }, controls, ladderHits, revision = _workspace.Project.Revision,
                programBlocks = _workspace.Project.Blocks.Select(b => new { b.Id, b.Name, b.Number, language = b.Language.ToString(), b.Cyclic, networks = b.Networks.Count }).ToArray(),
                ladderNetworks = _workspace.Project.Blocks.FirstOrDefault(b => "block:" + b.Id == _view)?.Networks.Take(100).ToArray(),
                ladderSelection = _selection, ladderCollapsed = _canvas.CollapsedNetworks.ToArray(), ladderWidth = _canvas.ContentWidth,
                ladderScroll = _canvas.ScrollOffset, ladderHorizontal = _canvas.HorizontalOffset, ladderFont = _canvas.RendererFont,
                paletteFilter = _paletteFilter, palettePointer = ControlSpace.Controls.Uno.PaletteDragSource.LastPointerEvent,
                instructionDrag = _instructionDragState, instructionDropTarget = _canvas.InstructionDropTarget, dialogOpen = _programDialog is not null, dialogError = _programErrorText,
                view = _view, documents = _documents.Documents, maximized = _maximized, layout = _layout, projectDrawer = _projectDrawer, taskDrawer = _taskDrawer, source = _source?.Text, blockSources = _workspace.Project.Blocks.ToDictionary(b => b.Id, b => b.Source), tagCount = _workspace.Project.Tags.Count, tags = _workspace.Project.Tags.Take(100).ToArray(), tableRows = _table.RealizedRowCount, tableSelection = _table.ActiveTagName, tableStatus = _table.StatusText, values = _workspace.Project.Tags.Take(100).ToDictionary(t => t.Name, t => _workspace.Controller?.Read(t.Name) ?? t.InitialValue), state = _workspace.Controller?.State.ToString(), cycle = _workspace.Controller?.Cycle ?? 0, zoom = _canvas.Zoom, portal = _portal.Visibility == Visibility.Visible });
            JSHost.GlobalThis.SetProperty("controlSpaceVerificationJson", json);
        };
        _verificationTimer.Start();
    }
}
#endif
