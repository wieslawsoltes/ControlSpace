using System.Text.Json;
using ControlSpace.Core;
using ControlSpace.Engineering;
using ControlSpace.Languages;
using ControlSpace.Simulation;
using ControlSpace.Storage;

// Dependency-free regression runner for the same C# libraries used by Uno.
int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
}
static void Check(bool condition, string message = "Assertion failed")
{
    if (!condition) throw new InvalidOperationException(message);
}
static void Equal<T>(T expected, T actual) where T : notnull => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; got {actual}.");
static void Reject(Action action)
{
    try { action(); }
    catch (Exception e) when (e is ArgumentException or InvalidOperationException or InvalidDataException or JsonException or SclException) { return; }
    throw new InvalidOperationException("Expected operation to reject invalid input.");
}
static CompiledProgram Compile(ControlProject project)
{
    var result = ProjectCompiler.Compile(project);
    Check(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
    return result.Program!;
}
static ControlProject Scl(string source)
{
    var p = DemoProject.Create();
    return p with { Blocks = [p.Blocks[1] with { Source = source }] };
}
static VirtualPlc Runtime(ControlProject? project = null) => new(Compile(project ?? DemoProject.Create()));
static bool Scan(VirtualPlc controller) => controller.Step(TimeSpan.FromMilliseconds(100), singleStep: true);

Test("Demo schema and compilation", () => { var p = DemoProject.Create(); Check(!ProjectValidator.Validate(p).Any(d => d.Severity == Severity.Error)); Equal(2, Compile(p).Blocks.Count); });
Test("Project JSON round trip", () => { var json = ProjectStorage.Serialize(DemoProject.Create()); Equal(json, ProjectStorage.Serialize(ProjectStorage.Deserialize(json))); });
Test("Compiler snapshot isolation", () => { var p = DemoProject.Create(); var c = Compile(p); p.Tags[0] = p.Tags[0] with { Name = "Changed" }; Equal("Start_PB", c.Project.Tags[0].Name); });
Test("Duplicate JSON properties rejected", () => Reject(() => ProjectStorage.Deserialize(ProjectStorage.Serialize(DemoProject.Create()).Replace("\"version\": 1", "\"version\": 1, \"version\": 1"))));
Test("Unknown JSON properties rejected", () => Reject(() => ProjectStorage.Deserialize(ProjectStorage.Serialize(DemoProject.Create()).Insert(1, "\"unknown\": true,"))));
Test("Unsupported project version rejected", () => Reject(() => ProjectStorage.Deserialize(ProjectStorage.Serialize(DemoProject.Create() with { Version = 999 }))));
Test("Tag CSV round trip with quotes and newlines", () => { var p = DemoProject.Create(); p.Tags[0] = p.Tags[0] with { Comment = "A, \"quoted\"\ncomment" }; Check(p.Tags.SequenceEqual(ProjectStorage.ImportTagsCsv(ProjectStorage.ExportTagsCsv(p.Tags)))); });
Test("CSV invalid header rejected", () => Reject(() => ProjectStorage.ImportTagsCsv("Name,Type\nA,Bool")));
Test("CSV unterminated quote rejected", () => Reject(() => ProjectStorage.ImportTagsCsv("\"unterminated")));
foreach (var (type, good, bad) in new[] { (PlcType.Bool, 1d, 2d), (PlcType.Int, 32767d, 32768d), (PlcType.DInt, -2147483648d, -2147483649d), (PlcType.Time, 2147483647d, -1d), (PlcType.Real, .125d, double.PositiveInfinity) })
    Test($"{type} numeric boundaries", () => { Check(PlcValues.IsValid(type, good)); Check(!PlcValues.IsValid(type, bad)); });
foreach (var (name, source, symbol, expected) in new[]
{
    ("precedence", "Speed_Actual := 2 + 3 * 4;", "Speed_Actual", 14d),
    ("parentheses", "Speed_Actual := (2 + 3) * 4;", "Speed_Actual", 20d),
    ("unary", "Speed_Actual := -2 + +7;", "Speed_Actual", 5d),
    ("modulo", "Part_Count := 17 MOD 5;", "Part_Count", 2d),
    ("exponent", "Speed_Actual := 1.25e2 / 5;", "Speed_Actual", 25d),
    ("boolean", "Motor_Run := TRUE AND NOT FALSE OR FALSE;", "Motor_Run", 1d),
    ("xor", "Motor_Run := TRUE XOR TRUE;", "Motor_Run", 0d),
    ("comparison", "Motor_Run := 2 <= 3;", "Motor_Run", 1d),
    ("if else", "IF FALSE THEN Speed_Actual := 1; ELSE Speed_Actual := 2; END_IF;", "Speed_Actual", 2d),
    ("nested if", "IF TRUE THEN IF FALSE THEN Speed_Actual := 4; ELSE Speed_Actual := 7; END_IF; END_IF;", "Speed_Actual", 7d),
    ("case insensitive", "speed_actual := SPEED_SETPOINT;", "Speed_Actual", 65d),
    ("comments", "// before\n(* middle *) Speed_Actual := 4; // after", "Speed_Actual", 4d),
    ("quoted symbols", "\"Speed_Actual\" := \"Speed_Setpoint\";", "Speed_Actual", 65d)
}) Test($"SCL executes {name}", () => { var r = Runtime(Scl(source)); Check(Scan(r), r.Fault ?? "Scan failed"); Equal(expected, r.Read(symbol)); });
foreach (string source in new[] { "Start_PB := TRUE;", "Unknown := 5;", "Speed_Actual := 5", "Motor_Run := 1;", "IF 1 THEN Motor_Run := TRUE; END_IF;", "Speed_Actual := 1 AND 2;", "WHILE TRUE DO Motor_Run := TRUE; END_WHILE;", "Speed_Actual := sin(5);", "(* incomplete", "\"Speed_Actual := 1;", "Speed_Actual := (1 + 2;", "Speed_Actual := " + new string('(', 80) + "1" + new string(')', 80) + ";" })
    Test($"SCL rejects {source[..Math.Min(source.Length, 48)]}", () => Check(!ProjectCompiler.Compile(Scl(source)).Success));
Test("SCL diagnostic source position", () => { var d = ProjectCompiler.Compile(Scl("// comment\nUnknown := 5;")).Diagnostics.First(d => d.Severity == Severity.Error); Equal(2, d.Line); Check(d.Column >= 1); });
Test("Symbol rename preserves comments and longer names", () => Equal("// Speed_Actual\n\"Speed\" := Speed + Speed_ActualExtra;", SclParser.RenameSymbol("// Speed_Actual\n\"Speed_Actual\" := Speed_Actual + Speed_ActualExtra;", "Speed_Actual", "Speed")));
Test("Noncyclic block not executed", () => { var p = Scl("Speed_Actual := 42;"); p.Blocks[0] = p.Blocks[0] with { Cyclic = false }; var r = Runtime(p); Check(Scan(r)); Equal(0d, r.Read("Speed_Actual")); });
Test("LAD seal-in, timer, SCL and stop input", () => { var r = Runtime(); r.SetInput("Start_PB", 1); Check(Scan(r)); r.SetInput("Start_PB", 0); for (int i = 1; i < 20; i++) Check(Scan(r)); Equal(1d, r.Read("Motor_Run")); Equal(1d, r.Read("Ready_Lamp")); Equal(2000d, r.Read("Delay_ET")); Equal(65d, r.Read("Speed_Actual")); r.SetInput("Stop_PB", 1); Check(Scan(r)); Equal(0d, r.Read("Motor_Run")); Equal(0d, r.Read("Speed_Actual")); });
Test("Counter rising edges and reset priority", () => { var r = Runtime(); r.SetInput("Part_Sensor", 1); Check(Scan(r)); Check(Scan(r)); Equal(1d, r.Read("Part_Count")); r.SetInput("Part_Sensor", 0); Check(Scan(r)); r.SetInput("Part_Sensor", 1); Check(Scan(r)); Equal(2d, r.Read("Part_Count")); r.SetInput("Reset_Count", 1); Check(Scan(r)); Equal(0d, r.Read("Part_Count")); });
Test("Force and release", () => { var r = Runtime(); r.Force("Speed_Setpoint", 35); Check(Scan(r)); Equal(35d, r.Read("Speed_Setpoint")); r.Release("Speed_Setpoint"); Equal(0, r.Forces.Count); });
Test("STOP clears outputs and forces", () => { var r = Runtime(); r.Run(); r.SetInput("Start_PB", 1); Check(Scan(r)); r.Force("Motor_Run", 1); r.Stop(); Equal(0d, r.Read("Motor_Run")); Equal(0, r.Forces.Count); Equal(ControllerState.Stopped, r.State); Check(!r.Step(TimeSpan.FromMilliseconds(100))); });
Test("Fault rolls back partial scan", () => { var r = Runtime(Scl("Speed_Setpoint := 50; Speed_Actual := 1 / 0;")); Check(!Scan(r)); Equal(ControllerState.Faulted, r.State); Equal(65d, r.Read("Speed_Setpoint")); Equal(0L, r.Cycle); Reject(r.Run); r.Reset(); Equal(ControllerState.Stopped, r.State); });
Test("Invalid scan period rejected", () => { var r = Runtime(); Reject(() => r.Step(TimeSpan.Zero, true)); Reject(() => r.Step(TimeSpan.FromMilliseconds(1001), true)); });
Test("Input writes restricted to input image", () => { var r = Runtime(); Reject(() => r.SetInput("Motor_Run", 1)); Reject(() => r.SetInput("Start_PB", 2)); });
Test("Trace ring remains bounded and ordered", () => { var r = Runtime(); var trace = new TraceBuffer(2); for (int i = 0; i < 3; i++) { Check(Scan(r)); trace.Add(r.Snapshot()); } Equal(2, trace.Count); Equal(2L, trace.Read()[0].Cycle); Equal(3L, trace.Read()[1].Cycle); trace.Clear(); Equal(0, trace.Count); });
Test("Workspace undo/redo and dirty state", () => { var w = new Workspace(DemoProject.Create()); Check(!w.IsDirty); w.RenameProject("Changed"); Check(w.IsDirty); w.Undo(); Equal("Conveyor_Line", w.Project.Name); Check(!w.IsDirty); w.Redo(); Equal("Changed", w.Project.Name); w.MarkSaved(); Check(!w.IsDirty); });
Test("Editing invalidates compiled runtime", () => { var w = new Workspace(DemoProject.Create()); Check(w.Compile().Success); w.Edit("Comment", p => p.Tags[0] = p.Tags[0] with { Comment = "Edited" }); Check(w.Controller is null); Check(w.Compilation is null); });
Test("Editing while RUN is rejected", () => { var w = new Workspace(DemoProject.Create()); Check(w.Compile().Success); w.Controller!.Run(); Reject(() => w.RenameProject("Blocked")); Equal("Conveyor_Line", w.Project.Name); });
Test("Tag rename updates program and HMI references", () => { var w = new Workspace(DemoProject.Create()); w.RenameTag("Motor_Run", "Drive_Run"); Check(w.Compile().Success); Check(w.Project.Blocks[1].Source.Contains("Drive_Run")); Check(w.Project.Screens[0].Objects.Any(o => o.Tag == "Drive_Run")); Check(w.CrossReferences("Motor_Run").Count == 0); });
Test("Atomic save and reopen", () => { string dir = Path.Combine(Path.GetTempPath(), "controlspace-test-" + Guid.NewGuid().ToString("N")); try { string file = Path.Combine(dir, "project.json"); var p = DemoProject.Create(); ProjectStorage.SaveAtomicAsync(file, p).GetAwaiter().GetResult(); Equal(ProjectStorage.Serialize(p), ProjectStorage.Serialize(ProjectStorage.Deserialize(File.ReadAllText(file)))); } finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); } });
int fixtureArg = Array.IndexOf(args, "--fixtures");
string fixtureDir = fixtureArg >= 0 && fixtureArg + 1 < args.Length ? args[fixtureArg + 1] : "tests/fixtures";
Test("Shared JavaScript/C# conveyor scan fixture", () =>
{
    var p = ProjectStorage.Deserialize(File.ReadAllText(Path.Combine(fixtureDir, "conveyor.controlspace.json")));
    var r = Runtime(p);
    using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureDir, "conveyor-scans.json")));
    r.Run();
    int scans = 0;
    foreach (var scan in document.RootElement.EnumerateArray())
    {
        foreach (var input in scan.GetProperty("inputs").EnumerateObject()) r.SetInput(input.Name, input.Value.GetDouble());
        Check(r.Step(TimeSpan.FromMilliseconds(scan.GetProperty("period").GetDouble())), r.Fault ?? "Fixture scan failed");
        foreach (var expected in scan.GetProperty("expected").EnumerateObject()) Equal(expected.Value.GetDouble(), r.Read(expected.Name));
        scans++;
    }
    Check(scans > 0); Console.WriteLine($"  Verified {scans} shared scan vectors.");
});
Console.WriteLine($"C# regression tests: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
