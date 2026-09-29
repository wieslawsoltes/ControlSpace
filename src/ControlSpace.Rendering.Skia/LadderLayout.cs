using ControlSpace.Core;
namespace ControlSpace.Rendering.Skia;

public sealed record LadderNetworkLayout(string Id, int Index, double Y, double Height, bool Collapsed);
/// <summary>Reusable logical geometry for drawing, scrolling and hit testing. Collapse never changes program execution.</summary>
public sealed class LadderLayout
{
    public const double HeaderHeight = 26, Gap = 10, BranchSpacing = 66, ContactSpacing = 142;
    public IReadOnlyList<LadderNetworkLayout> Networks { get; }
    public double Width { get; }
    public double Height { get; }
    public LadderLayout(ProgramBlock block, double viewportWidth, IReadOnlySet<string>? collapsed = null)
    {
        int columns = block.Networks.SelectMany(n => n.Branches).Select(b => b.Count).DefaultIfEmpty(1).Max();
        Width = Math.Max(double.IsFinite(viewportWidth) ? viewportWidth : 580, 300 + columns * ContactSpacing);
        var rows = new List<LadderNetworkLayout>(block.Networks.Count); double y = 0;
        for (int i = 0; i < block.Networks.Count; i++)
        {
            var n = block.Networks[i]; bool fold = collapsed?.Contains(n.Id) == true;
            double h = fold ? HeaderHeight : Math.Max(178, 124 + BranchSpacing * n.Branches.Count);
            rows.Add(new(n.Id, i, y, h, fold)); y += h + Gap;
        }
        Networks = rows; Height = y;
    }
    public IEnumerable<LadderNetworkLayout> Visible(double offset, double height)
    {
        offset = double.IsFinite(offset) ? Math.Max(0, offset) : 0;
        height = double.IsFinite(height) ? Math.Max(0, height) : 0;
        // Binary-search the first intersecting network; keep scrolling independent of hidden network count.
        int lo = 0, hi = Networks.Count;
        while (lo < hi) { int mid = (lo + hi) / 2; if (Networks[mid].Y + Networks[mid].Height < offset) lo = mid + 1; else hi = mid; }
        for (int i = lo; i < Networks.Count && Networks[i].Y <= offset + height; i++) yield return Networks[i];
    }
    public static RectD Contact(int column, double lineY) => new(74 + column * ContactSpacing, lineY - 42, 124, 66);
}
