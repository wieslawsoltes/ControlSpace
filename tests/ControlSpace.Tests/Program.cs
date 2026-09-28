using ControlSpace.Core;
using ControlSpace.Languages;
using ControlSpace.Simulation;
using ControlSpace.Storage;

var passed = 0; var failed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.Error.WriteLine($"FAIL {name}: {e}"); }
}
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}."); }
void True(bool value) { if (!value) throw new Exception("Expected true."); }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
TagDefinition Tag(string name, PlcType type = PlcType.Bool, string address = "", double value = 0) => new() { Name = name, Type = type, Address = address, InitialValue = value };
ProjectDocument TextProject(string text, params TagDefinition[] tags) => new() { Tags = tags, Blocks = [new() { Language = ProgramLanguage.ST, Source = text }] };
PlcSimulator Simulator(ProjectDocument project)
{
    var compile = ProjectCompiler.Compile(project);
    if (!compile.Success) throw new Exception(string.Join("; ", compile.Diagnostics.Select(d => d.Message)));
    return new(compile.Program!);
}
ProjectDocument Ladder(ActionKind action = ActionKind.Coil, ContactKind contact = ContactKind.NormallyOpen, double preset = 200) => new()
{
    Tags = [Tag("Input", address: "%I0.0"), Tag("Output", action is ActionKind.CTU or >= ActionKind.Move ? PlcType.DInt : PlcType.Bool, action is ActionKind.CTU or >= ActionKind.Move ? "%MD0" : "%Q0.0"), Tag("Reset", address: "%I0.1"), Tag("Elapsed", PlcType.Time, "%MD10"), Tag("Done", address: "%M20.0")],
    Blocks = [new() { Networks = [new() { Paths = [[new() { Operand = "Input", Kind = contact }]], Action = new() { Kind = action, Target = "Output", Preset = preset, InputA = "7", InputB = "2", ResetTag = action == ActionKind.CTU ? "Reset" : "", AuxiliaryTag = action == ActionKind.CTU ? "Done" : action is ActionKind.TON or ActionKind.TOF or ActionKind.TP ? "Elapsed" : "" } }] }]
};

