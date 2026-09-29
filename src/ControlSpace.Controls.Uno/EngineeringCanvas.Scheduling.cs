using Microsoft.UI.Dispatching;
namespace ControlSpace.Controls.Uno;

public sealed partial class EngineeringCanvas
{
    private bool _renderQueued;
    public long RenderRequests { get; private set; }
    public long RenderSubmissions { get; private set; }
    public long PaintCount { get; private set; }
    public double LastPaintMilliseconds { get; private set; }
    public long TextRunCreations => _renderer.TextRunCreations;
    public int LastDrawnInstructions => _renderer.LastDrawnInstructions;

    /// <summary>Coalesce invalidations within the current UI turn. Uno owns presentation
    /// and caches the drawn scene; no application frame timer or second surface is used.
    /// Must be called on the owning UI thread.</summary>
    public new void Invalidate() => RequestRender();

    public void RequestRender()
    {
        if (_disposed) return;
        RenderRequests++;
        if (_renderQueued || !IsLoaded) return;
        _renderQueued = true;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            _renderQueued = false;
            if (_disposed || !IsLoaded) return;
            RenderSubmissions++;
            base.Invalidate();
        })) _renderQueued = false;
    }
}
