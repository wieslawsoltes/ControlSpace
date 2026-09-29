using ControlSpace.Core;
using ControlSpace.Languages;
using ControlSpace.Simulation;
using ControlSpace.Storage;
namespace ControlSpace.Engineering;

public sealed record HistoryItem(string Description, ControlProject Before, ControlProject After);
public sealed record SymbolReference(string Tag, string Block, string Network, string Instruction, bool Write);

/// <summary>UI-independent editing transactions. Runtime is invalidated after every model change.</summary>
public sealed class Workspace
{
    private readonly List<HistoryItem> _undo = [], _redo = [];
    private readonly int _historyLimit;
    private ControlProject _saved;
    private bool _isDirty;
    public ControlProject Project { get; private set; }
    public VirtualPlc? Controller { get; private set; }
    public CompilationResult? Compilation { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    /// <summary>Constant-time saved-state query. Changes must use Edit/Load/Undo/Redo.</summary>
    public bool IsDirty => _isDirty;
    public event EventHandler? Changed;
    public Workspace(ControlProject project, int historyLimit = 80)
    {
        if (historyLimit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(historyLimit));
        _historyLimit = historyLimit; Project = ProjectStorage.Deserialize(ProjectStorage.Serialize(project)); _saved = ProjectSnapshot.Clone(Project);
    }
    public void Edit(string description, Action<ControlProject> change)
    {
        if (Controller?.State == ControllerState.Running) throw new InvalidOperationException("Stop simulation before changing the program.");
        var before = ProjectSnapshot.Clone(Project); var draft = ProjectSnapshot.Clone(Project); change(draft);
        if (ProjectSnapshot.ContentEquals(draft, before)) return;
        draft = draft with { Revision = before.Revision + 1 };
        var validation = ProjectValidator.Validate(draft);
        if (validation.Any(d => d.Severity == Severity.Error)) throw new InvalidOperationException(string.Join("\n", validation.Where(d => d.Severity == Severity.Error).Take(10).Select(d => d.Message)));
        Project = draft; _undo.Add(new(description, before, ProjectSnapshot.Clone(draft))); if (_undo.Count > _historyLimit) _undo.RemoveAt(0); _redo.Clear(); Invalidate();
    }
    public void RenameProject(string name)
    {
        Replace(Project with { Name = name.Trim(), Revision = Project.Revision + 1 }, "Rename project");
    }
    private void Replace(ControlProject next, string description)
    {
        if (Controller?.State == ControllerState.Running) throw new InvalidOperationException("Stop simulation before editing.");
        var validation = ProjectValidator.Validate(next); if (validation.Any(d => d.Severity == Severity.Error)) throw new ArgumentException(validation.First(d => d.Severity == Severity.Error).Message);
        _undo.Add(new(description, Project, next)); if (_undo.Count > _historyLimit) _undo.RemoveAt(0); _redo.Clear(); Project = next; Invalidate();
    }
    public void Undo()
    {
        if (!CanUndo) return; Controller?.Stop(); var item = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); _redo.Add(item); Project = ProjectSnapshot.Clone(item.Before); Invalidate();
    }
    public void Redo()
    {
        if (!CanRedo) return; Controller?.Stop(); var item = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); _undo.Add(item); Project = ProjectSnapshot.Clone(item.After); Invalidate();
    }
    public void Load(ControlProject project)
    {
        var next = ProjectStorage.Deserialize(ProjectStorage.Serialize(project)); Controller?.Stop(); Project = next; _undo.Clear(); _redo.Clear(); _saved = ProjectSnapshot.Clone(next); Invalidate();
    }
    public void MarkSaved() => MarkSaved(Project);
    /// <summary>Mark the actual exported snapshot, not a possibly newer document after an asynchronous save.</summary>
    public void MarkSaved(ControlProject exported)
    {
        ArgumentNullException.ThrowIfNull(exported);
        _saved = ProjectSnapshot.Clone(exported); _isDirty = !ProjectSnapshot.ContentEquals(Project, _saved);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public CompilationResult Compile()
    {
        Controller?.Stop(); Compilation = ProjectCompiler.Compile(Project); Controller = Compilation.Success ? new VirtualPlc(Compilation.Program!) : null; Changed?.Invoke(this, EventArgs.Empty); return Compilation;
    }
    private void Invalidate() { _isDirty = !ProjectSnapshot.ContentEquals(Project, _saved); Controller?.Stop(); Controller = null; Compilation = null; Changed?.Invoke(this, EventArgs.Empty); }
    public void RenameTag(string oldName, string newName)
    {
        Edit("Rename tag", p =>
        {
            int index = p.Tags.FindIndex(t => t.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase)); if (index < 0) throw new ArgumentException("Tag does not exist.");
            p.Tags[index] = p.Tags[index] with { Name = newName };
            for (int i = 0; i < p.Blocks.Count; i++)
            {
                var block = p.Blocks[i];
                Instruction Rename(Instruction instruction) => instruction with { Tag = instruction.Tag.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? newName : instruction.Tag, Auxiliary = instruction.Auxiliary.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? newName : instruction.Auxiliary };
                var networks = block.Networks.Select(n => n with { Branches = n.Branches.Select(b => b.Select(Rename).ToList()).ToList(), Output = Rename(n.Output) }).ToList();
                string source = SclParser.RenameSymbol(block.Source, oldName, newName);
                p.Blocks[i] = block with { Networks = networks, Source = source };
            }
            foreach (var screen in p.Screens) for (int i = 0; i < screen.Objects.Count; i++) if (screen.Objects[i].Tag.Equals(oldName, StringComparison.OrdinalIgnoreCase)) screen.Objects[i] = screen.Objects[i] with { Tag = newName };
        });
    }
    public IReadOnlyList<SymbolReference> CrossReferences(string? tag = null)
    {
        var result = new List<SymbolReference>();
        foreach (var block in Project.Blocks) foreach (var network in block.Networks)
        {
            foreach (var instruction in network.Branches.SelectMany(b => b)) if (tag is null || instruction.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)) result.Add(new(instruction.Tag, block.Name, network.Title, instruction.Id, false));
            if (tag is null || network.Output.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)) result.Add(new(network.Output.Tag, block.Name, network.Title, network.Output.Id, true));
            if (!string.IsNullOrEmpty(network.Output.Auxiliary) && (tag is null || network.Output.Auxiliary.Equals(tag, StringComparison.OrdinalIgnoreCase))) result.Add(new(network.Output.Auxiliary, block.Name, network.Title, network.Output.Id, network.Output.Kind != InstructionKind.CountUp));
        }
        return result;
    }
}
