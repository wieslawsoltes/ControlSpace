using System.Net;
using System.Text.RegularExpressions;
namespace ControlSpace.Core;

public static partial class ProjectValidator
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)] private static partial Regex Identifier();
    [GeneratedRegex(@"^%([IQM])([BWD]?)([0-9]{1,6})(?:\.([0-7]))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex Address();
    public static List<Diagnostic> Validate(ControlProject p)
    {
        var d = new List<Diagnostic>();
        void Error(string code, string message, string location = "") => d.Add(new(Severity.Error, code, message, location));
        if (p is null) { Error("CS003", "Project is null."); return d; }
        if (p.Revision < 0) Error("CS008", "Project revision must be nonnegative.");
        if (p.Format != ControlProject.FormatId || p.Version != ControlProject.CurrentVersion) Error("CS001", "Unsupported project format/version.");
        if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 128) Error("CS002", "Project name is required and limited to 128 characters.");
        if (p.Tags is null || p.Blocks is null || p.Devices is null || p.Links is null || p.Screens is null) { Error("CS003", "Project collections cannot be null."); return d; }
        if (p.Tags.Count > 10000 || p.Blocks.Count > 1000 || p.Devices.Count > 2000 || p.Screens.Count > 1000 || p.Links.Count > 10000) { Error("CS004", "Project exceeds supported resource limits."); return d; }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var memory = new List<(string Area, int Start, int End, string Name)> (p.Tags.Count);
        foreach (var t in p.Tags)
        {
            if (t is null) { Error("CS005", "Null tag."); continue; }
            if (t.Name is null || !Identifier().IsMatch(t.Name) || !names.Add(t.Name)) Error("CS010", "Invalid or duplicate tag name.", t.Name ?? "");
            if (t.Comment is null) Error("CS009", "Tag comment cannot be null.", t.Name ?? "");
            if (!PlcValues.IsValid(t.Type, t.InitialValue)) Error("CS011", "Initial value is not valid for the declared type.", t.Name);
            var match = Address().Match(t.Address ?? "");
            if (!match.Success) { Error("CS012", "Use %I, %Q or %M byte/bit addresses (for example %I0.0, %MW10, %MD20).", t.Name); continue; }
            string prefix = match.Groups[1].Value.ToUpperInvariant(), width = match.Groups[2].Value.ToUpperInvariant();
            bool bit = match.Groups[4].Success;
            if (t.Type == PlcType.Bool ? (!bit || width.Length != 0) : bit || width != (t.Type == PlcType.Int ? "W" : "D")) Error("CS013", "Address width does not match tag type.", t.Name);
            int start = int.Parse(match.Groups[3].Value) * 8 + (bit ? int.Parse(match.Groups[4].Value) : 0);
            int bits = bit ? 1 : width == "W" ? 16 : 32;
            memory.Add((prefix, start, start + bits, t.Name ?? ""));
        }
        // Sort integer intervals rather than allocating up to 32 string-key entries per tag.
        memory.Sort(static (a, b) => { int area = string.CompareOrdinal(a.Area, b.Area); return area != 0 ? area : a.Start.CompareTo(b.Start); });
        string previousArea = "", previousName = ""; int end = -1;
        foreach (var address in memory)
        {
            if (address.Area != previousArea) { previousArea = address.Area; end = -1; }
            if (address.Start < end) Error("CS014", $"Address overlaps '{previousName}'.", address.Name);
            if (address.End > end) { end = address.End; previousName = address.Name; }
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Id(string? id) { if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) Error("CS020", "Object IDs must be nonempty and unique.", id ?? ""); }
        Id(p.Id);
        foreach (var b in p.Blocks)
        {
            if (b is null || b.Networks is null) { Error("CS021", "Invalid block."); continue; }
            Id(b.Id);
            if (string.IsNullOrWhiteSpace(b.Name) || b.Source is null || b.Source.Length > 1_000_000 || b.Networks.Count > 1000) { Error("CS022", "Block exceeds limits or is incomplete.", b.Id); continue; }
            if (!Enum.IsDefined(b.Language)) Error("CS023", "Unsupported block language.", b.Id);
            foreach (var n in b.Networks)
            {
                if (n is null || n.Branches is null || n.Output is null) { Error("CS024", "Incomplete network.", b.Id); continue; }
                Id(n.Id);
                if (n.Title is null || n.Comment is null) Error("CS024", "Network text cannot be null.", n.Id);
                void CheckInstruction(Instruction instruction)
                {
                    Id(instruction.Id);
                    if (!Enum.IsDefined(instruction.Kind) || instruction.Tag is null || instruction.Auxiliary is null || !double.IsFinite(instruction.Parameter)) Error("CS027", "Invalid instruction fields.", instruction.Id);
                }
                CheckInstruction(n.Output);
                if (n.Branches.Count is < 1 or > 16) Error("CS025", "A network must contain 1–16 parallel paths.", n.Id);
                foreach (var path in n.Branches)
                {
                    if (path is null || path.Count is < 1 or > 64) { Error("CS026", "Invalid or oversized path.", n.Id); continue; }
                    foreach (var instruction in path) { if (instruction is null) Error("CS027", "Null instruction.", n.Id); else CheckInstruction(instruction); }
                }
            }
        }
        var ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deviceIds = new HashSet<string>();
        foreach (var device in p.Devices)
        {
            if (device is null) { Error("CS030", "Null device."); continue; }
            Id(device.Id); deviceIds.Add(device.Id);
            if (device.IpAddress is null || device.IpAddress.Split('.').Length != 4 || !device.IpAddress.Split('.').All(part => byte.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)) || !IPAddress.TryParse(device.IpAddress, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) Error("CS031", "An IPv4 address is required.", device.Name);
            else if (!ips.Add(ip.ToString())) Error("CS032", "Duplicate device IP address.", device.Name);
            if (!double.IsFinite(device.X) || !double.IsFinite(device.Y) || device.Modules is null || device.Modules.Count > 32 || device.Modules.Any(m => m is null) || device.Name is null || device.Model is null || !Enum.IsDefined(device.Kind)) Error("CS033", "Invalid device geometry or modules.", device.Name);
        }
        foreach (var link in p.Links)
        {
            if (link is null) { Error("CS034", "Null link."); continue; }
            Id(link.Id);
            if (!deviceIds.Contains(link.From) || !deviceIds.Contains(link.To) || link.From == link.To || link.Subnet is null) Error("CS035", "Network link references missing or identical devices.", link.Id);
        }
        foreach (var screen in p.Screens)
        {
            if (screen is null || screen.Objects is null) { Error("CS040", "Incomplete HMI screen."); continue; }
            Id(screen.Id);
            if (string.IsNullOrWhiteSpace(screen.Name) || !double.IsFinite(screen.Width) || !double.IsFinite(screen.Height) || screen.Width is < 100 or > 8192 || screen.Height is < 100 or > 8192 || screen.Objects.Count > 10000) Error("CS041", "Invalid HMI screen size.", screen.Id);
            foreach (var o in screen.Objects)
            {
                if (o is null) { Error("CS042", "Null HMI object.", screen.Id); continue; }
                Id(o.Id);
                if (!double.IsFinite(o.X) || !double.IsFinite(o.Y) || !double.IsFinite(o.Width) || !double.IsFinite(o.Height) || o.Width is <= 0 or > 8192 || o.Height is <= 0 or > 8192 || o.X < 0 || o.Y < 0 || o.X + o.Width > screen.Width || o.Y + o.Height > screen.Height) Error("CS043", "Invalid HMI geometry.", o.Id);
                if (o.Text is null || o.Tag is null || o.Color is null || !Regex.IsMatch(o.Color, "^#[0-9A-Fa-f]{6}$")) Error("CS043", "Invalid HMI object text, binding or color.", o.Id);
                if (!Enum.IsDefined(o.Kind)) Error("CS044", "Unsupported HMI object.", o.Id);
                if (HmiShapeGeometry.IsShape(o.Kind) && !string.IsNullOrEmpty(o.Tag)) Error("CS046", "Basic HMI shapes do not have a tag binding.", o.Id);
                if (!string.IsNullOrEmpty(o.Tag) && !names.Contains(o.Tag)) Error("CS045", "HMI tag does not exist.", o.Id);
            }
        }
        return d;
    }
}
