using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlSpace.Core;
namespace ControlSpace.Storage;

public static class ProjectStorage
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 64, Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };
    public static string Serialize(ControlProject project) => JsonSerializer.Serialize(project, Options);
    public static ControlProject Deserialize(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Project exceeds the 8 MiB import limit.");
        // Duplicate JSON keys are rejected instead of silently taking the last value.
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        CheckKeys(document.RootElement);
        var result = JsonSerializer.Deserialize<ControlProject>(json, Options) ?? throw new InvalidDataException("Project is null.");
        var errors = ProjectValidator.Validate(result).Where(d => d.Severity == Severity.Error).ToList();
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors.Take(20).Select(d => $"{d.Code}: {d.Message} ({d.Location})")));
        return result;
    }
    private static void CheckKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject()) { if (!keys.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property: " + property.Name); CheckKeys(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) CheckKeys(item);
    }
    public static ControlProject Clone(ControlProject project) => ProjectSnapshot.Clone(project);
    public static async Task SaveAtomicAsync(string path, ControlProject project, CancellationToken cancellationToken = default)
    {
        string target = Path.GetFullPath(path), directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory); string temporary = Path.Combine(directory, ".controlspace-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, Serialize(project), Encoding.UTF8, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string ExportTagsCsv(IEnumerable<PlcTag> tags)
    {
        static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        var builder = new StringBuilder("Name,DataType,Address,InitialValue,Comment,Retain\r\n");
        foreach (var tag in tags)
            builder.AppendLine(string.Join(",", new[] { tag.Name, tag.Type.ToString(), tag.Address, tag.InitialValue.ToString(CultureInfo.InvariantCulture), tag.Comment, tag.Retain.ToString() }.Select(Quote)));
        return builder.ToString();
    }
    public static IReadOnlyList<PlcTag> ImportTagsCsv(string csv)
    {
        if (Encoding.UTF8.GetByteCount(csv) > MaximumBytes) throw new InvalidDataException("CSV exceeds import limit.");
        var rows = CsvRows(csv);
        if (rows.Count == 0 || !rows[0].SequenceEqual(new[] { "Name", "DataType", "Address", "InitialValue", "Comment", "Retain" }, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid tag CSV header.");
        var tags = new List<PlcTag>();
        foreach (var row in rows.Skip(1))
        {
            if (row.Count != 6 || !Enum.TryParse<PlcType>(row[1], true, out var type) || !Enum.IsDefined(type) || !double.TryParse(row[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !bool.TryParse(row[5], out var retain)) throw new InvalidDataException("Invalid tag CSV row.");
            tags.Add(new(row[0], type, row[2], value, row[4], retain));
        }
        return tags;
    }
    private static List<List<string>> CsvRows(string csv)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder(); bool quoted = false, closed = false;
        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];
            if (quoted)
            {
                if (c == '"') { if (i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; closed = true; } }
                else cell.Append(c);
            }
            else if (c == ',' || c is '\n' or '\r')
            {
                row.Add(cell.ToString()); cell.Clear(); closed = false;
                if (c != ',') { if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++; if (row.Count != 1 || row[0].Length != 0) rows.Add(row); row = []; }
            }
            else if (c == '"' && cell.Length == 0 && !closed) quoted = true;
            else if (closed || c == '"') throw new InvalidDataException("Malformed CSV quoting.");
            else cell.Append(c);
        }
        if (quoted) throw new InvalidDataException("Unterminated CSV quoted field.");
        if (cell.Length > 0 || row.Count > 0 || closed) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}
