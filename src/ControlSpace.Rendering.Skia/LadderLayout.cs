using ControlSpace.Core;
namespace ControlSpace.Rendering.Skia;

public sealed record LadderNetworkLayout(string Id, int Index, double Y, double Height, bool Collapsed)
{
    public string Caption { get; init; } = "";
    public string Comment { get; init; } = "";
}
/// <summary>Reusable logical geometry for drawing, scrolling and hit testing. Collapse never changes program execution.</summary>
public sealed class LadderLayout
{
    public const double HeaderHeight = 26, Gap = 6, BranchSpacing = 60, ContactSpacing = 142, FirstRungY = 94;
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
            double h = fold ? HeaderHeight : FirstRungY + Math.Max(70, 42 + BranchSpacing * (n.Branches.Count - 1));
            string Trim(string? text) => text is null ? "" : text.Length > 512 ? text[..511] + "…" : text;
            rows.Add(new(n.Id, i, y, h, fold) { Caption = $"Network {i + 1}:  {Trim(n.Title)}", Comment = Trim(n.Comment).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "  ·  ") }); y += h + Gap;
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
    /// <summary>Constant-time horizontal culling, including one preceding contact for correct wire continuity.</summary>
    public static (int First, int End) VisibleContacts(int count, double offset, double width)
    {
        if (count <= 0 || !double.IsFinite(offset) || !double.IsFinite(width) || width <= 0) return (0, 0);
        int first = (int)Math.Clamp(Math.Floor((offset - 198) / ContactSpacing), 0, count);
        int end = (int)Math.Clamp(Math.Ceiling((offset + width - 74) / ContactSpacing) + 1, first, count);
        return (first, end);
    }
    public static RectD Contact(int column, double lineY) => new(74 + column * ContactSpacing, lineY - 42, 124, 66);
}
