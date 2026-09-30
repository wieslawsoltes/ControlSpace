namespace ControlSpace.Core;

/// <summary>Shared basic-shape geometry. Coordinates are in screen units, not view pixels.</summary>
public static class HmiShapeGeometry
{
    public static bool IsShape(HmiKind kind) => kind is HmiKind.Rectangle or HmiKind.Ellipse or HmiKind.Line;

    // The positive-size model describes a top-left to bottom-right segment. Inset
    // endpoints keep the complete two-pixel stroke inside even subpixel bounds.
    public static (PointD Start, PointD End, double StrokeWidth) Line(HmiObject item)
    {
        double stroke = Math.Min(2, Math.Min(item.Width, item.Height));
        double inset = stroke / 2;
        return (new(item.X + inset, item.Y + inset),
            new(item.X + item.Width - inset, item.Y + item.Height - inset), stroke);
    }

    /// <summary>Hit the filled region or line stroke. Only a line gains extra pointer
    /// tolerance, so transparent ellipse corners can select objects beneath them.</summary>
    public static bool Contains(HmiObject item, PointD point, double lineTolerance = 0)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
            !double.IsFinite(item.X) || !double.IsFinite(item.Y) ||
            !double.IsFinite(item.Width) || !double.IsFinite(item.Height) ||
            item.Width <= 0 || item.Height <= 0 || !Enum.IsDefined(item.Kind)) return false;
        double x = point.X - item.X, y = point.Y - item.Y;
        if (item.Kind != HmiKind.Line)
        {
            if (x < 0 || y < 0 || x > item.Width || y > item.Height) return false;
            if (item.Kind != HmiKind.Ellipse) return true;
            double nx = 2 * (x / item.Width) - 1, ny = 2 * (y / item.Height) - 1;
            return nx * nx + ny * ny <= 1 + 1e-12;
        }
        lineTolerance = double.IsFinite(lineTolerance) ? Math.Clamp(lineTolerance, 0, 100) : 0;
        var (start, end, stroke) = Line(item);
        double tolerance = Math.Max(stroke / 2, lineTolerance);
        if (x < -tolerance || y < -tolerance || x > item.Width + tolerance || y > item.Height + tolerance) return false;
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double length = dx * dx + dy * dy;
        double t = length <= 0 ? 0 : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / length, 0, 1);
        double distanceX = point.X - start.X - t * dx, distanceY = point.Y - start.Y - t * dy;
        return distanceX * distanceX + distanceY * distanceY <= tolerance * tolerance + 1e-12;
    }
}
