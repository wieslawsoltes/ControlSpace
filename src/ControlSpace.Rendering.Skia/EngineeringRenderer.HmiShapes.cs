using ControlSpace.Core;
using SkiaSharp;
namespace ControlSpace.Rendering.Skia;

public sealed partial class EngineeringRenderer
{
    // Shared by the public Hmi/HmiView API and the Uno HmiDesigner surface.
    private void DrawHmiShape(SKCanvas canvas, HmiObject item, SKColor color)
    {
        canvas.Save();
        try
        {
            var rect = new SKRect((float)item.X, (float)item.Y, (float)(item.X + item.Width), (float)(item.Y + item.Height));
            canvas.ClipRect(rect);
            Color(color);
            if (item.Kind == HmiKind.Rectangle) canvas.DrawRect(rect, _paint);
            else if (item.Kind == HmiKind.Ellipse) canvas.DrawOval(rect, _paint);
            else if (item.Kind == HmiKind.Line)
            {
                var (start, end, width) = HmiShapeGeometry.Line(item);
                Color(color, true, (float)width); _paint.StrokeCap = SKStrokeCap.Round;
                if (start == end) canvas.DrawPoint((float)start.X, (float)start.Y, _paint);
                else canvas.DrawLine((float)start.X, (float)start.Y, (float)end.X, (float)end.Y, _paint);
            }
        }
        finally { _paint.StrokeCap = SKStrokeCap.Butt; canvas.Restore(); }
    }
}
