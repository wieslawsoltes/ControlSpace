using ControlSpace.Core;
namespace ControlSpace.Engineering;

/// <summary>Bounded runtime-only navigation history. Never enters project undo/persistence.</summary>
public sealed class HmiNavigationSession
{
    private readonly List<string> _previous = [];
    private string? _project;
    public int Count => _previous.Count;
    public void Clear() { _previous.Clear(); _project = null; }
    public string Activate(ControlProject project, string current, string target)
    {
        HmiEditor.Screen(project, current); HmiEditor.Screen(project, target);
        Bind(project);
        if (current == target) return current;
        _previous.Add(current); if (_previous.Count > 64) _previous.RemoveAt(0);
        return target;
    }
    public string Back(ControlProject project, string current)
    {
        HmiEditor.Screen(project, current); Bind(project);
        while (_previous.Count > 0)
        {
            string target = _previous[^1]; _previous.RemoveAt(_previous.Count - 1);
            if (target != current && project.Screens.Any(s => s.Id == target)) return target;
        }
        return current;
    }
    private void Bind(ControlProject project) { if (_project != project.Id) { _previous.Clear(); _project = project.Id; } }
}
