using ControlSpace.Core;

namespace ControlSpace.Languages;

public readonly record struct Operand(int Slot, double Constant, PlcType Type)
{
    public double Read(double[] values) => Slot >= 0 ? values[Slot] : Constant;
}
public sealed record CompiledContact(Guid Id, ContactKind Kind, Operand Operand, Operand CompareTo, int StateIndex);
public sealed record CompiledNetwork(Guid Id, CompiledContact[][] Paths, ActionKind Kind, int Target, Operand InputA, Operand InputB, double Preset, int ResetSlot, int AuxiliarySlot, int StateIndex);
public sealed record CompiledBlock(Guid Id, string Name, CompiledNetwork[] Networks, TextProgram? Text);
public sealed record CompiledProject(TagDefinition[] Tags, IReadOnlyDictionary<string, int> Symbols, CompiledBlock[] Blocks, int ContactCount, int NetworkCount, AlarmDefinition[] Alarms);
public sealed record Compilation(CompiledProject? Program, Diagnostic[] Diagnostics, CrossReference[] References)
{
    public bool Success => Program is not null && Diagnostics.All(d => d.Severity != Severity.Error);
}

public static class ProjectCompiler
{
    public static Compilation Compile(ProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var diagnostics = new List<Diagnostic>(); var references = new List<CrossReference>();
        void Error(string code, string message, string location = "Project") => diagnostics.Add(new(Severity.Error, code, message, location));
        try { DocumentShape.Validate(project); }
        catch (ArgumentException e) { Error("CS0001", e.Message); return new(null, diagnostics.ToArray(), []); }
        var symbols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var addressMap = new Dictionary<(char, int), string>();
        for (var i = 0; i < project.Tags.Length; i++)
        {
            var tag = project.Tags[i];
            if (!PlcValues.IsIdentifier(tag.Name)) Error("CS1001", $"Invalid tag name '{tag.Name}'.", "PLC tags");
            if (tag.Name.ToUpperInvariant() is "TRUE" or "FALSE" or "IF" or "THEN" or "ELSE" or "ELSIF" or "END_IF" or "WHILE" or "DO" or "END_WHILE" or "REPEAT" or "UNTIL" or "END_REPEAT" or "AND" or "OR" or "XOR" or "NOT" or "MOD") Error("CS1002", $"'{tag.Name}' is a reserved keyword.", "PLC tags");
            if (!symbols.TryAdd(tag.Name, i)) Error("CS1003", $"Duplicate tag '{tag.Name}'.", "PLC tags");
            try { PlcValues.Normalize(tag.Type, tag.InitialValue); } catch (ArithmeticException e) { Error("CS1004", $"{tag.Name}: {e.Message}", "PLC tags"); }
            if (tag.Address.Length > 0)
            {
                if (!PlcAddress.TryParse(tag.Address, tag.Type, out var address)) Error("CS1005", $"'{tag.Address}' is not a valid {tag.Type} I/Q/M address.", tag.Name);
                else
                {
                    for (var bit = address.StartBit; bit < address.StartBit + address.BitLength; bit++)
                        if (!addressMap.TryAdd((address.Area, bit), tag.Name)) { Error("CS1006", $"Address of '{tag.Name}' overlaps '{addressMap[(address.Area, bit)]}'. Aliased memory is not supported.", "PLC tags"); break; }
                }
            }
        }
        var networks = 0; var contacts = 0; var blocks = new List<CompiledBlock>();
        foreach (var block in project.Blocks)
        {
            var compiled = new List<CompiledNetwork>(); TextProgram? text = null;
            if (block.Language == ProgramLanguage.ST)
            {
                try
                {
                    text = StructuredTextCompiler.Compile(block.Source, symbols, project.Tags);
                    references.AddRange(text.References.Select(r => new CrossReference(r.Name, block.Name, Guid.Empty, $"{r.Access} (line {r.Line})")));
                }
                catch (TextCompileException e) { diagnostics.Add(new(Severity.Error, "CS2001", e.Message, block.Name, e.Line, e.Column)); }
            }
            else foreach (var network in block.Networks)
            {
                var location = $"{block.Name} / {network.Title}";
                Operand Read(string name, bool boolean = false)
                {
                    Operand result;
                    if (symbols.TryGetValue(name, out var slot))
                    {
                        result = new(slot, 0, project.Tags[slot].Type); references.Add(new(project.Tags[slot].Name, block.Name, network.Id, "Read"));
                    }
                    else if (PlcValues.TryParse(name, out var number)) result = new(-1, number, name.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || name.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ? PlcType.Bool : name.Contains('.') ? PlcType.Real : PlcType.DInt);
                    else { Error("CS2101", $"Unknown operand '{name}'.", location); result = new(-1, 0, PlcType.Bool); }
                    if (boolean && result.Type != PlcType.Bool) Error("CS2102", $"Operand '{name}' must be BOOL.", location);
                    return result;
                }
                int Write(string name, bool? boolean)
                {
                    if (!symbols.TryGetValue(name, out var slot)) { Error("CS2103", $"Unknown output tag '{name}'.", location); return 0; }
                    var tag = project.Tags[slot]; references.Add(new(tag.Name, block.Name, network.Id, "Write"));
                    if (boolean.HasValue && (tag.Type == PlcType.Bool) != boolean.Value) Error("CS2104", $"Output '{name}' has the wrong data type.", location);
                    if (PlcAddress.TryParse(tag.Address, tag.Type, out var address) && address.IsInput) Error("CS2105", $"Input tag '{name}' is read-only in program logic.", location);
                    return slot;
                }
                var paths = network.Paths.Select(path => path.Select(contact =>
                {
                    var isBit = contact.Kind <= ContactKind.FallingEdge;
                    var left = Read(contact.Operand, isBit); var right = isBit ? new Operand(-1, 0, PlcType.Bool) : Read(contact.CompareTo);
                    if (!isBit && (left.Type == PlcType.Bool) != (right.Type == PlcType.Bool)) Error("CS2106", "Comparison cannot mix BOOL and numeric values.", location);
                    if (!isBit && left.Type == PlcType.Bool && contact.Kind is not ContactKind.Equal and not ContactKind.NotEqual) Error("CS2106", "BOOL supports only equality comparisons.", location);
                    return new CompiledContact(contact.Id, contact.Kind, left, right, contacts++);
                }).ToArray()).ToArray();
                var action = network.Action;
                var bitAction = action.Kind <= ActionKind.TP;
                var target = Write(action.Target, action.Kind == ActionKind.Move ? null : bitAction);
                if (action.Kind == ActionKind.CTU && symbols.TryGetValue(action.Target, out var countSlot) && project.Tags[countSlot].Type is not PlcType.Int and not PlcType.DInt) Error("CS2107", "CTU count target must be INT or DINT.", location);
                if (action.Preset < 0 || action.Preset > int.MaxValue || Math.Truncate(action.Preset) != action.Preset) Error("CS2108", "Timer/counter preset must be a non-negative integer.", location);
                var a = new Operand(-1, 0, PlcType.DInt); var b = a;
                if (action.Kind >= ActionKind.Move)
                {
                    a = Read(action.InputA);
                    if (action.Kind != ActionKind.Move) { b = Read(action.InputB); if (a.Type == PlcType.Bool || b.Type == PlcType.Bool) Error("CS2109", "Arithmetic operands must be numeric.", location); }
                    if (symbols.TryGetValue(action.Target, out var to) && action.Kind == ActionKind.Move && (project.Tags[to].Type == PlcType.Bool) != (a.Type == PlcType.Bool)) Error("CS2110", "MOVE cannot mix BOOL and numeric values.", location);
                }
                var reset = -1; if (action.ResetTag.Length > 0) { var resetOperand = Read(action.ResetTag, true); reset = resetOperand.Slot; if (reset < 0) Error("CS2111", "Reset must reference a BOOL tag.", location); }
                var auxiliary = action.AuxiliaryTag.Length == 0 ? -1 : Write(action.AuxiliaryTag, action.Kind == ActionKind.CTU);
                if (auxiliary >= 0 && action.Kind is ActionKind.TON or ActionKind.TOF or ActionKind.TP && project.Tags[auxiliary].Type != PlcType.Time) Error("CS2112", "Timer auxiliary elapsed output must be TIME.", location);
                compiled.Add(new(network.Id, paths, action.Kind, target, a, b, action.Preset, reset, auxiliary, networks++));
            }
            if (block.Enabled) blocks.Add(new(block.Id, block.Name, compiled.ToArray(), text));
        }
        foreach (var duplicate in project.Blocks.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) Error("CS2200", $"Duplicate block name '{duplicate.Key}'.", "Program blocks");
        var ips = new HashSet<string>(StringComparer.Ordinal);
        foreach (var device in project.Devices)
        {
            var parts = device.IpAddress.Split('.');
            if (parts.Length != 4 || parts.Any(p => !byte.TryParse(p, out _)) || parts.Any(p => p.Length > 1 && p[0] == '0')) Error("CS3001", $"Invalid IPv4 address '{device.IpAddress}'.", device.Name);
            if (!ips.Add(device.IpAddress)) Error("CS3002", $"Duplicate IP address '{device.IpAddress}'.", device.Name);
            if (device.Modules.GroupBy(m => m.Slot).Any(g => g.Count() > 1)) Error("CS3003", "Two modules occupy the same slot.", device.Name);
        }
        var deviceIds = project.Devices.Select(d => d.Id).ToHashSet();
        foreach (var link in project.Connections)
            if (link.From == link.To || !deviceIds.Contains(link.From) || !deviceIds.Contains(link.To)) Error("CS3004", "Connection has missing or identical endpoints.", "Devices & networks");
        foreach (var screen in project.Screens) foreach (var item in screen.Objects)
        {
            if (!string.IsNullOrEmpty(item.Tag))
            {
                if (!symbols.TryGetValue(item.Tag, out var slot)) Error("CS4001", $"Unknown HMI tag '{item.Tag}'.", $"{screen.Name}/{item.Name}");
                else if (item.Kind is HmiKind.Button or HmiKind.Lamp or HmiKind.Motor && project.Tags[slot].Type != PlcType.Bool) Error("CS4002", "This HMI object requires a BOOL binding.", $"{screen.Name}/{item.Name}");
            }
        }
        foreach (var alarm in project.Alarms)
            if (!symbols.TryGetValue(alarm.Tag, out var slot) || project.Tags[slot].Type != PlcType.Bool) Error("CS4003", $"Alarm '{alarm.Message}' requires a valid BOOL tag.", "HMI alarms");
        if (project.Blocks.Length == 0) diagnostics.Add(new(Severity.Warning, "CS0002", "Project contains no executable program blocks."));
        var program = diagnostics.Any(d => d.Severity == Severity.Error) ? null : new CompiledProject(project.Tags.ToArray(), symbols, blocks.ToArray(), contacts, networks, project.Alarms.ToArray());
        return new(program, diagnostics.ToArray(), references.ToArray());
    }
}
