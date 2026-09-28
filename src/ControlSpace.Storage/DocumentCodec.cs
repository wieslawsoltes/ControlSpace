using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlSpace.Core;

namespace ControlSpace.Storage;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProjectDocument))]
internal partial class ProjectJsonContext : JsonSerializerContext { }

public static class DocumentCodec
{
    public const string Extension = ".controlspace";
    public static string Serialize(ProjectDocument project)
    {
        DocumentShape.Validate(project);
        var json = JsonSerializer.Serialize(project, ProjectJsonContext.Default.ProjectDocument);
        if (Encoding.UTF8.GetByteCount(json) > DocumentLimits.MaxBytes) throw new InvalidDataException("Project exceeds the 16 MiB limit.");
        return json;
    }
    public static ProjectDocument Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > DocumentLimits.MaxBytes || Encoding.UTF8.GetByteCount(json) > DocumentLimits.MaxBytes) throw new InvalidDataException("Project exceeds the 16 MiB limit.");
        try
        {
            var project = JsonSerializer.Deserialize(json, ProjectJsonContext.Default.ProjectDocument) ?? throw new InvalidDataException("Project is empty.");
            DocumentShape.Validate(project); return project;
        }
        catch (Exception e) when (e is JsonException or ArgumentException) { throw new InvalidDataException($"Invalid ControlSpace project: {e.Message}", e); }
    }
    public static ProjectDocument Clone(ProjectDocument project) => Deserialize(Serialize(project));
}

public sealed record StoredProject(ProjectDocument Project, string Token);
public interface IProjectStore
{
    Task<StoredProject?> LoadAsync(CancellationToken cancellationToken = default);
    Task<string> SaveAsync(ProjectDocument project, string? expectedToken, CancellationToken cancellationToken = default);
}
public sealed class StorageConflictException() : IOException("The saved project changed in another window. Export your work before reloading; the newer copy was not overwritten.");
