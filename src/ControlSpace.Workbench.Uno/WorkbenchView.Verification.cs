#if __WASM__
using System.Text.Json;
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
            var json = JsonSerializer.Serialize(new { controls, view = _view, documents = _documents.Documents, maximized = _maximized, layout = _layout, projectDrawer = _projectDrawer, taskDrawer = _taskDrawer, source = _source?.Text, blockSources = _workspace.Project.Blocks.ToDictionary(b => b.Id, b => b.Source), tagCount = _workspace.Project.Tags.Count, state = _workspace.Controller?.State.ToString(), cycle = _workspace.Controller?.Cycle ?? 0, zoom = _canvas.Zoom, portal = _portal.Visibility == Visibility.Visible });
            Uno.Foundation.WebAssemblyRuntime.InvokeJS("window.controlSpaceVerification = " + json + ";");
        };
        _verificationTimer.Start();
    }
}
#endif
