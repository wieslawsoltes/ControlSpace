using ControlSpace.Core;
using ControlSpace.Engineering;
using ControlSpace.Languages;
using ControlSpace.Rendering.Skia;
using ControlSpace.Simulation;
using SkiaSharp;

int count=0;
void Check(bool condition) { if(!condition) throw new Exception("Assertion failed"); }
void Test(string name,Action action) { action(); count++; Console.WriteLine("PASS "+name); }
ControlProject Large() { var p=DemoProject.Create(); for(int i=p.Tags.Count;i<10000;i++) p.Tags.Add(new("Tag_"+i,PlcType.Real,"%MD"+(10000+i*4))); return p; }
VirtualPlc Cpu() => new(ProjectCompiler.Compile(DemoProject.Create()).Program!);
Test("structural snapshots equal without shared collections",()=> { var p=DemoProject.Create(); var q=ProjectSnapshot.Clone(p); Check(ProjectSnapshot.ContentEquals(p,q)); Check(!ReferenceEquals(p.Tags,q.Tags)); Check(!ProjectSnapshot.ContentEquals(p,p with{Revision=1})); Check(!ProjectSnapshot.ContentEquals(p,null)); });
foreach(string area in new[]{"tag","source","instruction","network","module","link","screen","hmi"}) Test("structural comparison sees "+area,()=> {
 var p=DemoProject.Create(); var q=ProjectSnapshot.Clone(p);
 switch(area) {
 case "tag": q.Tags[0]=q.Tags[0] with{Comment="changed"}; break;
 case "source": q.Blocks[1]=q.Blocks[1] with{Source="changed"}; break;
 case "instruction": q.Blocks[0].Networks[0].Branches[0][0]=q.Blocks[0].Networks[0].Branches[0][0] with{Tag="changed"}; break;
 case "network": q.Blocks[0].Networks[0]=q.Blocks[0].Networks[0] with{Comment="changed"}; break;
 case "module": q.Devices[0].Modules.Add("changed"); break;
 case "link": q.Links[0]=q.Links[0] with{Subnet="changed"}; break;
 case "screen": q.Screens[0]=q.Screens[0] with{Name="changed"}; break;
 case "hmi": q.Screens[0].Objects[0]=q.Screens[0].Objects[0] with{Text="changed"}; break; }
 Check(!ProjectSnapshot.ContentEquals(p,q)); });
