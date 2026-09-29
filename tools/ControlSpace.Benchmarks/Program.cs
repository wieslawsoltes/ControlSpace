using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ControlSpace.Core;
using ControlSpace.Engineering;
using ControlSpace.Languages;
using ControlSpace.Rendering.Skia;
using ControlSpace.Simulation;
using SkiaSharp;

// This same source is compiled against BOTH revisions. Only pre-existing public APIs are used.
var project = DemoProject.Create();
for (int i = project.Tags.Count; i < 10000; i++) project.Tags.Add(new("Tag_" + i, PlcType.Real, "%MD" + (10000 + i * 4), i % 100));
var workspace = new Workspace(project, 8);
var controller = new VirtualPlc(ProjectCompiler.Compile(project).Program!); controller.Run();
using var surface = SKSurface.Create(new SKImageInfo(1000, 700));
using var renderer = new EngineeringRenderer();
var wide = project.Blocks[0] with { Networks = [new("wide", "Long network", "Viewport benchmark", Enumerable.Range(0,16).Select(b => Enumerable.Range(0,64).Select(c => new Instruction($"i{b}_{c}", InstructionKind.Contact, "Start_PB")).ToList()).ToList(), new("out", InstructionKind.Coil, "Motor_Run"))] };
var layout = new LadderLayout(wide, 1000);
var screen = new HmiScreen("screen", "Dense HMI", 1000, 700, Enumerable.Range(0,500).Select(i => new HmiObject("h"+i, HmiKind.Tank, "Level", "Tag_"+(9500+i), i%25*40, i/25*35, 35, 30)).ToList());
var snapshot = controller.Snapshot();
var results = new List<object>(); long sink = 0;
void Measure(string name, int iterations, Action action)
{
    for(int i=0;i<3;i++) action();
    var times = new double[7]; var allocations = new double[7];
    for(int sample=0;sample<7;sample++)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long bytes=GC.GetAllocatedBytesForCurrentThread(), start=Stopwatch.GetTimestamp();
        for(int i=0;i<iterations;i++) action();
        times[sample]=Stopwatch.GetElapsedTime(start).TotalMilliseconds/iterations;
        allocations[sample]=(GC.GetAllocatedBytesForCurrentThread()-bytes)/(double)iterations;
    }
    Array.Sort(times); Array.Sort(allocations);
    results.Add(new { name, milliseconds=times[3], allocatedBytes=allocations[3], minimumMilliseconds=times[0], maximumMilliseconds=times[^1], iterations, batches=7 });
    Console.WriteLine($"{name}: {times[3]:0.000000} ms/op, {allocations[3]:0} allocated bytes/op");
}
Measure("dirty_query_10000_tags", 25, () => { if(workspace.IsDirty) sink++; });
Measure("noop_edit_10000_tags", 5, () => workspace.Edit("no-op", _=>{}));
Measure("validate_10000_tags", 5, () => sink+=ProjectValidator.Validate(project).Count);
int edit=0;
Measure("comment_edit_10000_tags", 3, () => workspace.Edit("comment", p=>p.Tags[0]=p.Tags[0] with { Comment="edit "+edit++ }));
Measure("scan_10000_tags_trace_on", 20, () => { controller.Step(TimeSpan.FromMilliseconds(100)); sink+=controller.Cycle; });
Measure("ladder_64x16_viewport", 10, () => { renderer.Ladder(surface.Canvas,1000,700,wide,project.Tags,snapshot,horizontal:3000,layout:layout); });
Measure("hmi_500_objects_10000_tags", 5, () => { renderer.Hmi(surface.Canvas,1000,700,screen,project.Tags,snapshot,runtime:true); });
var report=new { revision=args.ElementAtOrDefault(0)??"unknown", runtime=RuntimeInformation.FrameworkDescription, os=RuntimeInformation.OSDescription, architecture=RuntimeInformation.ProcessArchitecture.ToString(), processors=Environment.ProcessorCount, scenario="Release native .NET; CPU raster Skia; warm median of seven batches; not a GPU or end-to-end UI latency benchmark", results, sink };
string output=args.ElementAtOrDefault(1)??"benchmark.json";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
