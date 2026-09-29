using ControlSpace.Core;
namespace ControlSpace.Engineering;

public sealed record ProjectNavigationEntry(string Id, string Caption, string? Target, int Depth, string Icon, bool Folder = false, string Description = "");
public enum ProjectNavigationMove { Previous, Next, First, Last, Parent, Child }

/// <summary>Platform-neutral indexed project outline. Search projects matching ancestors in
/// linear time; expansion and selection are view state, never project edits. UI-thread use.</summary>
public sealed class ProjectNavigator
{
    private readonly List<ProjectNavigationEntry> _entries = [], _visible = [];
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal), _visibleIndex = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private int[] _parents = [];
    private bool[] _matches = [];
    private ControlProject? _project;
    public ProjectNavigator() { Entries = _entries.AsReadOnly(); VisibleEntries = _visible.AsReadOnly(); }
    public IReadOnlyList<ProjectNavigationEntry> Entries { get; }
    public IReadOnlyList<ProjectNavigationEntry> VisibleEntries { get; }
    public string Filter { get; private set; } = "";
    public string? SelectedId { get; private set; }
    public string? ActiveTarget { get; private set; }
    public long IndexBuilds { get; private set; }
    public long ProjectionBuilds { get; private set; }
    public int LastProjectionVisits { get; private set; }
    public ProjectNavigationEntry? Selected => Find(SelectedId);
    public ProjectNavigationEntry? Find(string? id) => id is not null && _index.TryGetValue(id, out int i) ? _entries[i] : null;
    public int VisibleIndexOf(string? id) => id is not null && _visibleIndex.TryGetValue(id, out int i) ? i : -1;
    public bool IsExpanded(string id) => Filter.Length > 0 || !_collapsed.Contains(id);
    public void SetActive(string? target) => ActiveTarget = target;
    public bool Select(string id) { if (!_index.ContainsKey(id) || SelectedId == id) return false; SelectedId = id; return true; }

    /// <summary>Ignore edits which cannot change this outline (e.g. tag comments or SCL source).
    /// Snapshot collections must be treated as immutable by the host.</summary>
    public bool SetProject(ControlProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (ReferenceEquals(project, _project)) return false;
        bool changed = _project is null || !SameOutline(_project, project);
        bool newProject = _project?.Id != project.Id;
        _project = project;
        if (!changed) return false;
        if (newProject) { _collapsed.Clear(); SelectedId = ActiveTarget = null; }
        Build(project); Project(); return true;
    }
    private static bool SameOutline(ControlProject a, ControlProject b)
    {
        if (a.Id != b.Id || a.Name != b.Name || a.Tags.Count != b.Tags.Count || a.Blocks.Count != b.Blocks.Count || a.Screens.Count != b.Screens.Count) return false;
        foreach (var kind in new[] { DeviceKind.Controller, DeviceKind.Hmi })
            if (a.Devices.FirstOrDefault(d => d.Kind == kind)?.Name != b.Devices.FirstOrDefault(d => d.Kind == kind)?.Name) return false;
        for (int i = 0; i < a.Blocks.Count; i++)
        {
            var x = a.Blocks[i]; var y = b.Blocks[i];
            if (x.Id != y.Id || x.Name != y.Name || x.Number != y.Number || x.Language != y.Language || x.Cyclic != y.Cyclic) return false;
        }
        for (int i = 0; i < a.Screens.Count; i++)
        {
            var x = a.Screens[i]; var y = b.Screens[i];
            if (x.Id != y.Id || x.Name != y.Name || x.Width != y.Width || x.Height != y.Height) return false;
        }
        return true;
    }
    private void Build(ControlProject p)
    {
        IndexBuilds++; _entries.Clear(); _index.Clear();
        void Add(string id, string caption, string? target, int depth, string icon, bool folder = false, string description = "")
        { _index.Add(id, _entries.Count); _entries.Add(new(id, caption, target, depth, icon, folder, description)); }
        Add("project", p.Name, null, 0, "▣", true, "Local engineering project");
        Add("devices", "Devices & networks", "devices", 1, "▦", description: "Offline device configuration");
        var plc = p.Devices.FirstOrDefault(d => d.Kind == DeviceKind.Controller);
        Add("plc", (plc?.Name ?? "PLC_1") + " [virtual CPU]", null, 1, "▣", true);
        Add("config", "Device configuration", "devices", 2, "▦"); Add("online", "Online & diagnostics", "diagnostics", 2, "◉");
        Add("blocks", "Program blocks", null, 2, "▱", true, $"{p.Blocks.Count} program blocks");
        Add("add-block", "Add new block", "command:add-block", 3, "+"); Add("block-overview", "Block overview", "blocks", 3, "▤");
        foreach (var b in p.Blocks) Add("block:" + b.Id, b.Name + " [" + (b.Language == BlockLanguage.LAD ? "OB" : "FC") + b.Number + "]", "block:" + b.Id, 3, b.Language == BlockLanguage.LAD ? "◇" : "≡", description: b.Language + (b.Cyclic ? " · Cyclic simulation" : " · Offline block"));
        Add("tag-folder", "PLC tags", null, 2, "▱", true, $"{p.Tags.Count} declared tags"); Add("tags", "Default tag table [" + p.Tags.Count + "]", "tags", 3, "▤");
        Add("watch-folder", "Watch and force tables", null, 2, "▱", true); Add("watch", "Watch table_1", "watch", 3, "▤");
        Add("trace", "Traces", "trace", 2, "∿"); Add("references", "Cross-references", "references", 2, "↔");
        var hmi = p.Devices.FirstOrDefault(d => d.Kind == DeviceKind.Hmi);
        Add("hmi-folder", (hmi?.Name ?? "HMI_1") + " [virtual panel]", null, 1, "▣", true);
        Add("screens", "Screens", null, 2, "▱", true, $"{p.Screens.Count} screens");
        foreach (var s in p.Screens) Add("hmi:" + s.Id, s.Name, "hmi:" + s.Id, 3, "▣", description: $"HMI screen · {s.Width:0} × {s.Height:0}");
        Add("library", "Project library", "library", 1, "▥");
        _parents = new int[_entries.Count]; _matches = new bool[_entries.Count];
        var stack = new Stack<int>();
        for (int i = 0; i < _entries.Count; i++)
        {
            while (stack.Count > 0 && _entries[stack.Peek()].Depth >= _entries[i].Depth) stack.Pop();
            _parents[i] = stack.Count == 0 ? -1 : stack.Peek(); stack.Push(i);
        }
        _collapsed.RemoveWhere(id => !_index.ContainsKey(id));
        if (SelectedId is not null && !_index.ContainsKey(SelectedId)) SelectedId = null;
    }
    public bool SetFilter(string? filter)
    {
        filter = (filter ?? "").Trim();
        if (Filter == filter) return false;
        Filter = filter; Project(); return true;
    }
    private void Project()
    {
        ProjectionBuilds++; LastProjectionVisits = 0; _visible.Clear(); _visibleIndex.Clear();
        if (Filter.Length > 0)
        {
            Array.Clear(_matches);
            // Parents always precede children. One reverse pass bubbles all matches
            // without repeatedly walking past preceding siblings for each result.
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                LastProjectionVisits++;
                _matches[i] |= _entries[i].Caption.Contains(Filter, StringComparison.OrdinalIgnoreCase);
                if (_matches[i] && _parents[i] >= 0) _matches[_parents[i]] = true;
            }
        }
        int hiddenBelow = int.MaxValue;
        for (int i = 0; i < _entries.Count; i++)
        {
            LastProjectionVisits++; var entry = _entries[i];
            if (Filter.Length > 0) { if (!_matches[i]) continue; }
            else { if (entry.Depth > hiddenBelow) continue; hiddenBelow = _collapsed.Contains(entry.Id) ? entry.Depth : int.MaxValue; }
            _visibleIndex.Add(entry.Id, _visible.Count); _visible.Add(entry);
        }
    }
    public bool SetExpanded(string id, bool expanded)
    {
        if (Find(id)?.Folder != true) return false;
        bool changed = expanded ? _collapsed.Remove(id) : _collapsed.Add(id);
        if (changed) Project(); return changed;
    }
    public void ExpandAll(bool expanded)
    {
        if (expanded) _collapsed.Clear();
        else foreach (var entry in _entries) if (entry.Folder && entry.Depth > 0) _collapsed.Add(entry.Id);
        Project();
    }
    public string? Move(ProjectNavigationMove move)
    {
        if (_visible.Count == 0) return null;
        int index = VisibleIndexOf(SelectedId);
        if (index < 0) index = 0;
        var current = _visible[index];
        switch (move)
        {
            case ProjectNavigationMove.First: index = 0; break;
            case ProjectNavigationMove.Last: index = _visible.Count - 1; break;
            case ProjectNavigationMove.Next: index = Math.Min(index + 1, _visible.Count - 1); break;
            case ProjectNavigationMove.Previous: index = Math.Max(0, index - 1); break;
            case ProjectNavigationMove.Child:
                if (current.Folder && !IsExpanded(current.Id)) SetExpanded(current.Id, true);
                else if (index + 1 < _visible.Count && _visible[index + 1].Depth > current.Depth) index++;
                break;
            case ProjectNavigationMove.Parent:
                if (current.Folder && IsExpanded(current.Id) && Filter.Length == 0) SetExpanded(current.Id, false);
                else { int parent = _parents[_index[current.Id]]; if (parent >= 0) index = VisibleIndexOf(_entries[parent].Id); }
                break;
        }
        SelectedId = _visible[Math.Clamp(index, 0, _visible.Count - 1)].Id; return SelectedId;
    }
    /// <summary>Select the next visible caption beginning with this character,
    /// wrapping after the last row. Repeated initials cycle through siblings and
    /// other matches without changing the search, expansion or active document.</summary>
    public string? SelectByInitial(char initial)
    {
        if (!char.IsLetterOrDigit(initial) || _visible.Count == 0) return null;
        string prefix = initial.ToString();
        int start = VisibleIndexOf(SelectedId);
        for (int offset = 1; offset <= _visible.Count; offset++)
        {
            int index = (start + offset) % _visible.Count;
            if (!_visible[index].Caption.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            SelectedId = _visible[index].Id; return SelectedId;
        }
        return null;
    }
    /// <summary>Reveal the active document explicitly. Does not silently clear a user's filter.</summary>
    public string? RevealActive()
    {
        if (ActiveTarget is null) return null;
        int index = _entries.FindIndex(e => e.Target == ActiveTarget);
        if (index < 0) return null;
        bool changed = false;
        for (int parent = _parents[index]; parent >= 0; parent = _parents[parent]) changed |= _collapsed.Remove(_entries[parent].Id);
        if (changed) Project();
        if (VisibleIndexOf(_entries[index].Id) < 0) return null;
        SelectedId = _entries[index].Id; return SelectedId;
    }
    public string DescribeSelection()
    {
        if (Selected is not { } entry) return "Select a project object to see details.";
        var names = new List<string>(); int index = _index[entry.Id];
        for (int parent = _parents[index]; parent > 0; parent = _parents[parent]) names.Add(_entries[parent].Caption);
        names.Reverse();
        return entry.Caption + (entry.Description.Length > 0 ? "\n" + entry.Description : "") + (names.Count > 0 ? "\n" + string.Join(" › ", names) : "");
    }
}