Test("dirty queries allocate no project images",()=> { var w=new Workspace(Large()); for(int i=0;i<100;i++) Check(!w.IsDirty); long b=GC.GetAllocatedBytesForCurrentThread(); bool any=false; for(int i=0;i<10000;i++) any|=w.IsDirty; long used=GC.GetAllocatedBytesForCurrentThread()-b; Check(!any && used<1024); });
Test("dirty state follows undo redo and no-op",()=> { var w=new Workspace(DemoProject.Create()); w.Edit("noop",_=>{}); Check(!w.IsDirty&&!w.CanUndo); w.Edit("change",p=>p.Tags[0]=p.Tags[0] with{Comment="new"}); Check(w.IsDirty); w.Undo(); Check(!w.IsDirty); w.Redo(); Check(w.IsDirty); w.MarkSaved(); Check(!w.IsDirty); });
Test("late save completion does not mark newer edits saved",()=> { var w=new Workspace(DemoProject.Create()); var exported=w.Project; w.Edit("later",p=>p.Tags[0]=p.Tags[0] with{Comment="later"}); w.MarkSaved(exported); Check(w.IsDirty); w.Undo(); Check(!w.IsDirty); });
Test("saved checkpoint is detached",()=> { var w=new Workspace(DemoProject.Create()); var exported=ProjectSnapshot.Clone(w.Project); w.MarkSaved(exported); exported.Tags[0]=exported.Tags[0] with{Comment="external"}; w.Edit("x",p=>p.Tags[0]=p.Tags[0] with{Comment="x"}); w.Undo(); Check(!w.IsDirty); });
Test("address interval validation matches bit occupancy oracle",()=> { var random=new Random(752); for(int pass=0;pass<100;pass++) { var p=DemoProject.Create(); p.Tags.Clear(); p.Blocks.Clear(); p.Screens.Clear(); var occupied=new HashSet<(char,int)>(); bool overlap=false; for(int i=0;i<80;i++) { char area="IQM"[random.Next(3)]; bool bit=random.Next(2)==0; int start=random.Next(200); int width=bit?1:32; int first=bit?start:start/32*32; string address=bit?$"%{area}{first/8}.{first%8}":$"%{area}D{first/8}"; p.Tags.Add(new("T"+i,bit?PlcType.Bool:PlcType.Real,address)); for(int b=first;b<first+width;b++) if(!occupied.Add((area,b))) overlap=true; } Check(ProjectValidator.Validate(p).Any(d=>d.Code=="CS014")==overlap); } });
Test("large disjoint addresses validate",()=>Check(!ProjectValidator.Validate(Large()).Any(d=>d.Severity==Severity.Error)));
Test("live controller view follows scans without copies",()=> { var c=Cpu(); var view=c.ReadView; int input=c.FindSlot("Start_PB"); c.SetInput("Start_PB",1); Check(view.ReadValue(input)==1); c.Run(); c.Step(TimeSpan.FromMilliseconds(100)); Check(view.Cycle==1&&view.ReadValue(c.FindSlot("Motor_Run"))==1); Check(c.SnapshotCopies==0); });
Test("explicit historical snapshots remain detached",()=> { var c=Cpu(); var old=c.Snapshot(); c.SetInput("Start_PB",1); c.Run(); for(int i=0;i<8;i++) c.Step(TimeSpan.FromMilliseconds(100)); Check(old.Cycle==0 && old.Values[c.FindSlot("Start_PB")]==0 && old.Flow.Count==0 && c.SnapshotCopies==1); });
Test("trace samples never alias reusable scan buffers",()=> { var c=Cpu(); c.SetInput("Start_PB",1); c.Run(); c.Step(TimeSpan.FromMilliseconds(100)); var first=c.Trace[0]; c.SetInput("Stop_PB",1); c.Step(TimeSpan.FromMilliseconds(100)); c.Step(TimeSpan.FromMilliseconds(100)); Check(first.Values[c.FindSlot("Motor_Run")]==1&&first.Flow["start"]); Check(c.Read("Motor_Run")==0); });
Test("static scan image does not advance visual version",()=> { var c=Cpu(); c.Run(); c.Step(TimeSpan.FromMilliseconds(100)); long v=c.VisualVersion; for(int i=0;i<50;i++) c.Step(TimeSpan.FromMilliseconds(100)); Check(c.VisualVersion==v && c.Cycle==51); c.SetInput("Start_PB",1); Check(c.VisualVersion>v); });
Test("trace can be disabled without changing execution",()=> { var c=Cpu(); c.TraceEnabled=false;c.Run();c.Step(TimeSpan.FromMilliseconds(100));Check(c.Cycle==1&&c.Trace.Count==0); });
ScanSnapshot Sample(long cycle,int values=1) => new(cycle,cycle*100,0,ControllerState.Running,new double[values],new Dictionary<string,bool>(),null);
Test("trace payload budget evicts oldest samples",()=> { var b=new TraceBuffer(20,500);for(int i=0;i<10;i++)b.Add(Sample(i));Check(b.Count==3&&b[0].Cycle==7&&b[2].Cycle==9&&b.EstimatedRetainedBytes<=500&&b.DroppedSamples==7); });
Test("trace oversized sample preserves earlier history",()=> { var b=new TraceBuffer(20,500);b.Add(Sample(1));b.Add(Sample(2,100));Check(b.Count==1&&b[0].Cycle==1&&b.DroppedSamples==1); });
Test("trace ring wraps and clears",()=> { var b=new TraceBuffer(3);for(int i=0;i<10;i++)b.Add(Sample(i));Check(b.Read().Select(s=>s.Cycle).SequenceEqual(new long[]{7,8,9}));b.Clear();Check(b.Count==0&&b.EstimatedRetainedBytes==0&&b.DroppedSamples==0); });
Test("trace indexed paints allocate no history arrays",()=> { var b=new TraceBuffer(1000);for(int i=0;i<1000;i++)b.Add(Sample(i)); long bytes=GC.GetAllocatedBytesForCurrentThread(),sum=0;for(int i=0;i<1000;i++)sum+=b[i].Cycle;Check(GC.GetAllocatedBytesForCurrentThread()-bytes<128&&sum==499500); });
Test("horizontal culling includes every intersecting contact",()=> { for(int offset=0;offset<9500;offset+=79) { var range=LadderLayout.VisibleContacts(64,offset,1000);Check(range.End-range.First<=11);for(int i=0;i<64;i++){var rect=LadderLayout.Contact(i,94);if(rect.X+rect.Width>=offset&&rect.X<=offset+1000)Check(i>=range.First&&i<range.End);} } });
Test("horizontal culling handles empty and nonfinite input",()=> { Check(LadderLayout.VisibleContacts(0,0,100)==(0,0));Check(LadderLayout.VisibleContacts(64,double.NaN,100)==(0,0)); });
using var surface=SKSurface.Create(new SKImageInfo(1000,700));
Test("long ladder draws only viewport instructions",()=> { var p=DemoProject.Create();var b=p.Blocks[0] with{Networks=[new("wide","Long","",Enumerable.Range(0,16).Select(y=>Enumerable.Range(0,64).Select(x=>new Instruction($"i{y}_{x}",InstructionKind.Contact,"Start_PB")).ToList()).ToList(),new("out",InstructionKind.Coil,"Motor_Run"))]};using var r=new EngineeringRenderer();var layout=new LadderLayout(b,1000);var result=r.Ladder(surface.Canvas,1000,700,b,p.Tags,null,horizontal:3000,scroll:100,layout:layout);Check(r.LastDrawnInstructions<=150&&r.LastDrawnNetworks==1&&result.Hits.Any(h=>h.Id=="wide")); });
Test("warm ladder reuses text runs",()=> { var p=DemoProject.Create();using var r=new EngineeringRenderer();var l=new LadderLayout(p.Blocks[0],1000);r.Ladder(surface.Canvas,1000,700,p.Blocks[0],p.Tags,null,layout:l);long created=r.TextRunCreations;r.Ladder(surface.Canvas,1000,700,p.Blocks[0],p.Tags,null,layout:l);Check(r.TextRunCreations==created&&created>0); });
Test("text cache has fixed upper bound",()=> { var p=DemoProject.Create();using var r=new EngineeringRenderer();var objects=Enumerable.Range(0,700).Select(i=>new HmiObject("i"+i,HmiKind.Label,"Label "+i,"",1,1,100,20)).ToList();r.Hmi(surface.Canvas,1000,700,new("s","S",1000,700,objects),p.Tags,null,runtime:true);Check(r.CachedTextRunCount<=512&&r.TextRunCreations>=700); });
Test("HMI tag cache follows new snapshots and names case-insensitively",()=> { using var r=new EngineeringRenderer();var a=new List<PlcTag>{new("Value",PlcType.Bool,"%M0.0",1)};var screen=new HmiScreen("s","S",1000,700,[new("lamp",HmiKind.Lamp,"Lamp","value",40,40,140,80)]);r.Hmi(surface.Canvas,1000,700,screen,a,null,runtime:true);using var first=surface.Snapshot();var b=new List<PlcTag>{a[0] with{InitialValue=0}};r.Hmi(surface.Canvas,1000,700,screen,b,null,runtime:true);using var second=surface.Snapshot();using var p1=first.PeekPixels();using var p2=second.PeekPixels();Check(p1.GetPixelColor(84,100)!=p2.GetPixelColor(84,100)); });
Console.WriteLine($"Performance/correctness regression groups: {count} passed, 0 failed.");
