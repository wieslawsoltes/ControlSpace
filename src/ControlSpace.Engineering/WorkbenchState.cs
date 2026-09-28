using System.Text.Json;
namespace ControlSpace.Engineering;

/// <summary>User layout is independent of project content, revision and undo history.</summary>
public sealed record WorkbenchLayout
{
    public int Version { get; init; } = 1;
    public double ProjectWidth { get; init; } = 252;
    public double TaskWidth { get; init; } = 248;
    public double InspectorHeight { get; init; } = 204;
    public bool ProjectVisible { get; init; } = true;
    public bool TasksVisible { get; init; } = true;
    public bool InspectorVisible { get; init; } = true;
    public bool AutoHideTasks { get; init; }
    public string TaskCard { get; init; } = "Instructions";
    public string InspectorTab { get; init; } = "Info";
    public WorkbenchLayout Normalize() => this with
    {
        Version = 1,
        ProjectWidth = Bound(ProjectWidth, 180, 480, 252),
        TaskWidth = Bound(TaskWidth, 200, 460, 248),
        InspectorHeight = Bound(InspectorHeight, 120, 480, 204),
        TaskCard = TaskCard is "Instructions" or "Libraries" or "Testing" ? TaskCard : "Instructions",
        InspectorTab = InspectorTab is "Properties" or "Info" or "Diagnostics" ? InspectorTab : "Info"
    };
    private static double Bound(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    public string Serialize() => JsonSerializer.Serialize(Normalize());
    public static WorkbenchLayout Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 8192) return new();
        try { var value = JsonSerializer.Deserialize<WorkbenchLayout>(json); return value?.Version == 1 ? value.Normalize() : new(); }
        catch (JsonException) { return new(); }
    }
}

public sealed record EditorDocument(string Id, string Title);

/// <summary>Ordered, unique open editors with deterministic close/cycle/reorder semantics.</summary>
public sealed class EditorSession
{
    private readonly List<EditorDocument> _documents = [];
    public IReadOnlyList<EditorDocument> Documents => _documents.AsReadOnly();
    public string? ActiveId { get; private set; }
    public void Open(string id, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        int index = _documents.FindIndex(d => d.Id == id);
        var document = new EditorDocument(id, title);
        if (index < 0) _documents.Add(document); else _documents[index] = document;
        ActiveId = id;
    }
    public void Close(string id)
    {
        int index = _documents.FindIndex(d => d.Id == id); if (index < 0) return;
        _documents.RemoveAt(index);
        if (ActiveId == id) ActiveId = _documents.Count == 0 ? null : _documents[Math.Min(index, _documents.Count - 1)].Id;
    }
    public void CloseOthers(string id)
    {
        if (!_documents.Any(d => d.Id == id)) return;
        _documents.RemoveAll(d => d.Id != id); ActiveId = id;
    }
    public void Clear() { _documents.Clear(); ActiveId = null; }
    public string? Cycle(int direction)
    {
        if (_documents.Count == 0) return null;
        int index = _documents.FindIndex(d => d.Id == ActiveId);
        ActiveId = _documents[((index + Math.Sign(direction)) % _documents.Count + _documents.Count) % _documents.Count].Id;
        return ActiveId;
    }
    public void Move(string id, int delta)
    {
        int index = _documents.FindIndex(d => d.Id == id); if (index < 0) return;
        int target = (int)Math.Clamp((long)index + delta, 0, _documents.Count - 1);
        var item = _documents[index]; _documents.RemoveAt(index); _documents.Insert(target, item);
    }
}
