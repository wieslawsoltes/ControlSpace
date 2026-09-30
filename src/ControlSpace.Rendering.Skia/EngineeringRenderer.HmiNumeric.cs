using ControlSpace.Core;
using SkiaSharp;
namespace ControlSpace.Rendering.Skia;

public sealed partial class EngineeringRenderer
{
    /// <summary>Shared configured I/O appearance used by public rendering and Uno design/runtime.</summary>
    private void DrawConfiguredHmiNumeric(SKCanvas canvas, HmiObject o, double value, SKColor accent, bool runtime)
    {
        var r = o.Runtime!;
        float x = (float)o.X, y = (float)o.Y, w = (float)o.Width, h = (float)o.Height;
        bool input = HmiRuntimeRules.IsNumericInput(o);
        bool outside = !double.IsFinite(value) || value < r.Minimum || value > r.Maximum;
        var border = outside && runtime ? SKColor.Parse("#B03D32") : input ? SKColor.Parse("#397FC0") : SKColor.Parse("#CAD4DD");
        var background = outside && runtime ? SKColor.Parse("#FFF0EB") : SKColors.White;
        canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(x, y, x + w, y + h));
            Box(canvas, x, y, w, h, background);
            if (o.Kind == HmiKind.Tank)
            {
                float filled = (float)HmiRuntimeRules.Fraction(o, value) * h;
                Box(canvas, x + 1, y + h - filled, Math.Max(0, w - 2), filled, accent.WithAlpha(150));
            }
            Color(border, true, input ? 1.5f : 1);
            canvas.DrawRect(x + .75f, y + .75f, Math.Max(0, w - 1.5f), Math.Max(0, h - 1.5f), _paint);
            float margin = Math.Min(10, w / 6), available = Math.Max(1, w - margin * 2);
            CenterText(canvas, o.Text, x + w / 2, y + Math.Min(23, h * .23f), available, Teal, Math.Clamp(h * .12f, 6, 12), true);
            string text = HmiRuntimeRules.Format(o, value, runtime);
            float textSize = runtime && r.IoMode == HmiIoMode.Input ? 15 : Math.Clamp(h * .32f, 6, 42);
            CenterText(canvas, text, x + w / 2, y + h * .65f, available, Ink, textSize, true);
            if (o.Kind == HmiKind.Gauge && w > 20 && h > 20)
            {
                Box(canvas, x + 8, y + h - 15, w - 16, 5, SKColor.Parse("#E1E9EE"));
                Box(canvas, x + 8, y + h - 15, (w - 16) * (float)HmiRuntimeRules.Fraction(o, value), 5, accent);
            }
            else if (input && h >= 48 && w >= 80)
            {
                string mode = r.IoMode == HmiIoMode.Input ? "INPUT" : "I/O";
                Text(canvas, mode, x + margin, y + h - 6, border, 9);
                // Small vector entry mark rather than a platform-dependent glyph.
                Line(canvas, x + w - 19, y + h - 7, x + w - 9, y + h - 17, border, 1.3f);
                Line(canvas, x + w - 20, y + h - 6, x + w - 15, y + h - 7, border, 1.3f);
            }
        }
        finally { canvas.Restore(); }
    }
}
