using ControlSpace.Core;
namespace ControlSpace.Languages;

public static class ProjectCompiler
{
    public static CompilationResult Compile(ControlProject project)
    {
        var diagnostics = ProjectValidator.Validate(project);
        if (diagnostics.Any(d => d.Severity == Severity.Error)) return new(null, diagnostics);
        project = ProjectSnapshot.Clone(project);
        var symbols = project.Tags.Select((tag, slot) => (tag.Name, slot)).ToDictionary(x => x.Name, x => x.slot, StringComparer.OrdinalIgnoreCase);
        var blocks = new List<CompiledBlock>(); var writes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Error(string message, string location) => diagnostics.Add(new(Severity.Error, "CS100", message, location));
        int Slot(string name, string location)
        {
            if (!string.IsNullOrWhiteSpace(name) && symbols.TryGetValue(name, out int slot)) return slot;
            Error("Unknown tag '" + name + "'.", location); return -1;
        }
        foreach (var block in project.Blocks)
        {
            // All blocks are checked; only explicitly cyclic blocks are executed.
            var networks = new List<CompiledNetwork>(); IReadOnlyList<Statement> statements = [];
            if (block.Language == BlockLanguage.SCL)
            {
                try { statements = new SclParser(block.Source, symbols, project.Tags).Parse(); }
                catch (SclException ex) { diagnostics.Add(new(Severity.Error, "CS110", ex.Message, block.Id, ex.Line, ex.Column)); }
            }
            else foreach (var network in block.Networks)
            {
                var paths = new List<IReadOnlyList<CompiledContact>>();
                foreach (var path in network.Branches)
                {
                    var contacts = new List<CompiledContact>();
                    foreach (var instruction in path)
                    {
                        int slot = Slot(instruction.Tag, instruction.Id);
                        bool comparison = instruction.Kind is InstructionKind.Greater or InstructionKind.Less or InstructionKind.Equal;
                        bool contact = instruction.Kind is InstructionKind.Contact or InstructionKind.NegatedContact or InstructionKind.RisingEdge or InstructionKind.FallingEdge;
                        if (!comparison && !contact) Error("Only contacts, edges and comparisons are supported in a path.", instruction.Id);
                        if (slot >= 0 && (contact ? project.Tags[slot].Type != PlcType.Bool : project.Tags[slot].Type == PlcType.Bool)) Error("Instruction operand type does not match tag type.", instruction.Id);
                        if (!double.IsFinite(instruction.Parameter)) Error("Instruction parameter must be finite.", instruction.Id);
                        contacts.Add(new(instruction.Id, instruction.Kind, slot, instruction.Parameter));
                    }
                    paths.Add(contacts);
                }
                var output = network.Output; int target = Slot(output.Tag, output.Id), aux = string.IsNullOrWhiteSpace(output.Auxiliary) ? -1 : Slot(output.Auxiliary, output.Id);
                bool timer = output.Kind is InstructionKind.TimerOn or InstructionKind.TimerOff or InstructionKind.Pulse;
                bool bit = output.Kind is InstructionKind.Coil or InstructionKind.SetCoil or InstructionKind.ResetCoil || timer;
                bool number = output.Kind is InstructionKind.CountUp or InstructionKind.Move;
                if (!bit && !number) Error("Unsupported network output instruction.", output.Id);
                if (target >= 0)
                {
                    if (PlcValues.IsInput(project.Tags[target].Address)) Error("Program cannot write to input image tags.", output.Id);
                    if (bit != (project.Tags[target].Type == PlcType.Bool)) Error("Output tag has the wrong type.", output.Id);
                    if (output.Kind == InstructionKind.CountUp && project.Tags[target].Type is not (PlcType.Int or PlcType.DInt)) Error("Counter output must be INT or DINT.", output.Id);
                    if (block.Cyclic && writes.TryGetValue(output.Tag, out var previous)) diagnostics.Add(new(Severity.Warning, "CS120", $"Multiple network writers for '{output.Tag}'; scan order is significant ({previous}).", output.Id));
                    if (block.Cyclic) writes[output.Tag] = output.Id;
                }
                if (timer && (!double.IsFinite(output.Parameter) || output.Parameter is < 0 or > int.MaxValue || output.Parameter != Math.Truncate(output.Parameter))) Error("Timer preset must be an integer in 0..2147483647 ms.", output.Id);
                if (output.Kind == InstructionKind.Move && target >= 0 && !PlcValues.IsValid(project.Tags[target].Type, output.Parameter)) Error("MOVE value cannot be represented by the target type.", output.Id);
                if (aux >= 0)
                {
                    if (timer && (project.Tags[aux].Type != PlcType.Time || PlcValues.IsInput(project.Tags[aux].Address))) Error("Timer elapsed tag must be a writable TIME tag.", output.Id);
                    if (output.Kind == InstructionKind.CountUp && project.Tags[aux].Type != PlcType.Bool) Error("Counter reset tag must be BOOL.", output.Id);
                }
                networks.Add(new(network.Id, paths, new(output.Id, output.Kind, target, output.Parameter, aux)));
            }
            if (block.Cyclic) blocks.Add(new(block.Id, networks, statements));
        }
        if (blocks.Count == 0) diagnostics.Add(new(Severity.Warning, "CS121", "No cyclic blocks are enabled."));
        if (diagnostics.Any(d => d.Severity == Severity.Error)) return new(null, diagnostics);
        diagnostics.Add(new(Severity.Info, "CS000", $"Compiled {project.Blocks.Count} blocks, {project.Tags.Count} symbols; {blocks.Count} cyclic blocks."));
        return new(new(project, symbols, blocks), diagnostics);
    }
}
