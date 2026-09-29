using System.Globalization;
using System.Text;
using ControlSpace.Core;
namespace ControlSpace.Engineering;

public enum TagColumn { Name, DataType, Address, Retain, InitialValue, Comment, MonitorValue }
public sealed record TagCellEdit(string TagName, TagColumn Column, string Value);

/// <summary>Transactional tag-table operations shared by all hosts. No controller transport.</summary>
public sealed class TagTableEditor(Workspace workspace)
{
    public Workspace Workspace { get; } = workspace ?? throw new ArgumentNullException(nameof(workspace));
    public void Apply(IReadOnlyList<TagCellEdit> edits, long expectedRevision, string expectedProjectId)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (Workspace.Project.Revision != expectedRevision || Workspace.Project.Id != expectedProjectId)
            throw new InvalidOperationException("The project changed. Review the current table and retry the edit.");
        if (edits.Count > 70000) throw new ArgumentException("Paste is limited to 70,000 cells.");
        Workspace.Edit(edits.Count == 1 ? "Edit PLC tag cell" : "Paste PLC tag cells", p =>
        {
            var originals = p.Tags.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
            var changes = new Dictionary<string, PlcTag>(StringComparer.OrdinalIgnoreCase);
            foreach (var edit in edits)
            {
                if (!originals.TryGetValue(edit.TagName, out var original)) throw new ArgumentException("Tag no longer exists: " + edit.TagName);
                var tag = changes.GetValueOrDefault(edit.TagName, original);
                string value = edit.Value ?? throw new ArgumentException("A cell cannot be null.");
                if (value.Length > 16384) throw new ArgumentException("A cell is limited to 16,384 characters.");
                changes[edit.TagName] = edit.Column switch
                {
                    TagColumn.Name => tag with { Name = value.Trim() },
                    TagColumn.DataType => tag with { Type = ParseType(value) },
                    TagColumn.Address => tag with { Address = value.Trim().ToUpperInvariant() },
                    TagColumn.Retain => tag with { Retain = ParseBoolean(value) },
                    TagColumn.InitialValue => tag with { InitialValue = ParseValue(value) },
                    TagColumn.Comment => tag with { Comment = value },
                    _ => throw new ArgumentException("Monitor values are read-only. Use the virtual watch/force inspector.")
                };
            }
            var renames = changes.Where(e => !string.Equals(e.Key, e.Value.Name, StringComparison.Ordinal))
                .ToDictionary(e => e.Key, e => e.Value.Name, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < p.Tags.Count; i++) if (changes.TryGetValue(p.Tags[i].Name, out var tag)) p.Tags[i] = tag;
            // Rewrite all names simultaneously: swaps and chains must not cascade.
            if (renames.Count != 0) RewriteReferences(p, renames);
        });
    }
    public string Add(PlcType type = PlcType.Bool, string? duplicateName = null)
    {
        string created = "";
        Workspace.Edit(duplicateName is null ? "Add PLC tag" : "Duplicate PLC tag", p =>
        {
            if (p.Tags.Count >= 10000) throw new InvalidOperationException("The 10,000 tag project limit has been reached.");
            var source = duplicateName is null ? null : p.Tags.FirstOrDefault(t => t.Name.Equals(duplicateName, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("The tag to duplicate no longer exists.");
            type = source?.Type ?? type;
            if (!Enum.IsDefined(type)) throw new ArgumentException("Unsupported data type.");
            var names = p.Tags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string stem = source is null ? "Tag" : source.Name[..Math.Min(52, source.Name.Length)] + "_copy";
            int number = 1; created = stem + "_" + number;
            while (names.Contains(created)) created = stem + "_" + ++number;
            p.Tags.Add(new(created, type, AllocateAddress(p.Tags, type), source?.InitialValue ?? 0, source?.Comment ?? "", source?.Retain ?? false));
        });
        return created;
    }
    public void Delete(IReadOnlyCollection<string> names)
    {
        var selected = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0) return;
        Workspace.Edit("Delete PLC tags", p =>
        {
            var inUse = ReferencedNames(p).Where(selected.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (inUse.Length != 0) throw new InvalidOperationException("Remove program/HMI references before deleting: " + string.Join(", ", inUse.Take(8)));
            p.Tags.RemoveAll(t => selected.Contains(t.Name));
        });
    }
    public static string Cell(PlcTag tag, TagColumn column) => column switch
    {
        TagColumn.Name => tag.Name, TagColumn.DataType => tag.Type.ToString().ToUpperInvariant(),
        TagColumn.Address => tag.Address, TagColumn.Retain => tag.Retain ? "TRUE" : "FALSE",
        TagColumn.InitialValue => tag.Type == PlcType.Bool ? (tag.InitialValue == 0 ? "FALSE" : "TRUE") : tag.InitialValue.ToString("R", CultureInfo.InvariantCulture),
        TagColumn.Comment => tag.Comment, _ => ""
    };
    public static IReadOnlyList<PlcTag> Query(IReadOnlyList<PlcTag> tags, string filter, TagColumn? sort = null, bool descending = false)
    {
        string search = filter.Trim();
        IEnumerable<PlcTag> rows = tags.Where(t => search.Length == 0 || t.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || t.Address.Contains(search, StringComparison.OrdinalIgnoreCase) || t.Comment.Contains(search, StringComparison.OrdinalIgnoreCase)
            || t.Type.ToString().Contains(search, StringComparison.OrdinalIgnoreCase));
        if (sort is TagColumn column)
        {
            if (column == TagColumn.Address) rows = descending ? rows.OrderByDescending(t => AddressKey(t.Address)) : rows.OrderBy(t => AddressKey(t.Address));
            else if (column == TagColumn.InitialValue) rows = descending ? rows.OrderByDescending(t => t.InitialValue) : rows.OrderBy(t => t.InitialValue);
            else rows = descending ? rows.OrderByDescending(t => Cell(t, column), StringComparer.OrdinalIgnoreCase) : rows.OrderBy(t => Cell(t, column), StringComparer.OrdinalIgnoreCase);
        }
        return rows.ToArray();
    }
    public static (int First, int Count) VisibleRange(int rows, double offset, double viewport, double rowHeight = 24)
    {
        if (rows < 0 || !double.IsFinite(rowHeight) || rowHeight <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (!double.IsFinite(offset)) offset = 0;
        if (!double.IsFinite(viewport) || viewport < 0) viewport = 0;
        int first = (int)Math.Min(rows, Math.Max(0, Math.Floor(offset / rowHeight) - 1));
        int count = (int)Math.Min(rows - first, Math.Ceiling(viewport / rowHeight) + 3);
        return (first, count);
    }
    private static PlcType ParseType(string value) => Enum.TryParse<PlcType>(value.Trim(), true, out var type) && Enum.IsDefined(type)
        && !int.TryParse(value, out _) ? type : throw new ArgumentException("Use BOOL, INT, DINT, REAL or TIME.");
    private static bool ParseBoolean(string value) => value.Trim().ToUpperInvariant() switch
    { "TRUE" or "1" => true, "FALSE" or "0" => false, _ => throw new ArgumentException("Use TRUE or FALSE.") };
    public static double ParseValue(string value)
    {
        value = value.Trim();
        if (value.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return 1;
        if (value.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return 0;
        if (value.StartsWith("T#", StringComparison.OrdinalIgnoreCase) && value.EndsWith("ms", StringComparison.OrdinalIgnoreCase)) value = value[2..^2];
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? number : throw new ArgumentException("Enter a finite number, TRUE/FALSE, or T#<milliseconds>ms.");
    }
    private static long AddressKey(string address)
    {
        int start = 2; while (start < address.Length && !char.IsDigit(address[start])) start++;
        int end = address.IndexOf('.', start); if (end < 0) end = address.Length;
        long bytes = long.Parse(address.AsSpan(start, end - start), CultureInfo.InvariantCulture);
        return ((long)char.ToUpperInvariant(address[1]) << 32) + bytes * 8 + (end < address.Length ? address[end + 1] - '0' : 0);
    }
    private static string AllocateAddress(IReadOnlyList<PlcTag> tags, PlcType type)
    {
        var occupied = new HashSet<long>();
        foreach (var t in tags.Where(t => t.Address.StartsWith("%M", StringComparison.OrdinalIgnoreCase)))
        {
            long start = AddressKey(t.Address) & uint.MaxValue; int width = t.Type == PlcType.Bool ? 1 : t.Type == PlcType.Int ? 16 : 32;
            for (int i = 0; i < width; i++) occupied.Add(start + i);
        }
        int bits = type == PlcType.Bool ? 1 : type == PlcType.Int ? 16 : 32;
        for (long start = 800; start < 7999992; start += bits)
            if (Enumerable.Range(0, bits).All(i => !occupied.Contains(start + i)))
                return type == PlcType.Bool ? $"%M{start / 8}.{start % 8}" : $"%M{(type == PlcType.Int ? "W" : "D")}{start / 8}";
        throw new InvalidOperationException("No free marker address is available.");
    }
    private static IEnumerable<string> ReferencedNames(ControlProject p)
    {
        foreach (var block in p.Blocks)
        {
            foreach (var token in SymbolText.Identifiers(block.Source)) yield return token.Name;
            foreach (var op in block.Networks.SelectMany(n => n.Branches.SelectMany(b => b).Append(n.Output))) { yield return op.Tag; yield return op.Auxiliary; }
        }
        foreach (var obj in p.Screens.SelectMany(s => s.Objects)) yield return obj.Tag;
    }
    private static void RewriteReferences(ControlProject p, IReadOnlyDictionary<string, string> names)
    {
        string Name(string value) => names.TryGetValue(value, out var next) ? next : value;
        Instruction Op(Instruction op) => op with { Tag = Name(op.Tag), Auxiliary = Name(op.Auxiliary) };
        for (int i = 0; i < p.Blocks.Count; i++)
        {
            var b = p.Blocks[i];
            p.Blocks[i] = b with { Source = SymbolText.Rename(b.Source, names), Networks = b.Networks.Select(n => n with
            { Branches = n.Branches.Select(path => path.Select(Op).ToList()).ToList(), Output = Op(n.Output) }).ToList() };
        }
        foreach (var screen in p.Screens) for (int i = 0; i < screen.Objects.Count; i++) screen.Objects[i] = screen.Objects[i] with { Tag = Name(screen.Objects[i].Tag) };
    }
}

/// <summary>Bounded quoted TSV codec for spreadsheet clipboard interchange, not file-format conversion.</summary>
public static class TableClipboard
{
    public static string Write(IEnumerable<IEnumerable<string>> rows) => string.Join("\r\n", rows.Select(r => string.Join('\t', r.Select(v =>
        v.IndexOfAny(['\t', '\r', '\n', '"']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v))));
    public static IReadOnlyList<IReadOnlyList<string>> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 1_000_000) throw new ArgumentException("Clipboard text exceeds 1 MB.");
        var rows = new List<IReadOnlyList<string>>(); var row = new List<string>(); var cell = new StringBuilder();
        bool quoted = false, closed = false; int cells = 0;
        void Cell() { if (++cells > 70000 || cell.Length > 16384) throw new ArgumentException("Clipboard cell limit exceeded."); row.Add(cell.ToString()); cell.Clear(); closed = false; }
        void Row() { Cell(); rows.Add(row.ToArray()); row.Clear(); if (rows.Count > 10000) throw new ArgumentException("Clipboard row limit exceeded."); }
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted) { if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; closed = true; } } else cell.Append(c); continue; }
            if (c == '\t') Cell();
            else if (c is '\r' or '\n') { Row(); if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; }
            else if (closed) throw new ArgumentException("Unexpected text after a quoted cell.");
            else if (c == '"') { if (cell.Length != 0) throw new ArgumentException("Quotes must enclose the complete cell."); quoted = true; }
            else cell.Append(c);
        }
        if (quoted) throw new ArgumentException("Unterminated clipboard quote.");
        if (cell.Length > 0 || closed || row.Count > 0 || rows.Count == 0) Row();
        if (rows.Any(r => r.Count != rows[0].Count)) throw new ArgumentException("Paste requires a rectangular table.");
        return rows;
    }
}
