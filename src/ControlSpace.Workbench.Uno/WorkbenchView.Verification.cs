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
                        controls.Add(new { id, x = point.X, y = point.Y, width = element.ActualWidth, height = element.ActualHeight });
                    }
                }
                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++) Visit(VisualTreeHelper.GetChild(node, i));
            }
            Visit(this);
            if (_programDialog is not null) Visit(_programDialog);
            bool canvasVisible = _editor.Content == _graphics && _body.Visibility == Visibility.Visible;
            var canvasOrigin = canvasVisible ? _canvas.TransformToVisual(this).TransformPoint(new Point(0, 0)) : new Point(0, 0);
            var ladderHits = (canvasVisible ? _canvas.HitRegions : Array.Empty<ControlSpace.Rendering.Skia.HitRegion>()).Select(h => new { id = h.Id, kind = h.Kind, x = canvasOrigin.X + (h.Bounds.X - _canvas.HorizontalOffset) * _canvas.Zoom, y = canvasOrigin.Y + (h.Bounds.Y - _canvas.ScrollOffset) * _canvas.Zoom, width = h.Bounds.Width * _canvas.Zoom, height = h.Bounds.Height * _canvas.Zoom }).ToArray();
            var json = JsonSerializer.Serialize(new { controls, ladderHits, revision = _workspace.Project.Revision,
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
