using ControlSpace.Core;
namespace ControlSpace.Engineering;

/// <summary>Pure, cancellable gesture preview. The host commits the returned boxes once, using its captured project snapshot.</summary>
public sealed class HmiTransformSession
{
    public HmiScreen Screen { get; }
    public RectD OriginalBounds { get; }
    public string Handle { get; }
    private readonly HmiObject[] _objects;
    private readonly HashSet<string> _ids;
    private readonly PointD _start;
    public double? GuideX { get; private set; }
    public double? GuideY { get; private set; }
    public HmiTransformSession(HmiScreen screen, IEnumerable<string> selection, PointD start, string handle = "move")
    {
        Screen = screen; _ids = selection.ToHashSet(StringComparer.Ordinal); _objects = screen.Objects.Where(o => _ids.Contains(o.Id)).ToArray();
        if (_objects.Length == 0 || _objects.Length != _ids.Count) throw new ArgumentException("Select objects in this screen.");
        if (handle is not ("move" or "n" or "s" or "w" or "e" or "nw" or "ne" or "sw" or "se")) throw new ArgumentException("Invalid resize handle.");
        if (!double.IsFinite(start.X) || !double.IsFinite(start.Y)) throw new ArgumentException("Invalid pointer position.");
        OriginalBounds = HmiEditor.Bounds(_objects); Handle = handle; _start = start;
    }
    public IReadOnlyDictionary<string, RectD> Preview(PointD point, bool snap = true, double grid = 10, double tolerance = 5)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new ArgumentException("Invalid pointer position.");
        grid = double.IsFinite(grid) ? Math.Clamp(grid, 1, 100) : 10; tolerance = double.IsFinite(tolerance) ? Math.Clamp(tolerance, 0, 100) : 5;
        GuideX = GuideY = null;
        var b = OriginalBounds; double dx = point.X - _start.X, dy = point.Y - _start.Y;
        double Round(double value) => snap ? Math.Round(value / grid, MidpointRounding.AwayFromZero) * grid : value;
        if (Handle == "move")
        {
            double x = Round(b.X + dx), y = Round(b.Y + dy);
            if (snap)
            {
                double bestX = tolerance + 1, bestY = tolerance + 1; double rawX = b.X + dx, rawY = b.Y + dy;
                void Compare(double target, double position, double size, bool horizontal)
                {
                    for (int anchor = 0; anchor < 3; anchor++)
                    {
                        double offset = anchor * size / 2;
                        double difference = target - position - offset, distance = Math.Abs(difference);
                        if (distance > tolerance) continue;
                        if (horizontal && distance < bestX) { bestX = distance; x = rawX + difference; GuideX = target; }
                        if (!horizontal && distance < bestY) { bestY = distance; y = rawY + difference; GuideY = target; }
                    }
                }
                Compare(Screen.Width / 2, rawX, b.Width, true); Compare(Screen.Height / 2, rawY, b.Height, false);
                foreach (var o in Screen.Objects)
                {
                    if (_ids.Contains(o.Id)) continue;
                    Compare(o.X, rawX, b.Width, true); Compare(o.X + o.Width / 2, rawX, b.Width, true); Compare(o.X + o.Width, rawX, b.Width, true);
                    Compare(o.Y, rawY, b.Height, false); Compare(o.Y + o.Height / 2, rawY, b.Height, false); Compare(o.Y + o.Height, rawY, b.Height, false);
                }
            }
            double clampedX = Math.Clamp(x, 0, Screen.Width - b.Width), clampedY = Math.Clamp(y, 0, Screen.Height - b.Height);
            if (clampedX != x) GuideX = null; if (clampedY != y) GuideY = null;
            dx = clampedX - b.X; dy = clampedY - b.Y;
            return _objects.ToDictionary(o => o.Id, o => new RectD(o.X + dx, o.Y + dy, o.Width, o.Height));
        }
        double minWidth = _objects.Max(o => b.Width / o.Width * Math.Min(1, o.Width)), minHeight = _objects.Max(o => b.Height / o.Height * Math.Min(1, o.Height));
        double left = b.X, top = b.Y, right = b.X + b.Width, bottom = b.Y + b.Height;
        if (Handle.Contains('w')) left = Math.Clamp(Round(b.X + dx), 0, right - minWidth);
        if (Handle.Contains('e')) right = Math.Clamp(Round(right + dx), left + minWidth, Screen.Width);
        if (Handle.Contains('n')) top = Math.Clamp(Round(b.Y + dy), 0, bottom - minHeight);
        if (Handle.Contains('s')) bottom = Math.Clamp(Round(bottom + dy), top + minHeight, Screen.Height);
        double sx = (right - left) / b.Width, sy = (bottom - top) / b.Height;
        return _objects.ToDictionary(o => o.Id, o => new RectD(left + (o.X - b.X) * sx, top + (o.Y - b.Y) * sy, Math.Max(Math.Min(1, o.Width), o.Width * sx), Math.Max(Math.Min(1, o.Height), o.Height * sy)));
    }
}
