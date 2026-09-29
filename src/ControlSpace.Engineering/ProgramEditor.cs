using ControlSpace.Core;

namespace ControlSpace.Engineering;

/// <summary>Transactional program-block and flat-series/parallel LAD authoring for the supported model.</summary>
public sealed class ProgramEditor(Workspace workspace)
{
    private readonly Workspace _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    public static ProgramBlock Block(ControlProject project, string id) => project.Blocks.FirstOrDefault(b => b.Id == id)
        ?? throw new ArgumentException("The selected program block no longer exists.");
    public static LadderNetwork Network(ProgramBlock block, string id) => block.Networks.FirstOrDefault(n => n.Id == id)
        ?? throw new ArgumentException("The selected network no longer exists.");
    public static LadderNetwork? FindNetwork(ProgramBlock block, string? selection) => block.Networks.FirstOrDefault(n => n.Id == selection || n.Output.Id == selection || n.Branches.Any(b => b.Any(i => i.Id == selection)));
    private static string Id() => Guid.NewGuid().ToString("N");
    private void Edit(ControlProject expected, string caption, Action<ControlProject> change)
    {
        if (!ReferenceEquals(expected, _workspace.Project)) throw new InvalidOperationException("The project changed while this editor was open. Reopen the editor and try again.");
        _workspace.Edit(caption, change);
    }
    private static ProgramBlock LadderBlock(ControlProject p, string id)
    {
        var block = Block(p, id);
        if (block.Language != BlockLanguage.LAD) throw new InvalidOperationException("Open a LAD block to edit networks.");
        return block;
    }
    private static void CheckName(ControlProject p, string name, string? except = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || !(char.IsAsciiLetter(name[0]) || name[0] == '_') || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
            throw new ArgumentException("Use a block name of 1–64 letters, digits or underscores, starting with a letter or underscore.");
        if (p.Blocks.Any(b => b.Id != except && b.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("A block with this name already exists.");
    }
    public static int NextNumber(ControlProject p)
    {
        var occupied = p.Blocks.Select(b => b.Number).ToHashSet();
        for (int n = 1; n <= 65535; n++) if (!occupied.Contains(n)) return n;
        throw new InvalidOperationException("No free block number.");
    }
    public static string NextName(ControlProject p, string prefix = "Block")
    {
        prefix = new string(prefix.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').Take(54).ToArray());
        if (prefix.Length == 0 || char.IsAsciiDigit(prefix[0])) prefix = "Block";
        if (!p.Blocks.Any(b => b.Name.Equals(prefix, StringComparison.OrdinalIgnoreCase))) return prefix;
        for (int i = 1; ; i++) { string name = prefix + "_" + i; if (!p.Blocks.Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return name; }
    }
    private static void CheckNumber(ControlProject p, int number, string? except = null)
    {
        if (number is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(number), "Block number must be in 1–65535.");
        if (p.Blocks.Any(b => b.Id != except && b.Number == number)) throw new ArgumentException("This block number is already used in this project.");
    }
    public string AddBlock(ControlProject expected, string name, BlockLanguage language, int? number = null, bool cyclic = false)
    {
        string id = Id(); name = name.Trim();
        Edit(expected, "Add program block", p =>
        {
            CheckName(p, name); int n = number ?? NextNumber(p); CheckNumber(p, n);
            if (!Enum.IsDefined(language)) throw new ArgumentOutOfRangeException(nameof(language));
            p.Blocks.Add(new(id, name, n, language, cyclic, [], language == BlockLanguage.SCL ? "// " + name + "\n" : ""));
        });
        return id;
    }
    public void UpdateBlock(ControlProject expected, string id, string name, int number, bool cyclic)
    {
        name = name.Trim(); Edit(expected, "Edit block properties", p =>
        {
            CheckName(p, name, id); CheckNumber(p, number, id); var b = Block(p, id);
            p.Blocks[p.Blocks.IndexOf(b)] = b with { Name = name, Number = number, Cyclic = cyclic };
        });
    }
    private static LadderNetwork Clone(LadderNetwork n) => n with { Id = Id(), Branches = n.Branches.Select(b => b.Select(i => i with { Id = Id() }).ToList()).ToList(), Output = n.Output with { Id = Id() } };
    public string DuplicateBlock(ControlProject expected, string id)
    {
        string nextId = Id(); Edit(expected, "Duplicate program block", p =>
        {
            var b = Block(p, id); var copy = b with { Id = nextId, Name = NextName(p, b.Name + "_copy"), Number = NextNumber(p), Cyclic = false, Networks = b.Networks.Select(Clone).ToList() };
            // A copied program is offline until explicitly enabled; avoid introducing duplicate cyclic writers.
            p.Blocks.Insert(p.Blocks.IndexOf(b) + 1, copy);
        }); return nextId;
    }
    public void DeleteBlock(ControlProject expected, string id) => Edit(expected, "Delete program block", p => p.Blocks.Remove(Block(p, id)));
    public void MoveBlock(ControlProject expected, string id, int delta) => Edit(expected, "Change block scan order", p => Move(p.Blocks, p.Blocks.IndexOf(Block(p, id)), delta));
    private static void Move<T>(List<T> items, int index, int delta)
    {
        int target = (int)Math.Clamp((long)index + delta, 0, items.Count - 1); if (index == target) return;
        T item = items[index]; items.RemoveAt(index); items.Insert(target, item);
    }
    public string AddNetwork(ControlProject expected, string blockId, string? after = null)
    {
        string id = Id(); Edit(expected, "Insert LAD network", p =>
        {
            var b = LadderBlock(p, blockId); int index = after is null ? b.Networks.Count : b.Networks.IndexOf(Network(b, after)) + 1;
            var input = LadderInstructions.Create(p, InstructionKind.Contact);
            var output = LadderInstructions.Create(p, InstructionKind.Coil);
            b.Networks.Insert(index, new(id, "New network", "", [[input]], output));
        }); return id;
    }
    public void UpdateNetwork(ControlProject expected, string blockId, string id, string title, string comment) => Edit(expected, "Edit network title and comment", p =>
    {
        if (title is null || title.Length > 256 || comment is null || comment.Length > 8192) throw new ArgumentException("Network title is limited to 256 characters; comment to 8192 characters.");
        var b = LadderBlock(p, blockId); var n = Network(b, id); b.Networks[b.Networks.IndexOf(n)] = n with { Title = title, Comment = comment };
    });
    public string DuplicateNetwork(ControlProject expected, string blockId, string id)
    {
        string copyId = ""; Edit(expected, "Duplicate LAD network", p =>
        {
            var b = LadderBlock(p, blockId); var n = Network(b, id); var copy = Clone(n); copyId = copy.Id;
            b.Networks.Insert(b.Networks.IndexOf(n) + 1, copy);
        }); return copyId;
    }
    public void DeleteNetwork(ControlProject expected, string blockId, string id) => Edit(expected, "Delete LAD network", p => { var b = LadderBlock(p, blockId); b.Networks.Remove(Network(b, id)); });
    public void MoveNetwork(ControlProject expected, string blockId, string id, int delta) => Edit(expected, "Move LAD network", p => { var b = LadderBlock(p, blockId); Move(b.Networks, b.Networks.IndexOf(Network(b, id)), delta); });
    public string InsertContact(ControlProject expected, string blockId, string networkId, InstructionKind kind, string? afterInstruction = null)
    {
        string next = ""; Edit(expected, "Insert LAD instruction", p =>
        {
            if (!LadderInstructions.IsContact(kind)) throw new ArgumentException("Only contacts, edges and comparisons can be inserted into a path.");
            var n = Network(LadderBlock(p, blockId), networkId); var path = n.Branches[0]; int index = path.Count;
            if (afterInstruction is not null)
            {
                path = n.Branches.FirstOrDefault(b => b.Any(i => i.Id == afterInstruction)) ?? throw new ArgumentException("The selected contact is not in this network.");
                index = path.FindIndex(i => i.Id == afterInstruction) + 1;
            }
            var instruction = LadderInstructions.Create(p, kind); next = instruction.Id; path.Insert(index, instruction);
        }); return next;
    }
    public string AddBranch(ControlProject expected, string blockId, string networkId, int afterBranch = -1)
    {
        string firstId = ""; Edit(expected, "Add parallel LAD branch", p =>
        {
            var n = Network(LadderBlock(p, blockId), networkId);
            if (afterBranch < -1 || afterBranch >= n.Branches.Count) throw new ArgumentOutOfRangeException(nameof(afterBranch));
            var instruction = LadderInstructions.Create(p, InstructionKind.Contact); firstId = instruction.Id;
            n.Branches.Insert(afterBranch < 0 ? n.Branches.Count : afterBranch + 1, [instruction]);
        }); return firstId;
    }
    public void DeleteBranch(ControlProject expected, string blockId, string networkId, int branch) => Edit(expected, "Delete parallel LAD branch", p =>
    {
        var n = Network(LadderBlock(p, blockId), networkId);
        if (branch < 0 || branch >= n.Branches.Count) throw new ArgumentOutOfRangeException(nameof(branch));
        if (n.Branches.Count == 1) throw new InvalidOperationException("Keep at least one path in a network, or delete the network instead.");
        n.Branches.RemoveAt(branch);
    });
    public void DeleteContact(ControlProject expected, string blockId, string networkId, string id) => Edit(expected, "Delete LAD contact", p =>
    {
        var n = Network(LadderBlock(p, blockId), networkId); var path = n.Branches.FirstOrDefault(b => b.Any(i => i.Id == id)) ?? throw new ArgumentException("Select a contact. The network output cannot be deleted separately.");
        if (path.Count == 1) throw new InvalidOperationException("This is the last contact in the path. Delete the parallel branch or network instead.");
        path.RemoveAll(i => i.Id == id);
    });
    public void MoveContact(ControlProject expected, string blockId, string networkId, string id, int delta) => Edit(expected, "Move LAD contact", p =>
    {
        var n = Network(LadderBlock(p, blockId), networkId); var path = n.Branches.FirstOrDefault(b => b.Any(i => i.Id == id)) ?? throw new ArgumentException("Select a contact to move within its path.");
        Move(path, path.FindIndex(i => i.Id == id), delta);
    });
    public void UpdateInstruction(ControlProject expected, string blockId, string networkId, Instruction instruction) => Edit(expected, "Edit LAD instruction", p =>
    {
        var b = LadderBlock(p, blockId); var n = Network(b, networkId); bool output = n.Output.Id == instruction.Id;
        var path = n.Branches.FirstOrDefault(b => b.Any(i => i.Id == instruction.Id));
        if (!output && path is null) throw new ArgumentException("The selected instruction no longer exists.");
        LadderInstructions.Validate(p, instruction, output);
        if (output) b.Networks[b.Networks.IndexOf(n)] = n with { Output = instruction };
        else path![path.FindIndex(i => i.Id == instruction.Id)] = instruction;
    });
}

/// <summary>Shared insertion/operand rules; authoring rejects incompatible edits before committing.</summary>
public static class LadderInstructions
{
    public static bool IsContact(InstructionKind kind) => kind is InstructionKind.Contact or InstructionKind.NegatedContact or InstructionKind.RisingEdge or InstructionKind.FallingEdge or InstructionKind.Greater or InstructionKind.Less or InstructionKind.Equal;
    public static bool IsTimer(InstructionKind kind) => kind is InstructionKind.TimerOn or InstructionKind.TimerOff or InstructionKind.Pulse;
    public static bool IsOutput(InstructionKind kind) => Enum.IsDefined(kind) && !IsContact(kind);
    public static bool Accepts(InstructionKind kind, PlcTag tag) => kind switch
    {
        InstructionKind.Greater or InstructionKind.Less or InstructionKind.Equal => tag.Type != PlcType.Bool,
        InstructionKind.Contact or InstructionKind.NegatedContact or InstructionKind.RisingEdge or InstructionKind.FallingEdge => tag.Type == PlcType.Bool,
        InstructionKind.CountUp => tag.Type is PlcType.Int or PlcType.DInt && !PlcValues.IsInput(tag.Address),
        InstructionKind.Move => tag.Type != PlcType.Bool && !PlcValues.IsInput(tag.Address),
        _ => IsOutput(kind) && tag.Type == PlcType.Bool && !PlcValues.IsInput(tag.Address)
    };
    public static Instruction Create(ControlProject p, InstructionKind kind, string? tag = null)
    {
        var operand = tag is null ? p.Tags.FirstOrDefault(t => Accepts(kind, t)) : p.Tags.FirstOrDefault(t => t.Name.Equals(tag, StringComparison.OrdinalIgnoreCase) && Accepts(kind, t));
        if (operand is null) throw new InvalidOperationException("Create a compatible " + (IsOutput(kind) ? "writable " : "") + "PLC tag before inserting " + kind + ".");
        return Instruction.Create(kind, operand.Name, IsTimer(kind) ? 1000 : 0);
    }
    public static void Validate(ControlProject p, Instruction instruction, bool output)
    {
        if (output ? !IsOutput(instruction.Kind) : !IsContact(instruction.Kind)) throw new ArgumentException(output ? "Select a coil, timer, counter or MOVE output." : "Select a contact, edge or comparison.");
        var tag = p.Tags.FirstOrDefault(t => t.Name.Equals(instruction.Tag, StringComparison.OrdinalIgnoreCase));
        if (tag is null || !Accepts(instruction.Kind, tag)) throw new ArgumentException("The operand is missing, read-only or has an incompatible data type.");
        if (!double.IsFinite(instruction.Parameter)) throw new ArgumentException("Parameter must be finite.");
        if (IsTimer(instruction.Kind) && (instruction.Parameter < 0 || instruction.Parameter > int.MaxValue || instruction.Parameter != Math.Truncate(instruction.Parameter))) throw new ArgumentException("Timer preset must be an integer in 0–2147483647 milliseconds.");
        if (instruction.Kind == InstructionKind.Move && !PlcValues.IsValid(tag.Type, instruction.Parameter)) throw new ArgumentException("The MOVE constant cannot be represented by this tag.");
        if (string.IsNullOrEmpty(instruction.Auxiliary)) return;
        var auxiliary = p.Tags.FirstOrDefault(t => t.Name.Equals(instruction.Auxiliary, StringComparison.OrdinalIgnoreCase));
        bool valid = auxiliary is not null && (IsTimer(instruction.Kind) ? auxiliary.Type == PlcType.Time && !PlcValues.IsInput(auxiliary.Address) : instruction.Kind == InstructionKind.CountUp && auxiliary.Type == PlcType.Bool);
        if (!valid) throw new ArgumentException("Timers accept a writable TIME elapsed tag; counters accept a BOOL reset tag. Other instructions have no auxiliary operand.");
    }
}
