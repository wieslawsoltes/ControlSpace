using ControlSpace.Core;
using ControlSpace.Simulation;
using SkiaSharp;
namespace ControlSpace.Rendering.Skia;

public sealed partial class EngineeringRenderer
{
    public int LastHmiDrawnObjects { get; private set; }
    /// <summary>Viewport-sized HMI design surface: screen clipping, offscreen culling, rulers and ordered selection overlays.</summary>
    public RenderResult HmiDesigner(SKCanvas canvas, float width, float height, HmiScreen screen, IReadOnlyList<PlcTag> tags,
        IScanReadView? values, HmiViewportTransform transform, IReadOnlyList<string> selection, bool runtime = false, bool grid = true,
        RectD? marquee = null, double? guideX = null, double? guideY = null)
    {
        CacheTags(tags); LastHmiDrawnObjects = 0;
        canvas.Clear(SKColor.Parse("#E4E5E8")); var hits = new List<HitRegion>();
        float scale = (float)transform.Scale, ox = (float)transform.X, oy = (float)transform.Y;
        canvas.Save(); canvas.ClipRect(new SKRect(22, 22, width, height));
        canvas.Translate(ox, oy); canvas.Scale(scale);
        Box(canvas, 0, 0, (float)screen.Width, (float)screen.Height, SKColor.Parse("#F7FAFC"), SKColor.Parse("#8B96A3"));
        canvas.Save(); canvas.ClipRect(new SKRect(0, 0, (float)screen.Width, (float)screen.Height));
        if (grid && !runtime) DrawGrid(canvas, (float)screen.Width, (float)screen.Height, SKColor.Parse("#CCD2D9"), .6f / scale);
        var selected = selection.ToHashSet(StringComparer.Ordinal);
        foreach (var o in screen.Objects)
        {
            var bounds = transform.ToView(new RectD(o.X, o.Y, o.Width, o.Height));
            if (bounds.X + bounds.Width < 22 || bounds.Y + bounds.Height < 22 || bounds.X > width || bounds.Y > height) continue;
            LastHmiDrawnObjects++; hits.Add(new(o.Id, "hmi", bounds));
            float x = (float)o.X, y = (float)o.Y, w = (float)o.Width, h = (float)o.Height;
            var color = SKColor.TryParse(o.Color, out var parsed) ? parsed : Teal;
            int slot = _slotLookup.GetValueOrDefault(o.Tag, -1);
            double value = slot < 0 ? 0 : values is not null && slot < values.ValueCount ? values.ReadValue(slot) : tags[slot].InitialValue;
            canvas.Save(); canvas.ClipRect(new SKRect(x, y, x + w, y + h));
            if (o.Runtime is not null && HmiRuntimeRules.IsNumeric(o.Kind)) DrawConfiguredHmiNumeric(canvas, o, value, color, runtime);
            else switch (o.Kind)
            {
                case HmiKind.Rectangle:
                case HmiKind.Ellipse:
                case HmiKind.Line:
                    DrawHmiShape(canvas, o, color); break;
                case HmiKind.Label:
                    string[] lines = (o.Text.Length > 2048 ? o.Text[..2048] : o.Text).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                    float size = Math.Clamp(h / Math.Max(1, Math.Min(lines.Length, 20)) * .62f, 6, 26);
                    for (int i = 0; i < Math.Min(lines.Length, 20) && (i + 1) * size * 1.3f <= h + 1; i++)
                    { var run = Run(lines[i], size, h > 35); PaintRun(canvas, run, x, y + size + i * size * 1.3f, color, size, h > 35); }
                    break;
                case HmiKind.Button:
                    Box(canvas, x, y, w, h, value != 0 ? Active : color, SKColor.Parse("#60717D"));
                    Line(canvas, x + 1, y + 1, x + w - 1, y + 1, SKColors.White.WithAlpha(90));
                    CenterText(canvas, o.Text, x + w / 2, y + h / 2 + Math.Min(6, h / 5), Math.Max(1, w - 12), SKColors.White, Math.Clamp(h * .28f, 6, 17), true); break;
                case HmiKind.Lamp:
                    Box(canvas, x, y, w, h, SKColors.White, SKColor.Parse("#CAD4DD"));
                    Color(value != 0 ? Active : SKColor.Parse("#A7B5BD")); canvas.DrawCircle(x + Math.Min(23, w / 5), y + h / 2, Math.Min(7, Math.Min(w / 8, h / 5)), _paint);
                    float labelX = x + Math.Min(41, w * .32f);
                    Text(canvas, o.Text, labelX, y + h * .43f, size: Math.Clamp(h * .16f, 6, 12), bold: true);
                    Text(canvas, value != 0 ? "Active" : "Inactive", labelX, y + h * .7f, Teal, Math.Clamp(h * .16f, 6, 12)); break;
                case HmiKind.Numeric:
                case HmiKind.Gauge:
                    Box(canvas, x, y, w, h, SKColors.White, SKColor.Parse("#CAD4DD"));
                    Text(canvas, o.Text, x + 12, y + Math.Min(23, h * .23f), Teal, Math.Clamp(h * .12f, 6, 12), true);
                    CenterText(canvas, value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + (o.Kind == HmiKind.Gauge ? " %" : ""), x + w / 2, y + h * .66f, Math.Max(1, w - 16), Ink, Math.Clamp(h * .38f, 6, 46), true);
                    if (o.Kind == HmiKind.Gauge && w > 16 && h > 16)
                    { Box(canvas, x + 8, y + h - 16, w - 16, 6, SKColor.Parse("#E1E9EE")); Box(canvas, x + 8, y + h - 16, (w - 16) * (float)Math.Clamp(value / 100, 0, 1), 6, color); }
                    break;
                case HmiKind.Tank:
                    Box(canvas, x, y, w, h, SKColor.Parse("#DDECF0"), color);
                    float fill = (float)Math.Clamp(value / 100, 0, 1) * h; Box(canvas, x + 1, y + h - fill, Math.Max(0, w - 2), fill, color);
                    CenterText(canvas, o.Text, x + w / 2, y + Math.Min(24, h / 2), Math.Max(1, w - 8), Ink, Math.Clamp(h * .1f, 6, 14)); break;
            }
            canvas.Restore();
            if (!runtime && selected.Contains(o.Id))
            {
                Color(SKColor.Parse("#397FC0"), true, 1 / scale); canvas.DrawRect(x, y, w, h, _paint);
            }
        }
        if (!runtime)
        {
            if (guideX is double gx) Line(canvas, (float)gx, 0, (float)gx, (float)screen.Height, SKColor.Parse("#157ED2"), 1 / scale);
            if (guideY is double gy) Line(canvas, 0, (float)gy, (float)screen.Width, (float)gy, SKColor.Parse("#157ED2"), 1 / scale);
            if (marquee is RectD box) { Color(SKColor.Parse("#397FC0").WithAlpha(28)); canvas.DrawRect((float)box.X, (float)box.Y, (float)box.Width, (float)box.Height, _paint); Color(SKColor.Parse("#397FC0"), true, 1 / scale); canvas.DrawRect((float)box.X, (float)box.Y, (float)box.Width, (float)box.Height, _paint); }
        }
        canvas.Restore(); canvas.Restore();
        if (!runtime)
        {
            // All handles are the same device-independent size at every zoom level.
            var chosen = screen.Objects.Where(o => selected.Contains(o.Id)).ToArray();
            if (chosen.Length > 0)
            {
                double left = chosen.Min(o => o.X), top = chosen.Min(o => o.Y), right = chosen.Max(o => o.X + o.Width), bottom = chosen.Max(o => o.Y + o.Height);
                var b = transform.ToView(new RectD(left, top, right - left, bottom - top));
                Color(SKColor.Parse("#397FC0"), true); canvas.DrawRect((float)b.X, (float)b.Y, (float)b.Width, (float)b.Height, _paint);
                foreach (var point in HandlePoints(b)) Box(canvas, (float)point.X - 3, (float)point.Y - 3, 6, 6, SKColors.White, SKColor.Parse("#397FC0"));
            }
        }
        Box(canvas, 0, 0, width, 22, SKColor.Parse("#F4F4F5")); Box(canvas, 0, 22, 22, Math.Max(0, height - 22), SKColor.Parse("#F4F4F5"));
        double step = 50; while (step * scale < 40) step *= 2;
        for (double x = Math.Max(0, Math.Ceiling((22 - ox) / scale / step) * step); x <= screen.Width && ox + x * scale < width; x += step)
        { float px = ox + (float)x * scale; Line(canvas, px, 16, px, 22, SKColor.Parse("#7C8795")); Text(canvas, x.ToString("0"), px + 3, 12, size: 9); }
        for (double y = Math.Max(0, Math.Ceiling((22 - oy) / scale / step) * step); y <= screen.Height && oy + y * scale < height; y += step)
        { float py = oy + (float)y * scale; Line(canvas, 16, py, 22, py, SKColor.Parse("#7C8795")); canvas.Save(); canvas.Translate(11, py + 3); canvas.RotateDegrees(-90); Text(canvas, y.ToString("0"), 0, 0, size: 9); canvas.Restore(); }
        Box(canvas, 0, 0, 22, 22, SKColor.Parse("#D5D8DF"));
        return new(hits, (float)screen.Height);
    }
    public static IEnumerable<PointD> HandlePoints(RectD b)
    {
        yield return new(b.X, b.Y); yield return new(b.X + b.Width / 2, b.Y); yield return new(b.X + b.Width, b.Y);
        yield return new(b.X + b.Width, b.Y + b.Height / 2); yield return new(b.X + b.Width, b.Y + b.Height);
        yield return new(b.X + b.Width / 2, b.Y + b.Height); yield return new(b.X, b.Y + b.Height); yield return new(b.X, b.Y + b.Height / 2);
    }
}
