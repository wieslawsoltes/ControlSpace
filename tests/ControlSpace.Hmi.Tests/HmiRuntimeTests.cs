using System.Globalization;
using ControlSpace.Core;
using ControlSpace.Engineering;
using ControlSpace.Languages;
using ControlSpace.Simulation;
using ControlSpace.Storage;
using ControlSpace.Rendering.Skia;
using SkiaSharp;

internal static class HmiRuntimeTests
{
    public static int Run()
    {
        int count = 0;
        void Check(bool value) { if (!value) throw new Exception("HMI runtime assertion failed"); }
        void Test(string name, Action action) { action(); count++; Console.WriteLine("PASS " + name); }
        void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; } catch (System.IO.InvalidDataException) { return; } catch (System.Text.Json.JsonException) { return; } throw new Exception("Expected rejection"); }
        ControlProject Project()
        {
            var p = DemoProject.Create(); p.Blocks.Clear();
            p.Tags.Add(new("OperatorInput", PlcType.Real, "%ID100", 10));
            p.Screens.Clear();
            p.Screens.Add(new("first", "First", 960, 540,
            [new HmiObject("next", HmiKind.Button, "Next", "", 20, 20, 160, 60) { Button = new(HmiButtonAction.ActivateScreen, "second") },
             new HmiObject("number", HmiKind.Numeric, "Setpoint", "Speed_Setpoint", 200, 20, 160, 80) { Numeric = new(true, -50, 150, 2, "rpm") }]));
            p.Screens.Add(new("second", "Second", 960, 540,
            [new HmiObject("back", HmiKind.Button, "Back", "", 20, 20, 160, 60) { Button = new(HmiButtonAction.PreviousScreen) }]));
            return p;
        }
        HmiObject Number(ControlProject p) => p.Screens[0].Objects[1];
        VirtualPlc Cpu(ControlProject p) => new(ProjectCompiler.Compile(p).Program!);
        HmiNumericInputSession Session(ControlProject p, VirtualPlc c) => new(p, c, "first", "number");
        Test("legacy projects omit optional runtime fields and remain readable", () =>
        {
            var p = DemoProject.Create(); var json = ProjectStorage.Serialize(p);
            Check(!json.Contains("\"numeric\":") && !json.Contains("\"button\":"));
            Check(ProjectSnapshot.ContentEquals(p, ProjectStorage.Deserialize(json)));
        });
        Test("runtime options persist with JSON and structural equality", () =>
        {
            var p = Project(); var q = ProjectStorage.Deserialize(ProjectStorage.Serialize(p));
            Check(ProjectSnapshot.ContentEquals(p, q)); q.Screens[0].Objects[1] = Number(q) with { Numeric = new(true, -20, 80, 1, "bar") };
            Check(!ProjectSnapshot.ContentEquals(p, q));
        });
        Test("screen links survive rename and reject referenced deletion", () =>
        {
            var w = new Workspace(Project()); var e = new HmiEditor(w); e.UpdateScreen(w.Project, "second", "Renamed", 960, 540);
            Check(w.Project.Screens[0].Objects[0].Button!.TargetScreenId == "second");
            var before = w.Project; Reject(() => e.DeleteScreen(before, "second")); Check(ReferenceEquals(w.Project, before));
        });
        Test("screen self-links retarget to a deep duplicate", () =>
        {
            var p = Project(); p.Screens[0].Objects[0] = p.Screens[0].Objects[0] with { Button = new(HmiButtonAction.ActivateScreen, "first") };
            var w = new Workspace(p); var e = new HmiEditor(w); string id = e.DuplicateScreen(w.Project, "first");
            Check(HmiEditor.Screen(w.Project, id).Objects[0].Button!.TargetScreenId == id);
            Check(w.Project.Screens[0].Objects[0].Button!.TargetScreenId == "first");
            e.DeleteScreen(w.Project, id); Check(w.Project.Screens.Count == 2);
        });
        Test("navigation clipboard preserves target and rejects a missing destination", () =>
        {
            var w = new Workspace(Project()); string data = HmiEditor.Copy(w.Project, "first", ["next"]);
            new HmiEditor(w).Paste(w.Project, "second", data);
            Check(w.Project.Screens[1].Objects[^1].Button!.TargetScreenId == "second");
            var p = Project(); p.Screens.RemoveAt(1); p.Screens[0].Objects.RemoveAt(0); var target = new Workspace(p); var before = target.Project;
            Reject(() => new HmiEditor(target).Paste(before, "first", data)); Check(ReferenceEquals(before, target.Project));
        });
        foreach (var malformed in new HmiButtonBehavior[] { new((HmiButtonAction)99), new(HmiButtonAction.ActivateScreen, "missing"), new(HmiButtonAction.PreviousScreen, "first"), new(HmiButtonAction.None, null!) })
            Test("invalid button action rejected " + malformed.Action, () =>
            {
                var p = Project(); p.Screens[0].Objects[0] = p.Screens[0].Objects[0] with { Button = malformed };
                Check(ProjectValidator.Validate(p).Any(d => d.Code == "CS047")); Reject(() => ProjectStorage.Deserialize(ProjectStorage.Serialize(p)));
            });
        Test("navigation tag and behavior on incompatible object rejected", () =>
        {
            var p = Project(); p.Screens[0].Objects[0] = p.Screens[0].Objects[0] with { Tag = "Start_PB" };
            Check(ProjectValidator.Validate(p).Any(d => d.Code == "CS047"));
            p.Screens[0].Objects[0] = p.Screens[0].Objects[0] with { Tag = "", Kind = HmiKind.Label };
            Check(ProjectValidator.Validate(p).Any(d => d.Code == "CS047"));
        });
        foreach (var options in new HmiNumericOptions[] { new(true, 10, 10), new(true, 20, 10), new(true, 0, 100, -1), new(true, 0, 100, 10), new(true, 0, 100, 2, "\nbar"), new(true, 0, 100, 2, new string('x', 25)), new(true, double.NaN), new(true, 0, double.PositiveInfinity) })
            Test("invalid numeric options rejected " + options, () => { var p = Project(); p.Screens[0].Objects[1] = Number(p) with { Numeric = options }; Check(ProjectValidator.Validate(p).Any(d => d.Code == "CS047")); });
        Test("editable numeric output binding and editable gauge rejected", () =>
        {
            var p = Project(); p.Screens[0].Objects[1] = Number(p) with { Tag = "Motor_Run" }; Check(ProjectValidator.Validate(p).Any(d => d.Code == "CS047"));
            p.Screens[0].Objects[1] = Number(p) with { Tag = "Speed_Setpoint", Kind = HmiKind.Gauge }; Check(ProjectValidator.Validate(p).Any(d => d.Code == "CS047"));
        });
        Test("runtime numeric input leaves project and history unchanged", () =>
        {
            var w = new Workspace(Project()); var c = Cpu(w.Project); c.Run(); var before = ProjectStorage.Serialize(w.Project); long v = c.VisualVersion;
            Check(Session(w.Project, c).Commit(w.Project, c, "42.25") == 42.25 && c.Read("Speed_Setpoint") == 42.25 && c.VisualVersion > v);
            Check(!w.CanUndo && !w.IsDirty && ProjectStorage.Serialize(w.Project) == before && c.Forces.Count == 0);
        });
        foreach (string value in new[] { "", "NaN", "Infinity", "151", "-51", "1,23", "42.001", new string('1', 129) })
            Test("invalid operator input leaves image unchanged " + value, () =>
            {
                var p = Project(); var c = Cpu(p); double old = c.Read("Speed_Setpoint"); long version = c.VisualVersion;
                Reject(() => Session(p, c).Commit(p, c, value)); Check(c.Read("Speed_Setpoint") == old && c.VisualVersion == version && c.Forces.Count == 0);
            });
        Test("valid endpoints exponent and decimal precision accepted", () =>
        {
            var p = Project(); var c = Cpu(p); foreach (string t in new[] { "-50", "150", "4.225e1", "-0.25" }) Session(p, c).Commit(p, c, t);
            Check(c.Read("Speed_Setpoint") == -.25);
        });
        Test("input-image value survives scan and marker write is not forced", () =>
        {
            var p = Project(); var c = Cpu(p); c.SetOperatorValue("OperatorInput", 37.5); c.SetOperatorValue("Speed_Setpoint", 12); c.Run(); c.Step(TimeSpan.FromMilliseconds(100));
            Check(c.Read("OperatorInput") == 37.5 && c.Read("Speed_Setpoint") == 12 && c.Forces.Count == 0);
        });
        Test("operator writes reject BOOL outputs forced tags and repeated completion", () =>
        {
            var p = Project(); var c = Cpu(p); Reject(() => c.SetOperatorValue("Start_PB", 1)); Reject(() => c.SetOperatorValue("Motor_Run", 1));
            c.Force("Speed_Setpoint", 12); Reject(() => Session(p, c).Commit(p, c, "30")); Check(c.Forces.Count == 1); c.ReleaseAll();
            var input = Session(p, c); input.Commit(p, c, "32"); Reject(() => input.Commit(p, c, "33"));
        });
        Test("fault invalidates pending numeric entry and rejects new operator writes", () =>
        {
            var p = Project(); p.Blocks.Add(new("fault-block", "Fault", 8, BlockLanguage.SCL, true, [], "Speed_Actual := 1 / 0;"));
            var c = Cpu(p); c.Run(); var entry = Session(p, c); double before = c.Read("Speed_Setpoint");
            Check(!c.Step(TimeSpan.FromMilliseconds(100)) && c.State == ControllerState.Faulted);
            Reject(() => entry.Commit(p, c, "33")); Reject(() => c.SetOperatorValue("Speed_Setpoint", 33));
            Check(c.Read("Speed_Setpoint") == before && c.Forces.Count == 0);
        });
        Test("program writer can replace a one-shot marker value on the next scan", () =>
        {
            var p = Project(); p.Blocks.Add(new("writer", "Writer", 8, BlockLanguage.SCL, true, [], "Speed_Setpoint := 7;"));
            var c = Cpu(p); c.Run(); Session(p, c).Commit(p, c, "33"); Check(c.Read("Speed_Setpoint") == 33);
            c.Step(TimeSpan.FromMilliseconds(100)); Check(c.Read("Speed_Setpoint") == 7 && c.Forces.Count == 0);
        });
        Test("operator numeric output-image write explicitly rejected", () =>
        {
            var p = Project(); p.Tags.Add(new("OutputNumber", PlcType.Real, "%QD100", 0)); var c = Cpu(p); Reject(() => c.SetOperatorValue("OutputNumber", 1)); Check(c.Read("OutputNumber") == 0);
        });
        Test("invalid input can be corrected using the same session", () =>
        {
            var p = Project(); var c = Cpu(p); var s = Session(p, c); Reject(() => s.Commit(p, c, "999")); Check(s.Commit(p, c, "55") == 55);
        });
        Test("different content with matching project revision rejects session creation", () =>
        {
            var p = Project(); var c = Cpu(p); var changed = ProjectSnapshot.Clone(p);
            changed.Screens[0].Objects[1] = Number(changed) with { Numeric = new(true, -100, 200, 1, "bar") };
            Reject(() => Session(changed, c)); Check(c.Read("Speed_Setpoint") == 65);
        });
        Test("ordinary scans keep an operator session valid", () =>
        {
            var p = Project(); var c = Cpu(p); c.Run(); var s = Session(p, c); for (int i = 0; i < 10; i++) c.Step(TimeSpan.FromMilliseconds(100)); Check(s.Commit(p, c, "25") == 25);
        });
        foreach (string transition in new[] { "stop", "reset", "run", "restart", "replacement", "project" })
            Test("stale input rejects " + transition, () =>
            {
                var p = Project(); var c = Cpu(p); if (transition != "run") c.Run(); var s = Session(p, c); VirtualPlc other = c; var current = p;
                switch (transition) { case "stop": c.Stop(); break; case "reset": c.Reset(); break; case "run": c.Run(); break; case "restart": c.Stop(); c.Run(); break; case "replacement": other = Cpu(p); break; case "project": current = ProjectSnapshot.Clone(p); break; }
                Reject(() => s.Commit(current, other, "55")); Check(c.Read("Speed_Setpoint") != 55 && other.Read("Speed_Setpoint") != 55);
            });
        Test("numeric options retained by clipboard undo and tag rename", () =>
        {
            var w = new Workspace(Project()); var e = new HmiEditor(w); var options = Number(w.Project).Numeric;
            var ids = e.Paste(w.Project, "second", HmiEditor.Copy(w.Project, "first", ["number"]));
            Check(w.Project.Screens[1].Objects.Last().Numeric == options);
            w.RenameTag("Speed_Setpoint", "Setpoint"); Check(Number(w.Project).Tag == "Setpoint" && Number(w.Project).Numeric == options);
            w.Undo(); Check(Number(w.Project).Tag == "Speed_Setpoint");
        });
        Test("numeric formatting and scales use configured limits", () =>
        {
            var o = Number(Project()); Check(HmiRuntimeOptions.Format(o, 12.5) == "12.50 rpm");
            Check(HmiRuntimeOptions.Fraction(o, -50) == 0 && HmiRuntimeOptions.Fraction(o, 50) == .5 && HmiRuntimeOptions.Fraction(o, 151) == 1);
            Check(HmiRuntimeOptions.IsOutOfRange(o, 151) && !HmiRuntimeOptions.IsOutOfRange(o, 150));
            var culture = CultureInfo.CurrentCulture; try { CultureInfo.CurrentCulture = new("pl-PL"); Check(HmiRuntimeOptions.Format(o, 12.5) == "12.50 rpm"); } finally { CultureInfo.CurrentCulture = culture; }
        });
        Test("navigation history is bounded and ignores missing entries", () =>
        {
            var p = Project(); var history = new HmiNavigationSession(); string current = "first";
            for (int i = 0; i < 100; i++) current = history.Activate(p, current, current == "first" ? "second" : "first");
            Check(history.Count == 64); Check(history.Back(p, current) == "second");
            p.Screens.RemoveAt(0); Check(history.Back(p, "second") == "second");
            history.Clear(); Check(history.Count == 0);
        });
        Test("navigation rejects invalid target before history mutation", () =>
        {
            var p = Project(); var h = new HmiNavigationSession(); Reject(() => h.Activate(p, "first", "missing")); Check(h.Count == 0);
            Check(h.Activate(p, "first", "first") == "first" && h.Count == 0);
            h.Activate(p, "first", "second"); Check(h.Back(p with { Id = "new-project" }, "second") == "second" && h.Count == 0);
        });
        Test("numeric precision obeys bound integer type", () =>
        {
            var p = Project(); p.Tags.Add(new("IntInput", PlcType.Int, "%IW200", 0)); p.Screens[0].Objects[1] = Number(p) with { Tag = "IntInput" }; var c = Cpu(p);
            Reject(() => Session(p, c).Commit(p, c, "1.5")); Check(Session(p, c).Commit(p, c, "32") == 32);
        });
        Test("numeric scale rendering remains clipped and cache stable", () =>
        {
            var p = Project(); var o = Number(p) with { Kind = HmiKind.Gauge, Numeric = new(false, -50, 150, 1, "bar") };
            var screen = p.Screens[0] with { Objects = [o] }; using var surface = SKSurface.Create(new SKImageInfo(1000, 700)); using var r = new EngineeringRenderer();
            int saves = surface.Canvas.SaveCount; r.HmiDesigner(surface.Canvas, 1000, 700, screen, p.Tags, null, new(1, 24, 24), [], runtime: true);
            long runs = r.TextRunCreations; r.HmiDesigner(surface.Canvas, 1000, 700, screen, p.Tags, null, new(1, 24, 24), [], runtime: true);
            Check(surface.Canvas.SaveCount == saves && r.TextRunCreations == runs && r.LastHmiDrawnObjects == 1);
        });
        return count;
    }
}
