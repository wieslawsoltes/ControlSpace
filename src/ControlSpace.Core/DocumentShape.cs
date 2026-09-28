namespace ControlSpace.Core;

/// <summary>Resource and shape validation at persistence/compiler trust boundaries; not program semantic validation.</summary>
public static class DocumentShape
{
    public static void Validate(ProjectDocument project)
    {
        static void Check(bool value, string message) { if (!value) throw new ArgumentException(message); }
        static bool Text(string? value, int maximum = 4096) => value is not null && value.Length <= maximum;
        static bool Geometry(double x, double y, double w, double h) => double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(w) && double.IsFinite(h) && Math.Abs(x) <= 100000 && Math.Abs(y) <= 100000 && w is >= 1 and <= 100000 && h is >= 1 and <= 100000;
        Check(project.FormatVersion == 1, "Unsupported ControlSpace document version.");
        Check(Text(project.Name, 256) && Text(project.Author, 256) && Text(project.Comment), "Invalid project metadata.");
        Check(project.Tags is { Length: <= DocumentLimits.MaxTags } && project.Blocks is { Length: <= DocumentLimits.MaxBlocks } && project.Devices is { Length: <= 1000 } && project.Screens is { Length: <= 256 } && project.Connections is { Length: <= 10000 } && project.Alarms is { Length: <= 10000 }, "Document collections are missing or exceed resource limits.");
        var ids = new HashSet<Guid>();
        void Id(Guid id) => Check(id != Guid.Empty && ids.Add(id), "Document identifiers must be non-empty and globally unique.");
        var networkCount = 0; var contactCount = 0;
        foreach (var tag in project.Tags!) Check(tag is not null && Text(tag.Name, 128) && Text(tag.Address, 32) && Text(tag.Comment) && Enum.IsDefined(tag.Type) && double.IsFinite(tag.InitialValue), "Invalid tag definition.");
        foreach (var block in project.Blocks!)
        {
            Check(block is not null && Text(block.Name, 256) && Text(block.Comment) && Text(block.Source, DocumentLimits.MaxSourceLength) && Enum.IsDefined(block.Language) && block.Networks is not null, "Invalid program block."); Id(block!.Id);
            foreach (var network in block.Networks)
            {
                Check(++networkCount <= DocumentLimits.MaxNetworks, "Too many networks.");
                Check(network is not null && Text(network.Title, 256) && Text(network.Comment) && network.Paths is { Length: >= 1 and <= 64 } && network.Action is not null, "Invalid ladder network."); Id(network!.Id);
                foreach (var path in network.Paths)
                {
                    Check(path is { Length: <= 256 }, "Invalid ladder path.");
                    foreach (var contact in path!)
                    {
                        Check(++contactCount <= DocumentLimits.MaxContacts, "Too many contacts.");
                        Check(contact is not null && Enum.IsDefined(contact.Kind) && Text(contact.Operand, 128) && Text(contact.CompareTo, 128), "Invalid contact."); Id(contact!.Id);
                    }
                }
                var a = network.Action;
                Check(Enum.IsDefined(a.Kind) && Text(a.Target, 128) && Text(a.InputA, 128) && Text(a.InputB, 128) && Text(a.ResetTag, 128) && Text(a.AuxiliaryTag, 128) && double.IsFinite(a.Preset), "Invalid ladder instruction.");
            }
        }
        foreach (var device in project.Devices!)
        {
            Check(device is not null && Text(device.Name, 256) && Text(device.Model, 256) && Text(device.IpAddress, 32) && Enum.IsDefined(device.Kind) && Geometry(device.X, device.Y, 1, 1) && device.Modules is { Length: <= 64 }, "Invalid device."); Id(device!.Id);
            foreach (var module in device.Modules) { Check(module is not null && Text(module.Name, 256) && Enum.IsDefined(module.Kind) && module.Slot is >= 1 and <= 64 && module.Channels is >= 1 and <= 1024, "Invalid module."); Id(module!.Id); }
        }
        foreach (var screen in project.Screens!)
        {
            Check(screen is not null && Text(screen.Name, 256) && Geometry(0, 0, screen.Width, screen.Height) && screen.Objects is { Length: <= 10000 }, "Invalid HMI screen."); Id(screen!.Id);
            foreach (var item in screen.Objects)
            {
                Check(item is not null && Text(item.Name, 256) && Text(item.Text) && Text(item.Tag, 128) && Text(item.Fill, 16) && Enum.IsDefined(item.Kind) && Geometry(item.X, item.Y, item.Width, item.Height) && double.IsFinite(item.Maximum) && item.Maximum > 0, "Invalid HMI object."); Id(item!.Id);
            }
        }
        foreach (var link in project.Connections!) Check(link is not null && Text(link.Name, 256), "Invalid network connection.");
        foreach (var alarm in project.Alarms!) { Check(alarm is not null && Text(alarm.Tag, 128) && Text(alarm.Message) && Enum.IsDefined(alarm.Severity), "Invalid alarm."); Id(alarm!.Id); }
    }
}
