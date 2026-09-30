using System.Globalization;
using System.Text.Json;
using ControlSpace.Core;
using ControlSpace.Engineering;
using ControlSpace.Languages;
using ControlSpace.Rendering.Skia;
using ControlSpace.Simulation;
using ControlSpace.Storage;
using SkiaSharp;

internal static class HmiRuntimeTests
{
    public static int Run()
    {
        int count = 0;
        void Check(bool condition) { if (!condition) throw new Exception("HMI runtime assertion failed"); }
        void Test(string name, Action action) { action(); count++; Console.WriteLine("PASS " + name); }
        void Reject(Action action)
        {
            try { action(); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or InvalidDataException or JsonException) { return; }
            throw new Exception("Expected runtime validation failure");
        }
        HmiObject Number(HmiIoMode mode = HmiIoMode.InputOutput) => new("number", HmiKind.Numeric, "Setpoint", "Speed_Setpoint", 40, 40, 220, 110)
            { Runtime = new() { IoMode = mode, Minimum = 0, Maximum = 100, DecimalPlaces = 2, Unit = "rpm" } };
        HmiObject Button(string id, HmiButtonAction action, string tag = "Run_Delay", string screen = "", double value = 0) =>
            new(id, HmiKind.Button, id, tag, 40, 180, 150, 50) { Runtime = new() { Action = action, ScreenId = screen, WriteValue = value } };
        ControlProject Project()
        {
            var p = DemoProject.Create(); p.Blocks.Clear(); p.Screens.Clear();
            p.Screens.Add(new("operator", "Operator", 960, 540,
            [Number(), Button("set", HmiButtonAction.SetBit), Button("reset", HmiButtonAction.ResetBit), Button("toggle", HmiButtonAction.ToggleBit),
             Button("constant", HmiButtonAction.SetValue, "Speed_Setpoint", value: 37.25), Button("next", HmiButtonAction.ActivateScreen, "", "second")]));
            p.Screens.Add(new("second", "Second", 960, 540, [Button("back", HmiButtonAction.PreviousScreen, "")]));
            return p;
        }
        VirtualPlc Cpu(ControlProject p)
        {
            var result = ProjectCompiler.Compile(p);
            if (!result.Success) throw new Exception(string.Join("\n", result.Diagnostics));
            return new(result.Program!);
        }
        void Replace(ControlProject p, HmiObject o) => p.Screens[0].Objects[0] = o;
        Test("runtime configuration survives JSON and structural snapshots", () =>
        {
            var p = Project(); var q = ProjectStorage.Deserialize(ProjectStorage.Serialize(p));
            Check(ProjectSnapshot.ContentEquals(p, q));
            var changed = ProjectSnapshot.Clone(q); changed.Screens[0].Objects[0] = changed.Screens[0].Objects[0] with { Runtime = new() { Unit = "bar" } };
            Check(!ProjectSnapshot.ContentEquals(q, changed));
        });
        Test("legacy exports omit absent runtime configuration", () =>
        {
            var p = DemoProject.Create(); string text = ProjectStorage.Serialize(p);
            Check(!text.Contains("\"runtime\"")); Check(ProjectStorage.Deserialize(text).Screens[0].Objects.All(o => o.Runtime is null));
        });
        Test("unknown and duplicate runtime JSON fields are rejected", () =>
        {
            string text = ProjectStorage.Serialize(Project());
            Reject(() => ProjectStorage.Deserialize(text.Replace("\"unit\": \"rpm\"", "\"unit\": \"rpm\", \"unit\": \"bar\"")));
            Reject(() => ProjectStorage.Deserialize(text.Replace("\"unit\": \"rpm\"", "\"unit\": \"rpm\", \"script\": \"anything\"")));
        });
        foreach (var invalid in new HmiRuntimeOptions[]
        {
            new() { Minimum = 100, Maximum = 0 }, new() { Minimum = 0, Maximum = 0 },
            new() { Minimum = double.NaN }, new() { Maximum = double.PositiveInfinity },
            new() { DecimalPlaces = -1 }, new() { DecimalPlaces = 7 }, new() { Unit = "x\ny" },
            new() { Unit = new string('x', 25) }, new() { IoMode = (HmiIoMode)99 }, new() { Action = (HmiButtonAction)99 }
        }) Test("invalid runtime configuration " + count, () =>
        {
            var p = Project(); Replace(p, Number() with { Runtime = invalid });
            Check(ProjectValidator.Validate(p).Any(d => d.Code == "CS047"));
        });
        Test("shape and label behavior payloads are rejected", () =>
        {
            foreach (var kind in new[] { HmiKind.Rectangle, HmiKind.Ellipse, HmiKind.Line, HmiKind.Label, HmiKind.Lamp })
            {
                var p = Project(); Replace(p, Number() with { Kind = kind, Tag = "", Runtime = new() });
                Check(ProjectValidator.Validate(p).Any(d => d.Code == "CS047"));
            }
        });
        Test("navigation target and incompatible action fields reject atomically", () =>
        {
            var w = new Workspace(Project()); var e = new HmiEditor(w); var before = w.Project;
            Reject(() => e.UpdateObject(before, "operator", Button("next", HmiButtonAction.ActivateScreen, "", "missing")));
            Reject(() => e.UpdateObject(before, "operator", Button("next", HmiButtonAction.ActivateScreen, "Start_PB", "second")));
            Reject(() => e.UpdateObject(before, "operator", Button("next", HmiButtonAction.SetBit, "Run_Delay", "second")));
            Check(ReferenceEquals(before, w.Project) && !w.CanUndo);
        });
        Test("bit actions require BOOL writable tags", () =>
        {
            foreach (var tag in new[] { "Speed_Setpoint", "Motor_Run", "missing" })
            {
                var p = Project(); Replace(p, Button("bad", HmiButtonAction.ToggleBit, tag));
                Check(ProjectValidator.Validate(p).Any(d => d.Code == "CS047"));
            }
        });
        Test("numeric input rejects output-image and BOOL bindings", () =>
        {
            foreach (var tag in new[] { "Motor_Run", "Start_PB", "" })
            {
                var p = Project(); Replace(p, Number() with { Tag = tag }); Check(ProjectValidator.Validate(p).Any(d => d.Code == "CS047"));
            }
        });
        Test("numeric output can read numeric output-image tags", () =>
        {
            var p = Project(); p.Tags.Add(new("Analog_Output", PlcType.Real, "%QD100"));
            Replace(p, Number(HmiIoMode.Output) with { Tag = "Analog_Output" }); Check(ProjectCompiler.Compile(p).Success);
        });
        Test("valid numeric entry writes without project history or force entries", () =>
        {
            var w = new Workspace(Project()); w.Compile(); var before = ProjectStorage.Serialize(w.Project);
            using var session = new HmiRuntimeSession(w.Project, w.Controller!, "operator");
            using var entry = session.BeginNumericInput("number"); entry.Commit("73.25");
            Check(w.Controller!.Read("Speed_Setpoint") == 73.25 && w.Controller.Forces.Count == 0);
            Check(!w.CanUndo && !w.IsDirty && before == ProjectStorage.Serialize(w.Project));
            Reject(() => entry.Commit("1"));
        });
        foreach (string text in new[] { "", "NaN", "Infinity", "1,5", "20 rpm", "101", "-1", "0.001", "1e999", new string('1', 81) })
            Test("numeric input rejects " + (text.Length > 20 ? "oversized input" : text), () =>
            {
                var p = Project(); var c = Cpu(p); using var s = new HmiRuntimeSession(p, c, "operator");
                using var entry = s.BeginNumericInput("number"); Reject(() => entry.Commit(text));
                Check(c.Read("Speed_Setpoint") == 65); entry.Commit("64.5"); Check(c.Read("Speed_Setpoint") == 64.5);
            });
        Test("DINT numeric input rejects fractional values even inside configured limits", () =>
        {
            var p = Project(); Replace(p, Number() with { Tag = "Part_Count" }); var c = Cpu(p);
            using var s = new HmiRuntimeSession(p, c, "operator"); using var input = s.BeginNumericInput("number");
            Reject(() => input.Commit("1.5")); input.Commit("99"); Check(c.Read("Part_Count") == 99);
        });
        Test("numeric input respects invariant decimals under non-English culture", () =>
        {
            var old = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL"); var p = Project(); var c = Cpu(p); using var s = new HmiRuntimeSession(p, c, "operator"); using var input = s.BeginNumericInput("number"); input.Commit("10.25"); Check(c.Read("Speed_Setpoint") == 10.25); }
            finally { CultureInfo.CurrentCulture = old; }
        });
        Test("input-only fields do not show the existing process value", () =>
        {
            var p = Project(); Replace(p, Number(HmiIoMode.Input)); var c = Cpu(p); using var s = new HmiRuntimeSession(p, c, "operator");
            using var input = s.BeginNumericInput("number"); Check(input.InitialText == "" && HmiRuntimeRules.Format(Number(HmiIoMode.Input), 65, true) == "Enter value…");
        });
        Test("cancelled numeric entry writes nothing", () =>
        {
            var p = Project(); var c = Cpu(p); using var s = new HmiRuntimeSession(p, c, "operator"); var input = s.BeginNumericInput("number"); input.Dispose();
            Reject(() => input.Commit("7")); Check(c.Read("Speed_Setpoint") == 65);
        });
        foreach (string change in new[] { "stop", "run", "stop-run", "reset", "navigate", "suspend", "new-entry", "dispose" })
            Test("pending numeric write rejects context change " + change, () =>
            {
                var p = Project(); var c = Cpu(p); using var s = new HmiRuntimeSession(p, c, "operator"); using var input = s.BeginNumericInput("number");
                switch (change)
                {
                    case "stop": c.Stop(); break; case "run": c.Run(); break; case "stop-run": c.Stop(); c.Run(); break;
                    case "reset": c.Reset(); break; case "navigate": s.Navigate("second"); s.Back(); break;
                    case "suspend": s.CancelPendingInput(); break; case "new-entry": s.BeginNumericInput("number").Dispose(); break; case "dispose": s.Dispose(); break;
                }
                Reject(() => input.Commit("4")); Check(c.Read("Speed_Setpoint") == 65);
            });
        Test("ordinary scans do not invalidate a pending numeric entry", () =>
        {
            var p = Project(); var c = Cpu(p); c.Run(); using var s = new HmiRuntimeSession(p, c, "operator"); using var input = s.BeginNumericInput("number");
            for (int i = 0; i < 8; i++) c.Step(TimeSpan.FromMilliseconds(100)); input.Commit("7.25"); Check(c.Read("Speed_Setpoint") == 7.25);
        });
        Test("forces reject numeric writes without overwriting the forced tag", () =>
        {
            var p = Project(); var c = Cpu(p); c.Force("Speed_Setpoint", 20); using var s = new HmiRuntimeSession(p, c, "operator"); using var input = s.BeginNumericInput("number");
            Reject(() => input.Commit("30")); Check(c.Read("Speed_Setpoint") == 65 && c.Forces.Count == 1); c.Release("Speed_Setpoint"); input.Commit("30"); Check(c.Read("Speed_Setpoint") == 30);
        });
        Test("set reset toggle and constant actions write once without forcing", () =>
        {
            var p = Project(); var c = Cpu(p); using var s = new HmiRuntimeSession(p, c, "operator");
            s.ActivateButton("set"); Check(c.Read("Run_Delay") == 1); s.ActivateButton("set"); Check(c.Read("Run_Delay") == 1);
            s.ActivateButton("reset"); Check(c.Read("Run_Delay") == 0); s.ActivateButton("toggle"); Check(c.Read("Run_Delay") == 1);
            s.ActivateButton("toggle"); Check(c.Read("Run_Delay") == 0); s.ActivateButton("constant"); Check(c.Read("Speed_Setpoint") == 37.25 && c.Forces.Count == 0);
        });
        Test("virtual HMI input-image writes survive the next scan", () =>
        {
            var p = Project(); var c = Cpu(p); c.WriteHmiValue("Start_PB", 1); c.Run(); c.Step(TimeSpan.FromMilliseconds(100)); Check(c.Read("Start_PB") == 1);
        });
        Test("marker writes are not persistent forces and the program can overwrite them", () =>
        {
            var p = Project(); p.Blocks.Add(new("scl", "SCL", 1, BlockLanguage.SCL, true, [], "Speed_Setpoint := 12;"));
            var c = Cpu(p); c.WriteHmiValue("Speed_Setpoint", 50); c.Run(); c.Step(TimeSpan.FromMilliseconds(100)); Check(c.Read("Speed_Setpoint") == 12 && c.Forces.Count == 0);
        });
        Test("one-shot output-image invalid and forced writes do not alter state", () =>
        {
            var p = Project(); var c = Cpu(p); long before = c.VisualVersion;
            Reject(() => c.WriteHmiValue("Motor_Run", 1)); Reject(() => c.WriteHmiValue("Speed_Setpoint", double.NaN));
            Reject(() => c.WriteHmiValue("Part_Count", .5)); Check(c.VisualVersion == before && c.Read("Speed_Setpoint") == 65);
        });
        Test("momentary helper rejects forced input tags", () =>
        {
            var c = Cpu(Project()); c.Force("Start_PB", 0); using var hold = new HmiMomentaryInput(); Check(!hold.Press(c, "Start_PB") && !hold.IsPressed && c.Read("Start_PB") == 0);
        });
        Test("screen action and Back retain the controller and document", () =>
        {
            var p = Project(); var c = Cpu(p); using var s = new HmiRuntimeSession(p, c, "operator");
            s.ActivateButton("next"); Check(s.ScreenId == "second" && s.CanGoBack && ReferenceEquals(s.Controller, c));
            s.ActivateButton("back"); Check(s.ScreenId == "operator" && !s.CanGoBack); s.Back(); Check(s.ScreenId == "operator");
        });
        Test("screen history is bounded and same-screen actions do not push", () =>
        {
            var p = Project(); using var s = new HmiRuntimeSession(p, Cpu(p), "operator"); s.Navigate("operator"); Check(!s.CanGoBack);
            for (int i = 0; i < 100; i++) s.Navigate(i % 2 == 0 ? "second" : "operator");
            int back = 0; while (s.CanGoBack) { s.Back(); back++; } Check(back == HmiRuntimeSession.HistoryLimit);
        });
        Test("actions cannot dispatch from an inactive screen", () =>
        {
            var p = Project(); using var s = new HmiRuntimeSession(p, Cpu(p), "second"); Reject(() => s.ActivateButton("set")); Reject(() => s.BeginNumericInput("number"));
        });
        Test("pointer activation guards document controller screen and epoch", () =>
        {
            var p = Project(); var c = Cpu(p); using var s = new HmiRuntimeSession(p, c, "operator"); var a = new HmiRuntimeActivation(p, c, "operator", "set", c.InteractionEpoch);
            s.ValidateActivation(a); Reject(() => s.ValidateActivation(a with { Project = ProjectSnapshot.Clone(p) }));
            Reject(() => s.ValidateActivation(a with { Controller = Cpu(p) })); Reject(() => s.ValidateActivation(a with { ScreenId = "second" }));
            c.Stop(); Reject(() => s.ValidateActivation(a));
        });
        Test("session rejects mismatched compiled project", () =>
        {
            var p = Project(); var changed = p with { Revision = 1 }; Reject(() => new HmiRuntimeSession(changed, Cpu(p), "operator"));
        });
        Test("referenced screens cannot be deleted but self-linked screens can", () =>
        {
            var w = new Workspace(Project()); var before = w.Project; var e = new HmiEditor(w); Reject(() => e.DeleteScreen(before, "second")); Check(ReferenceEquals(before, w.Project));
            e.UpdateObject(w.Project, "operator", w.Project.Screens[0].Objects.First(o => o.Id == "next") with { Runtime = new() { Action = HmiButtonAction.ActivateScreen, ScreenId = "operator" } });
            e.DeleteScreen(w.Project, "operator"); Check(w.Project.Screens.Count == 1);
        });
        Test("screen duplication retargets self links and keeps external links", () =>
        {
            var p = Project(); p.Screens[0].Objects.Add(Button("self", HmiButtonAction.ActivateScreen, "", "operator"));
            var w = new Workspace(p); string id = new HmiEditor(w).DuplicateScreen(w.Project, "operator"); var copy = HmiEditor.Screen(w.Project, id);
            Check(copy.Objects.Single(o => o.Text == "self").Runtime!.ScreenId == id);
            Check(copy.Objects.Single(o => o.Text == "next").Runtime!.ScreenId == "second");
        });
        Test("clipboard preserves runtime settings and rejects missing target screens", () =>
        {
            var w = new Workspace(Project()); var e = new HmiEditor(w); string text = HmiEditor.Copy(w.Project, "operator", ["number"]);
            var ids = e.Paste(w.Project, "operator", text); Check(w.Project.Screens[0].Objects.Single(o => o.Id == ids[0]).Runtime == Number().Runtime);
            text = HmiEditor.Copy(w.Project, "operator", ["next"]); var p = Project(); p.Screens[0].Objects.RemoveAll(o => o.Id == "next"); p.Screens.RemoveAt(1);
            var other = new Workspace(p); Reject(() => new HmiEditor(other).Paste(other.Project, "operator", text)); Check(!other.CanUndo);
        });
        Test("tag rename preserves action configuration and updates runtime bindings", () =>
        {
            var w = new Workspace(Project()); w.RenameTag("Speed_Setpoint", "TargetSpeed"); Check(w.Project.Screens[0].Objects[0].Tag == "TargetSpeed");
            w.Compile(); using var s = new HmiRuntimeSession(w.Project, w.Controller!, "operator"); s.ActivateButton("constant"); Check(w.Controller!.Read("TargetSpeed") == 37.25);
        });
        Test("normalized custom ranges clamp and format independently of culture", () =>
        {
            var o = Number() with { Runtime = new() { Minimum = -50, Maximum = 150, DecimalPlaces = 1, Unit = "bar" } };
            Check(HmiRuntimeRules.Fraction(o, -100) == 0 && HmiRuntimeRules.Fraction(o, 50) == .5 && HmiRuntimeRules.Fraction(o, 200) == 1);
            Check(HmiRuntimeRules.Format(o, 2.25) == "2.2 bar"); Check(HmiRuntimeRules.Format(o, double.NaN) == "—");
        });
        Test("public and designer renderers draw configured numeric objects consistently", () =>
        {
            using var a = SKSurface.Create(new SKImageInfo(1000, 700)); using var b = SKSurface.Create(new SKImageInfo(1000, 700)); using var renderer = new EngineeringRenderer();
            var p = Project(); var screen = p.Screens[0] with { Width = 952, Height = 652, Objects = [Number()] };
            renderer.Hmi(a.Canvas, 1000, 700, screen, p.Tags, null, runtime: true);
            renderer.HmiDesigner(b.Canvas, 1000, 700, screen, p.Tags, null, new(1, 24, 24), [], runtime: true);
            using var ia = a.Snapshot(); using var ib = b.Snapshot(); using var pa = ia.PeekPixels(); using var pb = ib.PeekPixels();
            for (int y = 67; y < 170; y += 7) for (int x = 67; x < 280; x += 7) Check(pa.GetPixelColor(x, y) == pb.GetPixelColor(x, y));
            Check(a.Canvas.SaveCount == b.Canvas.SaveCount);
        });
        Test("unrepresentable integer range is rejected during configuration", () =>
        {
            var p = Project(); Replace(p, Number() with { Tag = "Part_Count", Runtime = new() { IoMode = HmiIoMode.InputOutput, Minimum = .1, Maximum = .9 } });
            Check(ProjectValidator.Validate(p).Any(d => d.Code == "CS047"));
        });
        Test("fault during a pending numeric entry rejects the write", () =>
        {
            var p = Project(); p.Blocks.Add(new("fault", "Fault", 1, BlockLanguage.SCL, true, [], "Speed_Actual := 1 / 0;"));
            var c = Cpu(p); c.Run(); using var s = new HmiRuntimeSession(p, c, "operator"); using var entry = s.BeginNumericInput("number");
            Check(!c.Step(TimeSpan.FromMilliseconds(100))); Reject(() => entry.Commit("3")); Check(c.Read("Speed_Setpoint") == 65);
        });
        Test("standalone numeric input adapter preserves validation retry and one-shot commit", () =>
        {
            var p = Project(); var c = Cpu(p); var entry = new HmiNumericInputSession(p, c, "operator", "number");
            Reject(() => entry.Commit(p, c, "200")); Check(c.Read("Speed_Setpoint") == 65);
            Check(entry.Commit(p, c, "45.25") == 45.25 && c.Forces.Count == 0);
            Reject(() => entry.Commit(p, c, "20"));
        });
        Test("standalone numeric input adapter rejects replacement and lifecycle changes", () =>
        {
            var p = Project(); var c = Cpu(p); var entry = new HmiNumericInputSession(p, c, "operator", "number");
            Reject(() => entry.Commit(ProjectSnapshot.Clone(p), c, "1"));
            Reject(() => entry.Commit(p, Cpu(p), "1")); c.Stop(); c.Run();
            Reject(() => entry.Commit(p, c, "1")); Check(c.Read("Speed_Setpoint") == 65);
        });
        Test("standalone numeric operator writes reject bits outputs and active forces", () =>
        {
            var c = Cpu(Project()); Reject(() => c.SetOperatorValue("Start_PB", 1));
            Reject(() => c.SetOperatorValue("Motor_Run", 1)); c.Force("Speed_Setpoint", 20);
            Reject(() => c.SetOperatorValue("Speed_Setpoint", 25)); c.ReleaseAll();
            c.SetOperatorValue("Speed_Setpoint", 25); Check(c.Read("Speed_Setpoint") == 25 && c.Forces.Count == 0);
        });
        Test("standalone navigation keeps bounded history and rejects missing targets", () =>
        {
            var p = Project(); var history = new HmiNavigationSession(); string current = "operator";
            Check(history.Activate(p, current, current) == current && history.Count == 0);
            for (int i = 0; i < 100; i++) current = history.Activate(p, current, current == "operator" ? "second" : "operator");
            Check(history.Count == 64); Reject(() => history.Activate(p, current, "missing")); Check(history.Count == 64);
            int back = 0; while (history.Count > 0) { current = history.Back(p, current); back++; }
            Check(back == 64); history.Clear(); Check(history.Count == 0);
        });
        Test("standalone navigation skips deleted screens and clears different projects", () =>
        {
            var p = Project(); var history = new HmiNavigationSession();
            history.Activate(p, "operator", "second"); p.Screens.RemoveAt(0);
            Check(history.Back(p, "second") == "second" && history.Count == 0);
            p = Project(); history.Activate(p, "operator", "second");
            var other = p with { Id = "another-project" };
            Check(history.Back(other, "second") == "second" && history.Count == 0);
        });
        return count;
    }
}
