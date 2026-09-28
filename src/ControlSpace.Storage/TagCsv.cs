using System.Globalization;
using System.Text;
using ControlSpace.Core;

namespace ControlSpace.Storage;

public static class TagCsv
{
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    public static string Export(IEnumerable<TagDefinition> tags)
    {
        var result = new StringBuilder("Name,DataType,Address,InitialValue,Retain,Comment\r\n");
        foreach (var tag in tags) result.AppendLine(string.Join(',', new[] { tag.Name, tag.Type.ToString(), tag.Address, PlcValues.Format(tag.Type, tag.InitialValue), tag.Retain ? "TRUE" : "FALSE", tag.Comment }.Select(Quote)));
        return result.ToString();
    }
    public static TagDefinition[] Import(string csv)
    {
        if (csv.Length > DocumentLimits.MaxBytes) throw new InvalidDataException("CSV exceeds 16 MiB.");
        var rows = Parse(csv.TrimStart('\uFEFF'));
        if (rows.Count == 0 || !rows[0].SequenceEqual(new[] { "Name", "DataType", "Address", "InitialValue", "Retain", "Comment" }, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Expected CSV header: Name,DataType,Address,InitialValue,Retain,Comment.");
        if (rows.Count - 1 > DocumentLimits.MaxTags) throw new InvalidDataException("Too many tags.");
        var result = new List<TagDefinition>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i]; if (row.Length == 1 && row[0].Length == 0) continue;
            if (row.Length != 6 || !PlcValues.IsIdentifier(row[0]) || !names.Add(row[0]) || !Enum.TryParse<PlcType>(row[1], true, out var type) || !Enum.IsDefined(type) || !PlcValues.TryParse(row[3], out var initial) || !bool.TryParse(row[4], out var retain)) throw new InvalidDataException($"Invalid tag at CSV record {i + 1}.");
            try { initial = PlcValues.Normalize(type, initial); } catch (ArithmeticException e) { throw new InvalidDataException($"CSV record {i + 1}: {e.Message}", e); }
            result.Add(new() { Name = row[0], Type = type, Address = row[2], InitialValue = initial, Retain = retain, Comment = row[5] });
        }
        return result.ToArray();
    }
    private static List<string[]> Parse(string csv)
    {
        var rows = new List<string[]>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false; var closed = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];
            if (quoted)
            {
                if (ch == '"') { if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; } else { quoted = false; closed = true; } }
                else field.Append(ch);
            }
            else if (ch == '"' && field.Length == 0 && !closed) quoted = true;
            else if (ch == ',' || ch is '\r' or '\n')
            {
                row.Add(field.ToString()); field.Clear(); closed = false;
                if (ch != ',') { if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++; rows.Add(row.ToArray()); row.Clear(); }
            }
            else { if (closed || ch == '"') throw new InvalidDataException("Malformed CSV quoting."); field.Append(ch); }
        }
        if (quoted) throw new InvalidDataException("Unterminated CSV quoted field.");
        if (field.Length > 0 || row.Count > 0 || closed) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }
}
