using SkiaSharp;
namespace ControlSpace.Rendering.Skia;

public sealed partial class EngineeringRenderer
{
    private SKPoint[] _gridPoints = [];
    private (float Width, float Height) _gridSize;
    /// <summary>One native point batch, cached until the design surface changes size.</summary>
    private void DrawGrid(SKCanvas canvas, float width, float height, SKColor color, float radius)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return;
        if (_gridSize != (width, height))
        {
            _gridSize = (width, height);
            // Keep even a malformed/direct renderer input bounded. Valid projects use
            // much smaller surfaces and retain the ordinary 20-unit design grid.
            float step = Math.Max(20, MathF.Sqrt(width * height / 60000));
            int columns = Math.Clamp((int)MathF.Ceiling(width / step), 1, 60000);
            int rows = Math.Clamp((int)MathF.Ceiling(height / step), 1, Math.Max(1, 65536 / columns));
            _gridPoints = new SKPoint[columns * rows];
            for (int row = 0; row < rows; row++) for (int column = 0; column < columns; column++)
                _gridPoints[row * columns + column] = new(column * step, row * step);
        }
        Color(color, true, radius * 2);
        _paint.StrokeCap = SKStrokeCap.Round;
        canvas.DrawPoints(SKPointMode.Points, _gridPoints, _paint);
        _paint.StrokeCap = SKStrokeCap.Butt;
    }
}
