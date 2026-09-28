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
}
