using ControlSpace.Core;
using ControlSpace.Languages;
using ControlSpace.Simulation;
using ControlSpace.Engineering;
int count = 0;
void Check(string name, bool condition) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
var layout = new WorkbenchLayout();
Check("layout roundtrip", WorkbenchLayout.Parse(layout.Serialize()) == layout);
Check("malformed layout recovery", WorkbenchLayout.Parse("{") == layout);
Check("future layout recovery", WorkbenchLayout.Parse("{\"Version\":99}") == layout);
Check("oversized layout recovery", WorkbenchLayout.Parse(new string(' ', 9000)) == layout);
Check("null layout recovery", WorkbenchLayout.Parse("null") == layout);
Check("bounded project width", (layout with { ProjectWidth = -900 }).Normalize().ProjectWidth == 180);
Check("bounded task width", (layout with { TaskWidth = 90000 }).Normalize().TaskWidth == 460);
Check("bounded inspector height", (layout with { InspectorHeight = 90000 }).Normalize().InspectorHeight == 480);
Check("nonfinite widths", (layout with { ProjectWidth = double.NaN, TaskWidth = double.PositiveInfinity }).Normalize() == layout);
Check("invalid tabs normalized", (layout with { TaskCard = "unknown", InspectorTab = "unknown" }).Normalize() == layout);
Check("visibility and pin roundtrip", WorkbenchLayout.Parse((layout with { ProjectVisible = false, TasksVisible = false, InspectorVisible = false, AutoHideTasks = true }).Serialize()).AutoHideTasks);
var tabs = new EditorSession();
Check("empty cycle", tabs.Cycle(1) is null);
tabs.Open("a", "Main"); tabs.Open("b", "Tags"); tabs.Open("a", "Renamed");
Check("unique editors", tabs.Documents.Count == 2 && tabs.ActiveId == "a");
Check("title update", tabs.Documents[0].Title == "Renamed");
Check("forward cycle", tabs.Cycle(1) == "b");
Check("forward wrap", tabs.Cycle(1) == "a");
Check("backward wrap", tabs.Cycle(-1) == "b");
tabs.Move("b", -1); Check("reorder preserves active", tabs.Documents[0].Id == "b" && tabs.ActiveId == "b");
tabs.Move("b", int.MaxValue); Check("reorder bounded", tabs.Documents[^1].Id == "b");
tabs.Close("missing"); Check("unknown close no-op", tabs.Documents.Count == 2);
tabs.Close("a"); Check("inactive close preserves active", tabs.ActiveId == "b");
tabs.Open("c", "Screen"); tabs.Close("c"); Check("active close selects neighbor", tabs.ActiveId == "b");
tabs.Open("d", "Devices"); tabs.CloseOthers("b"); Check("close others", tabs.ActiveId == "b" && tabs.Documents.Count == 1);
tabs.CloseOthers("missing"); Check("unknown close others no-op", tabs.Documents.Count == 1);
tabs.Close("b"); Check("last editor closes to empty", tabs.Documents.Count == 0 && tabs.ActiveId is null);
tabs.Open("a", "Main"); tabs.Clear(); Check("clear editors", tabs.Documents.Count == 0 && tabs.ActiveId is null);
foreach (var newline in new[] { "\n", "\r\n", "\r" })
{
    string label = newline.Replace("\r", "CR").Replace("\n", "LF");
    var project = DemoProject.Create(); var block = project.Blocks.First(b => b.Language == BlockLanguage.SCL);
    project.Blocks.Clear(); project.Blocks.Add(block with { Source = "// editor comment" + newline + "Speed_Actual := 42;" });
    var compilation = ProjectCompiler.Compile(project);
    Check(label + " editor source compiles", compilation.Success);
    var plc = new VirtualPlc(compilation.Program!); plc.Step(TimeSpan.FromMilliseconds(100), true);
    Check(label + " line comment does not swallow program", plc.Read("Speed_Actual") == 42);
    project.Blocks[0] = block with { Source = "// editor comment" + newline + "Missing_Tag := 42;" };
    var diagnostic = ProjectCompiler.Compile(project).Diagnostics.First(d => d.Severity == Severity.Error);
    Check(label + " diagnostic points to second line", diagnostic.Line == 2 && diagnostic.Column == 1);
    Check(label + " rename preserves comment and offsets",
        SclParser.RenameSymbol("// Speed_Actual" + newline + "Speed_Actual := 42;", "Speed_Actual", "Actual") == "// Speed_Actual" + newline + "Actual := 42;");
}
Console.WriteLine($"Workbench state and editor conformance tests: {count} passed, 0 failed.");
