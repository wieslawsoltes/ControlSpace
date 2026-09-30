using System.Text.Json;
using System.Text.Json.Serialization;
using ControlSpace.Core;

namespace ControlSpace.Engineering;

public enum HmiArrange { Left, CenterX, Right, Top, CenterY, Bottom, SameWidth, SameHeight, SameSize, DistributeX, DistributeY, ScreenCenterX, ScreenCenterY }
public enum HmiOrder { Front, Back, Forward, Backward }

/// <summary>Snapshot-guarded, atomic HMI authoring. Selection order defines the reference object.</summary>
public sealed class HmiEditor(Workspace workspace)
{
    public const int ClipboardLimit = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions ClipboardOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private sealed record ClipboardData(string Format, int Version, List<HmiObject> Objects);
    public static HmiScreen Screen(ControlProject p, string id) => p.Screens.FirstOrDefault(s => s.Id == id) ?? throw new ArgumentException("This HMI screen no longer exists.");
    public static RectD Bounds(HmiObject o) => new(o.X, o.Y, o.Width, o.Height);
    public static RectD Bounds(IEnumerable<HmiObject> objects)
    {
        var items = objects.ToArray(); if (items.Length == 0) return new();
        double x = items.Min(o => o.X), y = items.Min(o => o.Y);
        return new(x, y, items.Max(o => o.X + o.Width) - x, items.Max(o => o.Y + o.Height) - y);
    }
    private static string Id() => Guid.NewGuid().ToString("N");
    private void Edit(ControlProject expected, string title, Action<ControlProject> action)
    {
        if (!ReferenceEquals(expected, workspace.Project)) throw new InvalidOperationException("The project changed. Cancel this operation and try again.");
        workspace.Edit(title, action);
    }
    public static string NextName(ControlProject p, string prefix = "Screen")
    {
        prefix = new string(prefix.Where(c => !char.IsControl(c)).Take(100).ToArray()).Trim(); if (prefix.Length == 0) prefix = "Screen";
        var names = p.Screens.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(prefix)) return prefix;
        for (int n = 1; ; n++) if (!names.Contains(prefix + "_" + n)) return prefix + "_" + n;
    }
    private static void Name(ControlProject p, string name, string? except = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl)) throw new ArgumentException("Use a screen name of 1–128 printable characters.");
        if (p.Screens.Any(s => s.Id != except && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("A screen with this name already exists.");
    }
    private static void Size(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width is < 100 or > 8192 || height is < 100 or > 8192) throw new ArgumentException("Screen dimensions must be in 100–8192 pixels.");
    }
    public string AddScreen(ControlProject expected, string name, double width = 960, double height = 540)
    {
        string id = Id(); name = name.Trim(); Edit(expected, "Add HMI screen", p => { Name(p, name); Size(width, height); p.Screens.Add(new(id, name, width, height, [])); }); return id;
    }
    public void UpdateScreen(ControlProject expected, string id, string name, double width, double height)
    {
        name = name.Trim(); Edit(expected, "HMI screen properties", p =>
        {
            Name(p, name, id); Size(width, height); var s = Screen(p, id);
            if (s.Objects.Any(o => o.X + o.Width > width || o.Y + o.Height > height)) throw new ArgumentException("Move or resize objects inside the new screen bounds first.");
            p.Screens[p.Screens.IndexOf(s)] = s with { Name = name, Width = width, Height = height };
        });
    }
    public string DuplicateScreen(ControlProject expected, string id)
    {
        string copyId = Id(); Edit(expected, "Duplicate HMI screen", p => { var s = Screen(p, id); p.Screens.Insert(p.Screens.IndexOf(s) + 1, s with { Id = copyId, Name = NextName(p, s.Name + "_copy"), Objects = s.Objects.Select(o => o with { Id = Id(), Button = o.Button is { Action: HmiButtonAction.ActivateScreen } button && button.TargetScreenId == id ? button with { TargetScreenId = copyId } : o.Button }).ToList() }); }); return copyId;
    }
    public void DeleteScreen(ControlProject expected, string id) => Edit(expected, "Delete HMI screen", p =>
    {
        var source = p.Screens.FirstOrDefault(s => s.Id != id && s.Objects.Any(o => o.Button is { Action: HmiButtonAction.ActivateScreen } b && b.TargetScreenId == id));
        if (source is not null) throw new InvalidOperationException($"Screen '{source.Name}' contains a button referencing this screen. Remove or retarget that action first.");
        p.Screens.Remove(Screen(p, id));
    });
    public static bool Accepts(HmiKind kind, PlcTag tag) => kind switch
    {
        HmiKind.Button => tag.Type == PlcType.Bool && PlcValues.IsInput(tag.Address),
        HmiKind.Lamp => tag.Type == PlcType.Bool,
        HmiKind.Numeric or HmiKind.Gauge or HmiKind.Tank => tag.Type != PlcType.Bool,
        _ => false
    };
    public static void ValidateBinding(ControlProject p, HmiObject o)
    {
        if (o.Tag.Length == 0) return;
        var tag = p.Tags.FirstOrDefault(t => t.Name.Equals(o.Tag, StringComparison.OrdinalIgnoreCase));
        if (tag is null || !Accepts(o.Kind, tag)) throw new ArgumentException("Choose a compatible tag. Buttons use BOOL input-image tags; lamps use BOOL; numeric displays use numeric tags.");
    }
    public string AddObject(ControlProject expected, string screenId, HmiKind kind, PointD? position = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        string id = Id(); Edit(expected, "Insert HMI object", p =>
        {
            var s = Screen(p, screenId); double w = Math.Min(kind == HmiKind.Label ? 240 : 160, s.Width), h = Math.Min(kind is HmiKind.Gauge or HmiKind.Tank ? 140 : 60, s.Height);
            var at = position ?? new PointD(20 + s.Objects.Count % 8 * 20, 20 + s.Objects.Count % 8 * 20);
            string tag = p.Tags.FirstOrDefault(t => Accepts(kind, t))?.Name ?? "";
            s.Objects.Add(new(id, kind, kind.ToString(), tag, Math.Clamp(at.X, 0, s.Width - w), Math.Clamp(at.Y, 0, s.Height - h), w, h));
        }); return id;
    }
    private static List<HmiObject> Selected(HmiScreen s, IEnumerable<string> ids)
    {
        var lookup = s.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var unique = ids.Distinct(StringComparer.Ordinal).ToArray();
        if (unique.Length == 0) throw new InvalidOperationException("Select an HMI object first.");
        return unique.Select(id => lookup.TryGetValue(id, out var o) ? o : throw new ArgumentException("A selected object no longer exists in this screen.")).ToList();
    }
    private static void Apply(HmiScreen s, IReadOnlyDictionary<string, RectD> boxes)
    {
        var indexes = s.Objects.Select((o, index) => (o.Id, index)).ToDictionary(x => x.Id, x => x.index, StringComparer.Ordinal);
        foreach (var (id, b) in boxes)
        {
            if (!indexes.TryGetValue(id, out int i)) throw new ArgumentException("A selected object no longer exists.");
            // Preserve valid fractional legacy objects without permitting further shrinking.
            var original = s.Objects[i];
            if (!double.IsFinite(b.X) || !double.IsFinite(b.Y) || !double.IsFinite(b.Width) || !double.IsFinite(b.Height) || b.Width < Math.Min(1, original.Width) || b.Height < Math.Min(1, original.Height) || b.X < 0 || b.Y < 0 || b.X + b.Width > s.Width + 1e-7 || b.Y + b.Height > s.Height + 1e-7) throw new ArgumentException("Objects must remain inside the screen, with width and height of at least one pixel.");
            // Remove arithmetic roundoff at the right and bottom edges only.
            s.Objects[i] = s.Objects[i] with { X = Math.Min(b.X, s.Width - b.Width), Y = Math.Min(b.Y, s.Height - b.Height), Width = b.Width, Height = b.Height };
        }
    }
    public void Transform(ControlProject expected, string screenId, IReadOnlyDictionary<string, RectD> boxes)
    {
        var copy = boxes.ToDictionary(p => p.Key, p => p.Value); if (copy.Count == 0) return;
        Edit(expected, "Transform HMI objects", p => Apply(Screen(p, screenId), copy));
    }
    public void UpdateObject(ControlProject expected, string screenId, HmiObject next) => Edit(expected, "HMI object properties", p =>
    {
        var s = Screen(p, screenId); int i = s.Objects.FindIndex(o => o.Id == next.Id); if (i < 0) throw new ArgumentException("Object no longer exists.");
        if (s.Objects[i].Kind != next.Kind) throw new ArgumentException("Object type cannot be changed by a property edit.");
        ValidateBinding(p, next); Apply(s, new Dictionary<string, RectD> { [next.Id] = Bounds(next) }); s.Objects[i] = next with { X = s.Objects[i].X, Y = s.Objects[i].Y };
    });
    public void DeleteObjects(ControlProject expected, string screenId, IEnumerable<string> ids)
    {
        var selected = Selected(Screen(expected, screenId), ids).Select(o => o.Id).ToHashSet();
        Edit(expected, "Delete HMI objects", p => Screen(p, screenId).Objects.RemoveAll(o => selected.Contains(o.Id)));
    }
    public void Move(ControlProject expected, string screenId, IEnumerable<string> ids, double dx, double dy)
    {
        var s = Screen(expected, screenId); var selected = Selected(s, ids); var b = Bounds(selected);
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) throw new ArgumentException("Movement must be finite.");
        dx = Math.Clamp(dx, -b.X, s.Width - b.X - b.Width); dy = Math.Clamp(dy, -b.Y, s.Height - b.Y - b.Height);
        Transform(expected, screenId, selected.ToDictionary(o => o.Id, o => new RectD(o.X + dx, o.Y + dy, o.Width, o.Height)));
    }
    public void Arrange(ControlProject expected, string screenId, IEnumerable<string> ids, HmiArrange operation)
    {
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        var s = Screen(expected, screenId); var selected = Selected(s, ids); var reference = selected[0]; var group = Bounds(selected);
        bool distribute = operation is HmiArrange.DistributeX or HmiArrange.DistributeY;
        bool center = operation is HmiArrange.ScreenCenterX or HmiArrange.ScreenCenterY;
        if (selected.Count < (distribute ? 3 : center ? 1 : 2)) throw new InvalidOperationException(distribute ? "Select at least three objects to distribute." : "Select at least two objects to align or match sizes.");
        var boxes = selected.ToDictionary(o => o.Id, Bounds);
        if (distribute)
        {
            bool x = operation == HmiArrange.DistributeX;
            var ordered = selected.OrderBy(o => x ? o.X : o.Y).ToArray();
            double first = x ? ordered[0].X : ordered[0].Y;
            double end = x ? ordered[^1].X + ordered[^1].Width : ordered[^1].Y + ordered[^1].Height;
            double gap = (end - first - ordered.Sum(o => x ? o.Width : o.Height)) / (ordered.Length - 1), at = first;
            for (int i = 0; i < ordered.Length; i++) { var o = ordered[i]; if (i > 0 && i < ordered.Length - 1) boxes[o.Id] = x ? Bounds(o) with { X = at } : Bounds(o) with { Y = at }; at += (x ? o.Width : o.Height) + gap; }
        }
        else foreach (var o in selected)
        {
            var b = Bounds(o);
            boxes[o.Id] = operation switch
            {
                HmiArrange.Left => b with { X = reference.X }, HmiArrange.Right => b with { X = reference.X + reference.Width - o.Width },
                HmiArrange.CenterX => b with { X = reference.X + (reference.Width - o.Width) / 2 },
                HmiArrange.Top => b with { Y = reference.Y }, HmiArrange.Bottom => b with { Y = reference.Y + reference.Height - o.Height },
                HmiArrange.CenterY => b with { Y = reference.Y + (reference.Height - o.Height) / 2 },
                HmiArrange.SameWidth => b with { Width = reference.Width }, HmiArrange.SameHeight => b with { Height = reference.Height }, HmiArrange.SameSize => b with { Width = reference.Width, Height = reference.Height },
                HmiArrange.ScreenCenterX => b with { X = o.X + (s.Width - group.Width) / 2 - group.X }, HmiArrange.ScreenCenterY => b with { Y = o.Y + (s.Height - group.Height) / 2 - group.Y }, _ => b
            };
        }
        Edit(expected, "Arrange HMI objects", p => Apply(Screen(p, screenId), boxes));
    }
    public void Reorder(ControlProject expected, string screenId, IEnumerable<string> ids, HmiOrder operation)
    {
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        var set = Selected(Screen(expected, screenId), ids).Select(o => o.Id).ToHashSet();
        Edit(expected, "Change HMI object order", p =>
        {
            var items = Screen(p, screenId).Objects;
            if (operation is HmiOrder.Front or HmiOrder.Back)
            {
                var selected = items.Where(o => set.Contains(o.Id)).ToArray(); items.RemoveAll(o => set.Contains(o.Id));
                items.InsertRange(operation == HmiOrder.Front ? items.Count : 0, selected);
            }
            else if (operation == HmiOrder.Forward) { for (int i = items.Count - 2; i >= 0; i--) if (set.Contains(items[i].Id) && !set.Contains(items[i + 1].Id)) (items[i], items[i + 1]) = (items[i + 1], items[i]); }
            else { for (int i = 1; i < items.Count; i++) if (set.Contains(items[i].Id) && !set.Contains(items[i - 1].Id)) (items[i], items[i - 1]) = (items[i - 1], items[i]); }
        });
    }
    public static string Copy(ControlProject project, string screenId, IEnumerable<string> ids)
    {
        var s = Screen(project, screenId); var set = Selected(s, ids).Select(o => o.Id).ToHashSet();
        // Preserve paint order, not click order, on the clipboard.
        var objects = s.Objects.Where(o => set.Contains(o.Id)).ToList();
        if (objects.Sum(o => (long)o.Text.Length + o.Tag.Length + o.Id.Length + 256) > ClipboardLimit / 6) throw new ArgumentException("Selection is too large for the HMI clipboard.");
        string text = JsonSerializer.Serialize(new ClipboardData("controlspace.hmi.objects", 1, objects), ClipboardOptions);
        if (text.Length > ClipboardLimit) throw new ArgumentException("HMI clipboard exceeds 2 MiB."); return text;
    }
    public IReadOnlyList<string> Paste(ControlProject expected, string screenId, string text, double offset = 20)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > ClipboardLimit) throw new ArgumentException("Invalid or oversized HMI clipboard.");
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
        static void Unique(JsonElement e) { if (e.ValueKind == JsonValueKind.Object) { var keys = new HashSet<string>(); foreach (var p in e.EnumerateObject()) { if (!keys.Add(p.Name)) throw new ArgumentException("Duplicate clipboard property."); Unique(p.Value); } } else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Unique(x); }
        Unique(doc.RootElement);
        var data = JsonSerializer.Deserialize<ClipboardData>(text, ClipboardOptions);
        if (data is null || data.Format != "controlspace.hmi.objects" || data.Version != 1 || data.Objects is null || data.Objects.Count is < 1 or > 10000 || data.Objects.Any(o => o is null)) throw new ArgumentException("Paste a ControlSpace HMI object selection.");
        var s = Screen(expected, screenId); var box = Bounds(data.Objects);
        if (!double.IsFinite(offset) || !double.IsFinite(box.X) || !double.IsFinite(box.Y) || !double.IsFinite(box.Width) || !double.IsFinite(box.Height) || box.Width > s.Width || box.Height > s.Height) throw new ArgumentException("The selection does not fit this screen.");
        double dx = Math.Clamp(box.X + offset, 0, s.Width - box.Width) - box.X, dy = Math.Clamp(box.Y + offset, 0, s.Height - box.Height) - box.Y;
        var copies = data.Objects.Select(o => o with { Id = Id(), X = o.X + dx, Y = o.Y + dy }).ToList();
        Edit(expected, "Paste HMI objects", p => { foreach (var o in copies) { if (o.Text is null || o.Tag is null) throw new ArgumentException("Invalid clipboard object."); ValidateBinding(p, o); } Screen(p, screenId).Objects.AddRange(copies); });
        return copies.Select(o => o.Id).ToArray();
    }
    public IReadOnlyList<string> DuplicateObjects(ControlProject expected, string screenId, IEnumerable<string> ids) => Paste(expected, screenId, Copy(expected, screenId, ids));
}