Test("demo compiles", () => True(ProjectCompiler.Compile(DemoProject.Create()).Success));
Test("project JSON round trip", () => { var p = DemoProject.Create(); Equal(DocumentCodec.Serialize(p), DocumentCodec.Serialize(DocumentCodec.Deserialize(DocumentCodec.Serialize(p)))); });
Test("unknown version rejected", () => Throws<InvalidDataException>(() => DocumentCodec.Deserialize("{\"formatVersion\":99}")));
Test("null collection rejected", () => Throws<InvalidDataException>(() => DocumentCodec.Deserialize("{\"tags\":null}")));
Test("undefined enum rejected", () => { var p = Ladder() with { Tags = [Tag("x", (PlcType)999)] }; True(!ProjectCompiler.Compile(p).Success); });
Test("duplicate IDs rejected", () => { var p = Ladder(); p = p with { Blocks = [p.Blocks[0], p.Blocks[0]] }; True(!ProjectCompiler.Compile(p).Success); });
Test("nonfinite geometry rejected", () => { var p = DemoProject.Create(); p = p with { Devices = [p.Devices[0] with { X = double.NaN }] }; True(!ProjectCompiler.Compile(p).Success); });
Test("case insensitive duplicate tag", () => True(!ProjectCompiler.Compile(new() { Tags = [Tag("A"), Tag("a")] }).Success));
Test("reserved tag rejected", () => True(!ProjectCompiler.Compile(new() { Tags = [Tag("IF")] }).Success));
Test("input writes rejected in LAD", () => { var p = Ladder(); p = p with { Tags = p.Tags.Select(t => t.Name == "Output" ? t with { Address = "%I1.0" } : t).ToArray() }; True(!ProjectCompiler.Compile(p).Success); });
Test("input writes rejected in ST", () => True(!ProjectCompiler.Compile(TextProject("Input := TRUE;", Tag("Input", address: "%I0.0"))).Success));
Test("overlapping address rejected", () => True(!ProjectCompiler.Compile(new() { Tags = [Tag("A", PlcType.DInt, "%MD0"), Tag("B", address: "%M1.0")] }).Success));
Test("address type mismatch rejected", () => True(!PlcAddress.TryParse("%MW0", PlcType.Real, out _)));
Test("valid bool address", () => { True(PlcAddress.TryParse("%I2.7", PlcType.Bool, out var a)); Equal(23, a.StartBit); True(a.IsInput); });
Test("byte bit range rejected", () => True(!PlcAddress.TryParse("%I0.8", PlcType.Bool, out _)));
Test("disjoint addresses accepted", () => True(ProjectCompiler.Compile(new() { Tags = [Tag("A", address: "%M0.0"), Tag("B", address: "%M0.1")] }).Success));
foreach (var type in Enum.GetValues<PlcType>()) Test($"normalize zero {type}", () => Equal(0d, PlcValues.Normalize(type, 0)));
Test("BOOL does not silently coerce", () => Throws<ArithmeticException>(() => PlcValues.Normalize(PlcType.Bool, 2)));
Test("INT overflow rejected", () => Throws<ArithmeticException>(() => PlcValues.Normalize(PlcType.Int, 32768)));
Test("DINT fractional rejected", () => Throws<ArithmeticException>(() => PlcValues.Normalize(PlcType.DInt, 1.5)));
Test("NaN rejected", () => Throws<ArithmeticException>(() => PlcValues.Normalize(PlcType.Real, double.NaN)));
foreach (var item in new[] { ("T#2s", 2000d), ("TIME#1.5m", 90000d), ("T#-20ms", -20d), ("TRUE", 1d), ("FALSE", 0d), ("1.25", 1.25) }) Test($"parse {item.Item1}", () => { True(PlcValues.TryParse(item.Item1, out var v)); Equal(item.Item2, v); });
Test("bad TIME literal rejected", () => True(!PlcValues.TryParse("T#1.3ms", out _)));
Test("coil follows input", () => { var s = Simulator(Ladder()); s.SetValue("Input", 1); True(s.Step()); Equal(1d, s.GetValue("Output")); s.SetValue("Input", 0); s.Step(); Equal(0d, s.GetValue("Output")); });
Test("normally closed contact", () => { var s = Simulator(Ladder(contact: ContactKind.NormallyClosed)); s.Step(); Equal(1d, s.GetValue("Output")); });
Test("rising edge lasts one scan", () => { var s = Simulator(Ladder(contact: ContactKind.RisingEdge)); s.SetValue("Input", 1); s.Step(); Equal(1d, s.GetValue("Output")); s.Step(); Equal(0d, s.GetValue("Output")); });
Test("falling edge lasts one scan", () => { var s = Simulator(Ladder(contact: ContactKind.FallingEdge)); s.SetValue("Input", 1); s.Step(); Equal(0d, s.GetValue("Output")); s.SetValue("Input", 0); s.Step(); Equal(1d, s.GetValue("Output")); s.Step(); Equal(0d, s.GetValue("Output")); });
Test("set latches", () => { var s = Simulator(Ladder(ActionKind.Set)); s.SetValue("Input", 1); s.Step(); s.SetValue("Input", 0); s.Step(); Equal(1d, s.GetValue("Output")); });
Test("reset clears", () => { var p = Ladder(ActionKind.Reset); var block = p.Blocks[0]; p = p with { Blocks = [block with { Networks = [block.Networks[0] with { Id = Guid.NewGuid(), Paths = [[]], Action = new() { Kind = ActionKind.Set, Target = "Output" } }, block.Networks[0]] }] }; var s = Simulator(p); s.SetValue("Input", 1); s.Step(); Equal(0d, s.GetValue("Output")); });
Test("TON elapsed and reset", () => { var s = Simulator(Ladder(ActionKind.TON)); s.SetValue("Input", 1); s.Step(100); Equal(0d, s.GetValue("Output")); Equal(100d, s.GetValue("Elapsed")); s.Step(100); Equal(1d, s.GetValue("Output")); s.SetValue("Input", 0); s.Step(); Equal(0d, s.GetValue("Elapsed")); });
Test("TON zero preset", () => { var s = Simulator(Ladder(ActionKind.TON, preset: 0)); s.SetValue("Input", 1); s.Step(); Equal(1d, s.GetValue("Output")); });
Test("TOF falling delay", () => { var s = Simulator(Ladder(ActionKind.TOF)); s.SetValue("Input", 1); s.Step(100); Equal(1d, s.GetValue("Output")); s.SetValue("Input", 0); s.Step(100); Equal(1d, s.GetValue("Output")); s.Step(100); Equal(0d, s.GetValue("Output")); });
Test("TP does not retrigger while high", () => { var s = Simulator(Ladder(ActionKind.TP)); s.SetValue("Input", 1); s.Step(100); Equal(1d, s.GetValue("Output")); s.Step(100); s.Step(100); Equal(0d, s.GetValue("Output")); });
Test("CTU edge and reset priority", () => { var s = Simulator(Ladder(ActionKind.CTU, preset: 2)); s.SetValue("Input", 1); s.Step(); s.Step(); Equal(1d, s.GetValue("Output")); s.SetValue("Input", 0); s.Step(); s.SetValue("Input", 1); s.Step(); Equal(2d, s.GetValue("Output")); Equal(1d, s.GetValue("Done")); s.SetValue("Reset", 1); s.Step(); Equal(0d, s.GetValue("Output")); });
foreach (var item in new[] { (ActionKind.Move, 7d), (ActionKind.Add, 9d), (ActionKind.Subtract, 5d), (ActionKind.Multiply, 14d), (ActionKind.Divide, 3d) }) Test($"ladder {item.Item1}", () => { var s = Simulator(Ladder(item.Item1)); s.SetValue("Input", 1); s.Step(); Equal(item.Item2, s.GetValue("Output")); });
Test("parallel branch seal-in circuit", () => { var s = Simulator(DemoProject.Create()); s.SetValue("Start", 1); s.Step(100); s.SetValue("Start", 0); s.Step(100); Equal(1d, s.GetValue("ConveyorRun")); Equal(55d, s.GetValue("BeltSpeed")); s.SetValue("Stop", 1); s.Step(); Equal(0d, s.GetValue("ConveyorRun")); Equal(0d, s.GetValue("BeltSpeed")); });
Test("guard input inhibits seal-in", () => { var s = Simulator(DemoProject.Create()); s.SetValue("Start", 1); s.Step(); s.SetValue("GuardClosed", 0); s.Step(); Equal(0d, s.GetValue("ConveyorRun")); Equal(1, s.Alarms.Count); });
Test("alarm acknowledge and clear", () => { var s = Simulator(DemoProject.Create()); s.SetValue("GuardClosed", 0); s.Step(); s.AcknowledgeAll(); True(s.Alarms[0].Acknowledged); s.SetValue("GuardClosed", 1); s.Step(); True(!s.Alarms[0].Active); });
Test("STOP clears outputs and forces", () => { var s = Simulator(Ladder()); s.ForceInput("Input", 1); s.Step(); Equal(1d, s.GetValue("Output")); s.Stop(); Equal(0d, s.GetValue("Output")); Equal(0, s.ForceCount); });
Test("watch cannot write outputs", () => Throws<InvalidOperationException>(() => Simulator(Ladder()).SetValue("Output", 1)));
Test("force accepts input only", () => Throws<InvalidOperationException>(() => Simulator(Ladder()).ForceInput("Done", 1)));
Test("force blocks accidental write", () => { var s = Simulator(Ladder()); s.ForceInput("Input", 1); Throws<InvalidOperationException>(() => s.SetValue("Input", 0)); s.ReleaseForce("Input"); s.SetValue("Input", 0); Equal(0d, s.GetValue("Input")); });
Test("invalid scan delta rejected", () => Throws<ArgumentOutOfRangeException>(() => Simulator(Ladder()).Step(0)));
Test("ST arithmetic precedence", () => { var s = Simulator(TextProject("x := 2 + 3 * 4;", Tag("x", PlcType.DInt))); s.Step(); Equal(14d, s.GetValue("x")); });
Test("ST unary and parentheses", () => { var s = Simulator(TextProject("x := -(2 + 3) * 4;", Tag("x", PlcType.DInt))); s.Step(); Equal(-20d, s.GetValue("x")); });
Test("ST IF ELSIF ELSE", () => { var s = Simulator(TextProject("IF FALSE THEN x := 1; ELSIF TRUE THEN x := 2; ELSE x := 3; END_IF;", Tag("x", PlcType.DInt))); s.Step(); Equal(2d, s.GetValue("x")); });
Test("ST case-insensitive symbols", () => { var s = Simulator(TextProject("X := 3;", Tag("x", PlcType.DInt))); s.Step(); Equal(3d, s.GetValue("X")); });
Test("ST boolean operators", () => { var s = Simulator(TextProject("x := NOT FALSE AND (TRUE XOR FALSE) OR FALSE;", Tag("x"))); s.Step(); Equal(1d, s.GetValue("x")); });
Test("ST nested comments", () => { var s = Simulator(TextProject("(* outer (* inner *) *) x := 1; // end", Tag("x", PlcType.DInt))); s.Step(); Equal(1d, s.GetValue("x")); });
Test("ST unterminated comment", () => True(!ProjectCompiler.Compile(TextProject("(* comment", Tag("x"))).Success));
Test("ST unsupported source is an error", () => True(!ProjectCompiler.Compile(TextProject("FUNCTION_BLOCK FB1", Tag("x"))).Success));
Test("ST line diagnostics", () => { var c = ProjectCompiler.Compile(TextProject("// comment\nmissing := 1;", Tag("x"))); Equal(2, c.Diagnostics[0].Line); });
Test("ST WHILE", () => { var s = Simulator(TextProject("x := 0; WHILE x < 10 DO x := x + 1; END_WHILE;", Tag("x", PlcType.DInt))); True(s.Step()); Equal(10d, s.GetValue("x")); });
Test("ST REPEAT", () => { var s = Simulator(TextProject("x := 0; REPEAT x := x + 1; UNTIL x >= 5 END_REPEAT;", Tag("x", PlcType.DInt))); True(s.Step()); Equal(5d, s.GetValue("x")); });
Test("ST function calls", () => { var s = Simulator(TextProject("x := MAX(ABS(-7), MIN(10, 4)); y := SQRT(9.0);", Tag("x", PlcType.DInt), Tag("y", PlcType.Real))); True(s.Step()); Equal(7d, s.GetValue("x")); Equal(3d, s.GetValue("y")); });
Test("ST integer division", () => { var s = Simulator(TextProject("x := 7 / 2;", Tag("x", PlcType.DInt))); True(s.Step()); Equal(3d, s.GetValue("x")); });
Test("ST real division", () => { var s = Simulator(TextProject("x := 7.0 / 2.0;", Tag("x", PlcType.Real))); True(s.Step()); Equal(3.5d, s.GetValue("x")); });
Test("ST type mismatch is rejected", () => True(!ProjectCompiler.Compile(TextProject("x := 1;", Tag("x"))).Success));
Test("ST scan rollback on zero division", () => { var s = Simulator(TextProject("x := 5; y := 1 / 0;", Tag("x", PlcType.DInt), Tag("y", PlcType.DInt))); True(!s.Step()); Equal(0d, s.GetValue("x")); Equal(CpuMode.Faulted, s.Mode); });
Test("ST budget stops infinite loop", () => { var s = Simulator(TextProject("WHILE TRUE DO x := 1; END_WHILE;", Tag("x", PlcType.DInt))); True(!s.Step()); Equal(0L, s.ScanCount); Equal(0d, s.GetValue("x")); True(s.LastFault!.Contains("budget")); });
Test("fault requires reset", () => { var s = Simulator(TextProject("x := 1 / 0;", Tag("x", PlcType.DInt))); s.Step(); Throws<InvalidOperationException>(s.Start); s.Reset(); Equal(CpuMode.Stopped, s.Mode); });
Test("warm reset preserves retained memory", () => { var p = TextProject("", Tag("x", PlcType.DInt) with { Retain = true }); var s = Simulator(p); s.SetValue("x", 12); s.Reset(false); Equal(12d, s.GetValue("x")); s.Reset(true); Equal(0d, s.GetValue("x")); });
Test("cross references contain read and write", () => { var c = ProjectCompiler.Compile(DemoProject.Create()); True(c.References.Any(r => r.Tag == "ConveyorRun" && r.Access == "Read")); True(c.References.Any(r => r.Tag == "ConveyorRun" && r.Access == "Write")); });
Test("trace bounded order", () => { var t = new TraceBuffer(["x"], 3); for (var i = 0; i < 5; i++) t.Add(i, [i * 2]); Equal(3, t.Count); Equal(2d, t.TimeAt(0)); Equal(8d, t.ValueAt(0, 2)); });
Test("trace channel limit", () => Throws<ArgumentException>(() => new TraceBuffer(Enumerable.Range(0, 9).Select(i => i.ToString()))));
Test("trace sample on successful scan only", () => { var s = Simulator(Ladder()); s.Step(); Equal(1, s.Trace!.Count); });
Test("history saved identity survives undo", () => { var a = new object(); var h = new DocumentHistory<object>(a); h.Commit(new()); h.MarkSaved(); h.Commit(new()); True(h.IsDirty); h.Undo(); True(!h.IsDirty); h.Undo(); True(h.IsDirty); h.Redo(); True(!h.IsDirty); });
Test("history branches invalidate redo", () => { var h = new DocumentHistory<object>(new()); h.Commit(new()); h.Undo(); h.Commit(new()); True(!h.CanRedo); });
Test("bounded history", () => { var h = new DocumentHistory<object>(new(), 2); for (var i = 0; i < 10; i++) h.Commit(new()); True(h.Undo()); True(h.Undo()); True(!h.Undo()); });
Test("CSV escaped multiline round trip", () => { var tags = new[] { Tag("X") with { Comment = "First, \"quoted\"\nsecond line" } }; var actual = TagCsv.Import(TagCsv.Export(tags)); Equal(tags[0], actual[0]); });
Test("CSV duplicate rejected", () => Throws<InvalidDataException>(() => TagCsv.Import(TagCsv.Export([Tag("X"), Tag("x")]))));
Test("CSV malformed quotes rejected", () => Throws<InvalidDataException>(() => TagCsv.Import("Name,DataType,Address,InitialValue,Retain,Comment\n\"oops")));
Test("invalid IP rejected", () => { var p = DemoProject.Create(); p = p with { Devices = p.Devices.Select((d, i) => i == 0 ? d with { IpAddress = "999.0.0.1" } : d).ToArray() }; True(!ProjectCompiler.Compile(p).Success); });
Test("missing HMI binding rejected", () => { var p = DemoProject.Create(); p = p with { Screens = [p.Screens[0] with { Objects = [p.Screens[0].Objects[0] with { Tag = "Missing" }] }] }; True(!ProjectCompiler.Compile(p).Success); });
Test("FBD shares ladder semantics", () => { var p = Ladder(); p = p with { Blocks = [p.Blocks[0] with { Language = ProgramLanguage.FBD }] }; var s = Simulator(p); s.SetValue("Input", 1); s.Step(); Equal(1d, s.GetValue("Output")); });
Test("disabled block not executed", () => { var p = TextProject("x := 5;", Tag("x", PlcType.DInt)); p = p with { Blocks = [p.Blocks[0] with { Enabled = false }] }; var s = Simulator(p); s.Step(); Equal(0d, s.GetValue("x")); });

var directory = Path.Combine(Path.GetTempPath(), "controlspace-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new FileProjectStore(Path.Combine(directory, "workspace.controlspace")); var p = DemoProject.Create();
    var token = await store.SaveAsync(p, null);
    Test("file store saves and reloads", () => { var loaded = store.LoadAsync().GetAwaiter().GetResult(); Equal(token, loaded!.Token); Equal(p.Id, loaded.Project.Id); });
    Test("file store rejects stale token", () => Throws<StorageConflictException>(() => store.SaveAsync(p with { Name = "Changed" }, "stale").GetAwaiter().GetResult()));
    var token2 = await store.SaveAsync(p with { Name = "Changed" }, token);
    Test("file store token changes", () => True(token != token2));
    var loaded2 = await store.LoadAsync(); Test("file store stale save preserved contents", () => Equal("Changed", loaded2!.Project.Name));
}
catch (Exception e) { failed++; Console.Error.WriteLine($"FAIL persistence: {e}"); }
finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
