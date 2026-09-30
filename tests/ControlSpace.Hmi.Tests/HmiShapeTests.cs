using ControlSpace.Core;
using ControlSpace.Engineering;
using ControlSpace.Rendering.Skia;
using ControlSpace.Storage;
using SkiaSharp;

internal static class HmiShapeTests
{
    public static int Run()
    {
        int count = 0;
        void Check(bool condition) { if (!condition) throw new Exception("HMI shape assertion failed"); }
        void Test(string name, Action action) { action(); count++; Console.WriteLine("PASS " + name); }
        void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; } throw new Exception("Expected shape validation failure"); }
        HmiObject Shape(HmiKind kind) => new("shape", kind, "Shape", "", 40, 40, 120, 80, "#005A9C");
        ControlProject Project(HmiKind kind)
        {
            var p = DemoProject.Create(); p.Screens.Clear(); p.Screens.Add(new("shapes", "Shapes", 952, 652, [Shape(kind)])); return p;
        }
        foreach (var kind in new[] { HmiKind.Rectangle, HmiKind.Ellipse, HmiKind.Line })
        {
            Test(kind + " JSON and clipboard roundtrip", () =>
            {
                var p = Project(kind); var restored = ProjectStorage.Deserialize(ProjectStorage.Serialize(p));
                Check(restored.Screens[0].Objects[0] == p.Screens[0].Objects[0]);
                var w = new Workspace(restored); var e = new HmiEditor(w);
                string data = HmiEditor.Copy(w.Project, "shapes", ["shape"]);
                var ids = e.Paste(w.Project, "shapes", data);
                Check(ids.Count == 1 && ids[0] != "shape" && w.Project.Screens[0].Objects[1].Kind == kind);
                w.Undo(); Check(w.Project.Screens[0].Objects.Count == 1);
            });
            Test(kind + " bound shape rejected atomically", () =>
            {
                var w = new Workspace(Project(kind)); var before = w.Project;
                Reject(() => new HmiEditor(w).UpdateObject(before, "shapes", Shape(kind) with { Tag = "Start_PB" }));
                Check(ReferenceEquals(w.Project, before) && !w.CanUndo);
                var p = Project(kind); p.Screens[0].Objects[0] = Shape(kind) with { Tag = "Start_PB" };
                Check(ProjectValidator.Validate(p).Any(d => d.Code == "CS046"));
            });
            Test(kind + " grouped resize and order remain undoable", () =>
            {
                var w = new Workspace(Project(kind)); var before = w.Project;
                var gesture = new HmiTransformSession(before.Screens[0], ["shape"], new(160, 120), "se");
                new HmiEditor(w).Transform(before, "shapes", gesture.Preview(new(190, 140), false));
                Check(w.Project.Screens[0].Objects[0].Width == 150 && w.Project.Screens[0].Objects[0].Height == 100);
                w.Undo(); Check(ProjectSnapshot.ContentEquals(w.Project, before));
            });
            Test(kind + " designer and public renderer draw the same shape", () =>
            {
                using var a = SKSurface.Create(new SKImageInfo(1000, 700));
                using var b = SKSurface.Create(new SKImageInfo(1000, 700));
                using var renderer = new EngineeringRenderer(); var p = Project(kind); var screen = p.Screens[0];
                int saves = a.Canvas.SaveCount;
                renderer.HmiDesigner(a.Canvas, 1000, 700, screen, p.Tags, null, new(1, 24, 24), [], runtime: true);
                renderer.Hmi(b.Canvas, 1000, 700, screen, p.Tags, null, runtime: true);
                Check(saves == a.Canvas.SaveCount && saves == b.Canvas.SaveCount);
                using var ia = a.Snapshot(); using var ib = b.Snapshot(); using var pa = ia.PeekPixels(); using var pb = ib.PeekPixels();
                bool Colored(SKColor c) => c.Blue > c.Red + 40;
                Check(Colored(pa.GetPixelColor(124, 104)) && Colored(pb.GetPixelColor(124, 104)));
                Check(!Colored(pa.GetPixelColor(50, 50)) && !Colored(pb.GetPixelColor(50, 50)));
                bool filledCorner = kind == HmiKind.Rectangle;
                Check(Colored(pa.GetPixelColor(68, 76)) == filledCorner && Colored(pb.GetPixelColor(68, 76)) == filledCorner);
            });
        }
        Test("ellipse transparent corners are not selection targets", () =>
        {
            var o = Shape(HmiKind.Ellipse);
            Check(HmiShapeGeometry.Contains(o, new(100, 80)));
            Check(HmiShapeGeometry.Contains(o, new(100, 40)));
            Check(!HmiShapeGeometry.Contains(o, new(44, 44)) && !HmiShapeGeometry.Contains(o, new(38, 80), 100));
        });
        Test("line selects its stroke rather than its bounding rectangle", () =>
        {
            var o = Shape(HmiKind.Line);
            Check(HmiShapeGeometry.Contains(o, new(100, 80)));
            Check(HmiShapeGeometry.Contains(o, new(100, 83), 4));
            Check(!HmiShapeGeometry.Contains(o, new(100, 110), 4));
            Check(!HmiShapeGeometry.Contains(o, new(20, 30), 4));
        });
        Test("thin horizontal and vertical segments remain selectable", () =>
        {
            var h = Shape(HmiKind.Line) with { Height = 1 };
            var v = Shape(HmiKind.Line) with { Width = 1 };
            Check(HmiShapeGeometry.Contains(h, new(100, 40.5)) && HmiShapeGeometry.Contains(v, new(40.5, 80)));
            var dot = Shape(HmiKind.Line) with { Width = .5, Height = .5 };
            Check(HmiShapeGeometry.Contains(dot, new(40.25, 40.25)));
        });
        Test("line tolerance uses constant view pixels after zoom conversion", () =>
        {
            var line = Shape(HmiKind.Line) with { Height = 1 };
            foreach (double zoom in new[] { .1, .5, 1, 4 })
            {
                Check(HmiShapeGeometry.Contains(line, new(100, 40.5 + 3 / zoom), 4 / zoom));
                Check(!HmiShapeGeometry.Contains(line, new(100, 40.5 + 8 / zoom), 4 / zoom));
            }
        });
        Test("invalid geometry and nonfinite pointers never hit", () =>
        {
            var o = Shape(HmiKind.Rectangle);
            Check(!HmiShapeGeometry.Contains(o, new(double.NaN, 50)));
            Check(!HmiShapeGeometry.Contains(o with { Width = 0 }, new(50, 50)));
            Check(!HmiShapeGeometry.Contains(o with { X = double.PositiveInfinity }, new(50, 50)));
            Check(!HmiShapeGeometry.Contains(o with { Kind = (HmiKind)100 }, new(50, 50)));
        });
        Test("existing rectangular control hit geometry remains unchanged", () =>
        {
            foreach (var kind in new[] { HmiKind.Label, HmiKind.Button, HmiKind.Lamp, HmiKind.Numeric, HmiKind.Gauge, HmiKind.Tank })
            {
                var o = Shape(kind); Check(HmiShapeGeometry.Contains(o, new(40, 40)) && !HmiShapeGeometry.Contains(o, new(39, 40), 10));
            }
        });
        Test("large shape screen still culls offscreen drawing", () =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(1000, 700)); using var r = new EngineeringRenderer();
            var objects = Enumerable.Range(0, 10000).Select(i => Shape(i % 2 == 0 ? HmiKind.Ellipse : HmiKind.Rectangle) with { Id = "o" + i, X = i == 0 ? 40 : 5000 }).ToList();
            var screen = new HmiScreen("shapes", "Large", 8192, 652, objects);
            var result = r.HmiDesigner(surface.Canvas, 1000, 700, screen, [], null, new(1, 24, 24), [], runtime: true);
            Check(r.LastHmiDrawnObjects == 1 && result.Hits.Count == 1);
        });
        return count;
    }
}
