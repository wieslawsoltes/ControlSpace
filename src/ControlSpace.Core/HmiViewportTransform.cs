namespace ControlSpace.Core;

/// <summary>Shared screen-space transform for rendering, hit testing and pointer authoring.</summary>
public readonly record struct HmiViewportTransform(double Scale, double X, double Y)
{
    public PointD ToScreen(PointD p) => new((p.X - X) / Scale, (p.Y - Y) / Scale);
    public PointD ToView(PointD p) => new(X + p.X * Scale, Y + p.Y * Scale);
    public RectD ToView(RectD b) => new(X + b.X * Scale, Y + b.Y * Scale, b.Width * Scale, b.Height * Scale);
    public static HmiViewportTransform Create(HmiScreen screen, double width, double height, bool fit = true, double zoom = 1, double scrollX = 0, double scrollY = 0)
    {
        const double inset = 28;
        width = double.IsFinite(width) ? Math.Max(1, width) : 1; height = double.IsFinite(height) ? Math.Max(1, height) : 1;
        double scale = fit ? Math.Max(.001, Math.Min(Math.Max(1, width - inset * 2) / screen.Width, Math.Max(1, height - inset * 2) / screen.Height)) : double.IsFinite(zoom) ? Math.Clamp(zoom, .1, 4) : 1;
        double x = fit ? Math.Max(inset, (width - screen.Width * scale) / 2) : inset - (double.IsFinite(scrollX) ? scrollX : 0) * scale;
        double y = fit ? Math.Max(inset, (height - screen.Height * scale) / 2) : inset - (double.IsFinite(scrollY) ? scrollY : 0) * scale;
        return new(scale, x, y);
    }
}
