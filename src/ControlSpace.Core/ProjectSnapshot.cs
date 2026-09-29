namespace ControlSpace.Core;

/// <summary>Detaches all mutable collection containers while sharing immutable record leaves.</summary>
public static class ProjectSnapshot
{
    public static ControlProject Clone(ControlProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project with
        {
            Tags = [.. project.Tags],
            Blocks = project.Blocks.Select(block => block with
            {
                Networks = block.Networks.Select(network => network with
                {
                    Branches = network.Branches.Select(path => path.ToList()).ToList()
                }).ToList()
            }).ToList(),
            Devices = project.Devices.Select(device => device with { Modules = [.. device.Modules] }).ToList(),
            Links = [.. project.Links],
            Screens = project.Screens.Select(screen => screen with { Objects = [.. screen.Objects] }).ToList()
        };
    }
    /// <summary>Structural project equality without serialization or temporary collection copies.
    /// Includes revision, matching the persisted document's identity. Callers must mutate
    /// workspace documents through transactions so cached dirty state remains authoritative.</summary>
    public static bool ContentEquals(ControlProject? a, ControlProject? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Format != b.Format || a.Version != b.Version ||
            a.Id != b.Id || a.Name != b.Name || a.Revision != b.Revision) return false;
        return Same(a.Tags, b.Tags, static (x, y) => x == y) &&
            Same(a.Links, b.Links, static (x, y) => x == y) &&
            Same(a.Blocks, b.Blocks, BlockEquals) &&
            Same(a.Devices, b.Devices, static (x, y) => ReferenceEquals(x, y) || x is not null && y is not null &&
                x.Id == y.Id && x.Name == y.Name && x.Kind == y.Kind && x.Model == y.Model &&
                x.IpAddress == y.IpAddress && x.X.Equals(y.X) && x.Y.Equals(y.Y) && Same(x.Modules, y.Modules, static (m, n) => m == n)) &&
            Same(a.Screens, b.Screens, static (x, y) => ReferenceEquals(x, y) || x is not null && y is not null &&
                x.Id == y.Id && x.Name == y.Name && x.Width.Equals(y.Width) && x.Height.Equals(y.Height) &&
                Same(x.Objects, y.Objects, static (m, n) => m == n));
    }
    public static bool BlockEquals(ProgramBlock? a, ProgramBlock? b) => ReferenceEquals(a, b) ||
        a is not null && b is not null && a.Id == b.Id && a.Name == b.Name && a.Number == b.Number &&
        a.Language == b.Language && a.Cyclic == b.Cyclic && a.Source == b.Source && Same(a.Networks, b.Networks, NetworkEquals);
    private static bool NetworkEquals(LadderNetwork? a, LadderNetwork? b) => ReferenceEquals(a, b) ||
        a is not null && b is not null && a.Id == b.Id && a.Title == b.Title && a.Comment == b.Comment && a.Output == b.Output &&
        Same(a.Branches, b.Branches, static (x, y) => Same(x, y, static (m, n) => m == n));
    private static bool Same<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b, Func<T, T, bool> equal)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (!equal(a[i], b[i])) return false;
        return true;
    }
}
