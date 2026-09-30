using ControlSpace.Core;
using ControlSpace.Simulation;
using SkiaSharp;
namespace ControlSpace.Rendering.Skia;

public sealed record HitRegion(string Id, string Kind, RectD Bounds);
public sealed record RenderResult(IReadOnlyList<HitRegion> Hits, float ContentHeight);
public sealed partial class EngineeringRenderer : IDisposable
{
    private readonly SKPaint _paint = new() { IsAntialias = true };
    private SKTypeface _face = SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;
    private SKTypeface _boldFace = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
    private static readonly SKColor Ink = SKColor.Parse("#24313D"), Teal = SKColor.Parse("#008C95"), Gray = SKColor.Parse("#DADDDD"), Active = SKColor.Parse("#1A9F57");
    private readonly SKFont _font;
    private readonly SKFont _bold;
    public EngineeringRenderer() { _font = new(_face, 12); _bold = new(_boldFace, 12); }
    public string FontFamily => _face.FamilyName;
    /// <summary>Load host-provided font streams once; the renderer owns its resulting typefaces.</summary>
    public void SetFonts(Stream regular, Stream bold)
    {
        var face = SKTypeface.FromStream(regular) ?? throw new InvalidDataException("Invalid renderer text font.");
        var boldFace = SKTypeface.FromStream(bold);
        if (boldFace is null) { face.Dispose(); throw new InvalidDataException("Invalid renderer bold font."); }
        ClearTextRuns();
        _font.Typeface = face; _bold.Typeface = boldFace;
        _face.Dispose(); _boldFace.Dispose(); _face = face; _boldFace = boldFace;
    }
    private IReadOnlyList<PlcTag>? _cachedTags;
    private Dictionary<string, PlcTag> _tagLookup = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _slotLookup = new(StringComparer.OrdinalIgnoreCase);
    private void CacheTags(IReadOnlyList<PlcTag> tags)
    {
        if (ReferenceEquals(tags, _cachedTags)) return;
        _cachedTags = tags; _tagLookup.Clear(); _slotLookup.Clear();
        for (int i = 0; i < tags.Count; i++) { _tagLookup[tags[i].Name] = tags[i]; _slotLookup[tags[i].Name] = i; }
    }
    public int LastDrawnInstructions { get; private set; }
    public int LastDrawnNetworks { get; private set; }
    private ProgramBlock? _selectedBlock;
    private string? _lastSelection, _selectedNetwork;
    private void CacheSelection(ProgramBlock block, string? selection)
    {
        if (ReferenceEquals(block, _selectedBlock) && selection == _lastSelection) return;
        _selectedBlock = block; _lastSelection = selection; _selectedNetwork = null;
        if (selection is null) return;
        foreach (var network in block.Networks)
            if (network.Id == selection || network.Output.Id == selection || network.Branches.Any(b => b.Any(i => i.Id == selection))) { _selectedNetwork = network.Id; return; }
    }
    private void Color(SKColor color, bool stroke = false, float width = 1) { _paint.Color = color; _paint.Style = stroke ? SKPaintStyle.Stroke : SKPaintStyle.Fill; _paint.StrokeWidth = width; }
    private void Text(SKCanvas c, string text, float x, float y, SKColor? color = null, float size = 12, bool bold = false)
    {
        PaintRun(c, Run(text, size, bold), x, y, color ?? Ink, size, bold);
    }
    private void Box(SKCanvas c, float x, float y, float w, float h, SKColor fill, SKColor? stroke = null)
    {
        Color(fill); c.DrawRect(x, y, w, h, _paint); if (stroke is not null) { Color(stroke.Value, true); c.DrawRect(x, y, w, h, _paint); }
    }
    private void Line(SKCanvas c, float x1, float y1, float x2, float y2, SKColor? color = null, float width = 1)
    {
        Color(color ?? Ink, true, width); c.DrawLine(x1, y1, x2, y2, _paint);
    }
    public RenderResult Ladder(SKCanvas canvas, float width, float height, ProgramBlock block, IReadOnlyList<PlcTag> tags, ScanSnapshot? snapshot, string? selection = null, float scroll = 0, float zoom = 1, float horizontal = 0, LadderLayout? layout = null)
        => LadderView(canvas, width, height, block, tags, snapshot, selection, scroll, zoom, horizontal, layout);
    /// <summary>Render a synchronous read view without copying the full controller image.</summary>
    public RenderResult LadderView(SKCanvas canvas, float width, float height, ProgramBlock block, IReadOnlyList<PlcTag> tags, IScanReadView? snapshot, string? selection = null, float scroll = 0, float zoom = 1, float horizontal = 0, LadderLayout? layout = null)
    {
        CacheTags(tags); CacheSelection(block, selection); LastDrawnInstructions = LastDrawnNetworks = 0;
        layout ??= new LadderLayout(block, width / zoom);
        var hits = new List<HitRegion>();
        canvas.Clear(SKColors.White); canvas.Save(); canvas.Scale(zoom); canvas.Translate(-horizontal, -scroll);
        float logicalWidth = (float)layout.Width;
        bool Flow(string id) => snapshot is not null && snapshot.TryGetFlow(id, out bool active) && active;
        foreach (var row in layout.Visible(scroll, height / zoom))
        {
            var network = block.Networks[row.Index]; float y = (float)row.Y;
            bool selected = network.Id == _selectedNetwork; LastDrawnNetworks++;
            Box(canvas, 0, y, logicalWidth, (float)row.Height, SKColors.White, SKColor.Parse("#D9DADD"));
            hits.Add(new(network.Id, "network", new(0, y, logicalWidth, row.Height)));
            Box(canvas, horizontal, y, width / zoom, 26, SKColor.Parse(selected ? "#C7D9EE" : "#DADAE0"));
            Line(canvas, horizontal, y + 26, horizontal + width / zoom, y + 26, SKColor.Parse("#AEB2BC"));
            // Vector chevron: never depend on a missing glyph in a browser font.
            if (row.Collapsed) { Line(canvas, horizontal + 10, y + 8, horizontal + 15, y + 13); Line(canvas, horizontal + 15, y + 13, horizontal + 10, y + 18); }
            else { Line(canvas, horizontal + 8, y + 10, horizontal + 13, y + 15); Line(canvas, horizontal + 13, y + 15, horizontal + 18, y + 10); }
            hits.Add(new(network.Id, "network-toggle", new(horizontal, y, 26, 26)));
            canvas.Save(); canvas.ClipRect(new SKRect(horizontal + 28, y, horizontal + width / zoom - 10, y + 26));
            Text(canvas, row.Caption, horizontal + 30, y + 18, size: 12, bold: true); canvas.Restore();
            if (row.Collapsed) continue;
            canvas.Save(); canvas.ClipRect(new SKRect(horizontal + 22, y + 27, horizontal + width / zoom - 20, y + 54));
            Text(canvas, row.Comment, horizontal + 24, y + 45, SKColor.Parse("#66737D"), 11); canvas.Restore();
            float left = 46, merge = logicalWidth - 168, outputX = logicalWidth - 85, firstY = y + (float)LadderLayout.FirstRungY;
            float lastY = firstY + (network.Branches.Count - 1) * (float)LadderLayout.BranchSpacing;
            bool monitored = snapshot?.TryGetFlow(network.Id, out _) == true;
            Line(canvas, left, firstY - 21, left, lastY + 24, monitored ? Active : Ink, 2);
            if (network.Branches.Count > 1) Line(canvas, merge, firstY, merge, lastY, Flow(network.Id) ? Active : Ink);
            for (int branchIndex = 0; branchIndex < network.Branches.Count; branchIndex++)
            {
                var branch = network.Branches[branchIndex]; float lineY = firstY + branchIndex * (float)LadderLayout.BranchSpacing;
                if (lineY + 28 < scroll || lineY - 44 > scroll + height / zoom) continue;
                var visible = LadderLayout.VisibleContacts(branch.Count, horizontal, width / zoom);
                float wireX = visible.First == 0 ? left : (float)LadderLayout.Contact(visible.First - 1, lineY).X + 78;
                bool incoming = visible.First == 0 ? monitored : Flow(branch[visible.First - 1].Id);
                for (int j = visible.First; j < visible.End; j++)
                {
                    var instruction = branch[j]; var region = LadderLayout.Contact(j, lineY); float x = (float)region.X + 62;
                    hits.Add(new(instruction.Id, "instruction", region)); LastDrawnInstructions++;
                    if (selection == instruction.Id) Box(canvas, (float)region.X, (float)region.Y, (float)region.Width, (float)region.Height, SKColor.Parse("#EAF3FC"), SKColor.Parse("#397FB7"));
                    Line(canvas, wireX, lineY, x - 16, lineY, incoming ? Active : Ink);
                    var color = Flow(instruction.Id) ? Active : Ink;
                    Box(canvas, x - 16, lineY - 12, 32, 25, SKColors.White);
                    if (instruction.Kind is InstructionKind.Greater or InstructionKind.Less or InstructionKind.Equal)
                    {
                        Text(canvas, instruction.Kind == InstructionKind.Greater ? ">" : instruction.Kind == InstructionKind.Less ? "<" : "==", x - 10, lineY + 4, color, 16, true);
                        Text(canvas, instruction.Parameter.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), x - 9, lineY + 23, Teal, 10);
                    }
                    else
                    {
                        Line(canvas, x - 7, lineY - 10, x - 7, lineY + 10, color, 1.5f); Line(canvas, x + 7, lineY - 10, x + 7, lineY + 10, color, 1.5f);
                        if (instruction.Kind == InstructionKind.NegatedContact) Line(canvas, x - 13, lineY + 13, x + 13, lineY - 13, color, 1.5f);
                        if (instruction.Kind is InstructionKind.RisingEdge or InstructionKind.FallingEdge) Text(canvas, instruction.Kind == InstructionKind.RisingEdge ? "P" : "N", x - 4, lineY + 4, color, 10);
                    }
                    Operand(canvas, instruction.Tag, x - 58, lineY - 20, 116);
                    if (_tagLookup.TryGetValue(instruction.Tag, out var tag)) CenterText(canvas, tag.Address, x, lineY - 36, 116, Teal, 10);
                    incoming = Flow(instruction.Id); wireX = x + 16;
                }
                Line(canvas, wireX, lineY, visible.End == branch.Count ? merge : (float)LadderLayout.Contact(visible.End, lineY).X + 46, lineY, incoming ? Active : Ink);
                if (branchIndex > 0 && monitored) { Color(Flow(network.Id) ? Active : Ink); canvas.DrawCircle(merge, lineY, 2.2f, _paint); }
            }
            Line(canvas, merge, firstY, logicalWidth - 22, firstY, Flow(network.Id) ? Active : Ink);
            if (outputX + 62 < horizontal || outputX - 62 > horizontal + width / zoom || firstY + 49 < scroll || firstY - 45 > scroll + height / zoom) continue;
            var o = network.Output; var outputRegion = new RectD(outputX - 62, firstY - 45, 124, 94); hits.Add(new(o.Id, "instruction", outputRegion)); LastDrawnInstructions++;
            if (selection == o.Id) Box(canvas, (float)outputRegion.X, (float)outputRegion.Y, 124, 94, SKColor.Parse("#EAF3FC"), SKColor.Parse("#397FB7"));
            bool blockOutput = o.Kind is InstructionKind.TimerOn or InstructionKind.TimerOff or InstructionKind.Pulse or InstructionKind.CountUp or InstructionKind.Move;
            if (blockOutput)
            {
                Box(canvas, outputX - 46, firstY - 18, 92, 64, SKColor.Parse("#F1F3F5"), Flow(o.Id) ? Active : SKColor.Parse("#81919C"));
                Box(canvas, outputX - 46, firstY - 18, 92, 20, SKColor.Parse("#D9E0E8"));
                string caption = o.Kind switch { InstructionKind.TimerOn => "TON", InstructionKind.TimerOff => "TOF", InstructionKind.Pulse => "TP", InstructionKind.CountUp => "CTU", _ => "MOVE" };
                Text(canvas, caption, outputX - 16, firstY - 4, bold: true);
                Text(canvas, "IN", outputX - 40, firstY + 16, size: 10); Text(canvas, o.Kind == InstructionKind.Move ? "OUT" : "Q", outputX + 22, firstY + 16, size: 10);
                var parameter = o.Kind == InstructionKind.CountUp ? "R: " + o.Auxiliary : o.Kind == InstructionKind.Move ? "IN: " + o.Parameter : "PT: " + o.Parameter + " ms";
                canvas.Save(); canvas.ClipRect(new SKRect(outputX - 44, firstY + 20, outputX + 44, firstY + 46)); Text(canvas, parameter, outputX - 40, firstY + 35, Teal, 10); canvas.Restore();
            }
            else
            {
                Box(canvas, outputX - 17, firstY - 15, 34, 30, SKColors.White);
                Color(Flow(o.Id) ? Active : Ink, true, 1.5f);
                canvas.DrawArc(new SKRect(outputX - 12, firstY - 12, outputX + 3, firstY + 12), 100, 160, false, _paint);
                canvas.DrawArc(new SKRect(outputX - 3, firstY - 12, outputX + 12, firstY + 12), -80, 160, false, _paint);
                if (o.Kind is InstructionKind.SetCoil or InstructionKind.ResetCoil) Text(canvas, o.Kind == InstructionKind.SetCoil ? "S" : "R", outputX - 4, firstY + 4, size: 10);
            }
            Operand(canvas, o.Tag, outputX - 60, firstY - (blockOutput ? 27 : 23), 120);
            if (_tagLookup.TryGetValue(o.Tag, out var outTag)) CenterText(canvas, outTag.Address, outputX, firstY - (blockOutput ? 42 : 37), 120, Teal, 10);
        }
        if (block.Networks.Count == 0) { Text(canvas, "No networks in this block", 28, 40, size: 18); Text(canvas, "Insert a network from the toolbar or press Insert.", 28, 67, size: 12); }
        canvas.Restore(); return new(hits, (float)layout.Height);
    }
    private void Operand(SKCanvas canvas, string text, float x, float baseline, float width)
    {
        canvas.Save(); canvas.ClipRect(new SKRect(x, baseline - 13, x + width, baseline + 3));
        CenterText(canvas, "\"" + text + "\"", x + width / 2, baseline, width, Ink, 11); canvas.Restore();
    }
    public RenderResult Devices(SKCanvas canvas, float width, float height, ControlProject project, string? selection = null)
    {
        canvas.Clear(SKColor.Parse("#FAFBFC")); var hits = new List<HitRegion>();
        DrawGrid(canvas, width, height, SKColor.Parse("#DCE3E7"), .65f);
        foreach (var link in project.Links)
        {
            var a = project.Devices.Find(d => d.Id == link.From); var b = project.Devices.Find(d => d.Id == link.To); if (a is null || b is null) continue;
            float ax = (float)a.X + 100, bx = (float)b.X + 100, ay = (float)a.Y + 168, by = (float)b.Y + 168, busY = Math.Max(ay, by) + 50;
            Line(canvas, ax, ay, ax, busY, Active, 3); Line(canvas, ax, busY, bx, busY, Active, 3); Line(canvas, bx, busY, bx, by, Active, 3);
            Text(canvas, link.Subnet, (ax + bx) / 2 - 26, busY + 20, Active, 11);
        }
        foreach (var device in project.Devices)
        {
            float x = (float)device.X, y = (float)device.Y; hits.Add(new(device.Id, "device", new(x, y, 230, 172)));
            if (device.Id == selection) Box(canvas, x - 5, y - 29, 240, 202, SKColor.Parse("#E8F2FB"), SKColor.Parse("#468FC0"));
            Text(canvas, device.Name, x + 4, y - 10, Teal, 13, true);
            Box(canvas, x, y, 230, 136, SKColor.Parse("#454D54"), SKColor.Parse("#252C32"));
            Box(canvas, x + 8, y + 9, 214, 21, Teal); Text(canvas, device.Kind == DeviceKind.Hmi ? "CONTROLSPACE HMI" : "CONTROLSPACE / IO", x + 15, y + 24, SKColors.White, 10, true);
            if (device.Kind == DeviceKind.Hmi)
            {
                Box(canvas, x + 17, y + 43, 196, 77, SKColor.Parse("#DCE8EE")); Text(canvas, "Production overview", x + 27, y + 68, Teal, 12);
                Box(canvas, x + 29, y + 81, 58, 23, Teal); Text(canvas, "START", x + 41, y + 97, SKColors.White, 10);
            }
            else
            {
                for (int i = 0; i < Math.Min(device.Modules.Count, 5); i++)
                {
                    float mx = x + 10 + i * 43; Box(canvas, mx, y + 40, 38, 82, SKColor.Parse("#65727C"), SKColor.Parse("#222B33"));
                    for (int led = 0; led < 4; led++) { Color(led == 0 ? Active : SKColor.Parse("#A5ADAA")); canvas.DrawCircle(mx + 8, y + 50 + led * 10, 2, _paint); }
                    Text(canvas, (i + 1).ToString(), mx + 14, y + 111, SKColors.White, 11);
                }
            }
            Text(canvas, device.IpAddress, x + 5, y + 155, Teal, 11); Text(canvas, "Offline device configuration", x + 5, y + 172, SKColor.Parse("#738390"), 10);
        }
        return new(hits, height);
    }
    public RenderResult Hmi(SKCanvas canvas, float width, float height, HmiScreen screen, IReadOnlyList<PlcTag> tags, ScanSnapshot? snapshot, string? selection = null, bool runtime = false)
        => HmiView(canvas, width, height, screen, tags, snapshot, selection, runtime);
    public RenderResult HmiView(SKCanvas canvas, float width, float height, HmiScreen screen, IReadOnlyList<PlcTag> tags, IScanReadView? snapshot, string? selection = null, bool runtime = false)
    {
        CacheTags(tags);
        canvas.Clear(SKColor.Parse("#E1E5E8")); var hits = new List<HitRegion>();
        float scale = Math.Min((width - 48) / (float)screen.Width, (height - 48) / (float)screen.Height); scale = Math.Max(.1f, scale);
        canvas.Save(); canvas.Translate(24, 24); canvas.Scale(scale); Box(canvas, 0, 0, (float)screen.Width, (float)screen.Height, SKColor.Parse("#F7FAFC"), SKColor.Parse("#8D9DA7"));
        if (!runtime) DrawGrid(canvas, (float)screen.Width, (float)screen.Height, SKColor.Parse("#DFE8EC"), .7f);
        foreach (var o in screen.Objects)
        {
            float x = (float)o.X, y = (float)o.Y, w = (float)o.Width, h = (float)o.Height; var color = SKColor.TryParse(o.Color, out var parsed) ? parsed : Teal;
            int slot = _slotLookup.GetValueOrDefault(o.Tag, -1); double value = slot < 0 ? 0 : snapshot is not null && slot < snapshot.ValueCount ? snapshot.ReadValue(slot) : tags[slot].InitialValue;
            hits.Add(new(o.Id, "hmi", new(24 + x * scale, 24 + y * scale, w * scale, h * scale)));
            if (o.Runtime is not null && HmiRuntimeRules.IsNumeric(o.Kind)) DrawConfiguredHmiNumeric(canvas, o, value, color, runtime);
            else switch (o.Kind)
            {
                case HmiKind.Rectangle:
                case HmiKind.Ellipse:
                case HmiKind.Line:
                    DrawHmiShape(canvas, o, color); break;
                case HmiKind.Label: Text(canvas, o.Text, x, y + h * .72f, color, h > 35 ? 26 : 15, h > 35); break;
                case HmiKind.Button:
                    Box(canvas, x, y, w, h, value != 0 ? Active : color); Text(canvas, o.Text, x + 24, y + h / 2 + 6, SKColors.White, 17, true); break;
                case HmiKind.Lamp:
                    Box(canvas, x, y, w, h, SKColors.White, SKColor.Parse("#D7E2E7")); Color(value != 0 ? Active : SKColor.Parse("#A7B5BD")); canvas.DrawCircle(x + 23, y + h / 2, 7, _paint);
                    Text(canvas, o.Text, x + 41, y + h / 2 - 4, size: 12, bold: true); Text(canvas, value != 0 ? "Active" : "Inactive", x + 41, y + h / 2 + 17, Teal, 12); break;
                case HmiKind.Numeric:
                    Box(canvas, x, y, w, h, SKColors.White, SKColor.Parse("#D7E2E7")); Text(canvas, o.Text, x + 18, y + 23, Teal, 12, true); Text(canvas, value.ToString("0.##"), x + 18, y + 76, size: 38, bold: true); break;
                case HmiKind.Tank:
                    Box(canvas, x, y, w, h, SKColor.Parse("#DDECF0"), Teal); float fill = (float)Math.Clamp(value / 100, 0, 1) * h; Box(canvas, x + 3, y + h - fill, w - 6, fill, color); Text(canvas, o.Text, x + 12, y + 24, size: 14); break;
                case HmiKind.Gauge:
                    Box(canvas, x, y, w, h, SKColors.White, SKColor.Parse("#D7E2E7")); Text(canvas, o.Text, x + 20, y + 27, Teal, 12, true);
                    Text(canvas, value.ToString("0") + " %", x + 22, y + h * .64f, size: 46, bold: true);
                    Box(canvas, x + 20, y + h - 40, w - 40, 8, SKColor.Parse("#E1E9EE")); Box(canvas, x + 20, y + h - 40, (w - 40) * (float)Math.Clamp(value / 100, 0, 1), 8, color); break;
            }
            if (!runtime && o.Id == selection)
            {
                Color(SKColor.Parse("#308DCD"), true, 1 / scale); canvas.DrawRect(x - 2, y - 2, w + 4, h + 4, _paint);
                foreach (float hx in new[] { x, x + w }) foreach (float hy in new[] { y, y + h }) Box(canvas, hx - 3, hy - 3, 6, 6, SKColors.White, SKColor.Parse("#308DCD"));
            }
        }
        canvas.Restore(); return new(hits, height);
    }
    public void Trace(SKCanvas canvas, float width, float height, IReadOnlyList<ScanSnapshot> samples, IReadOnlyList<PlcTag> tags, string channel = "Speed_Actual")
    {
        canvas.Clear(SKColors.White); CacheTags(tags); int slot = _slotLookup.GetValueOrDefault(channel, -1);
        if (slot < 0) for (int i = 0; i < tags.Count; i++) if (tags[i].Type != PlcType.Bool) { slot = i; break; }
        if (slot < 0 && tags.Count > 0) slot = 0;
        if (slot < 0) { Text(canvas, "This project has no tags to trace.", 30, 45, Teal, 14); return; }
        channel = tags[slot].Name;
        float x0 = 62, y0 = 38, w = Math.Max(40, width - 100), h = Math.Max(40, height - 90);
        int start = Math.Max(0, samples.Count - 300), count = samples.Count - start;
        double maximum = 100, minimum = 0;
        for (int i = start; i < samples.Count; i++) if (slot < samples[i].Values.Length) { maximum = Math.Max(maximum, samples[i].Values[slot]); minimum = Math.Min(minimum, samples[i].Values[slot]); }
        double range = maximum - minimum;
        Text(canvas, channel + "  /  virtual scan time", x0, 22, Teal, 13, true);
        for (int i = 0; i <= 5; i++) { float y = y0 + i * h / 5; Line(canvas, x0, y, x0 + w, y, SKColor.Parse("#DFE6EC")); Text(canvas, (maximum - range * i / 5).ToString("0.#"), 20, y + 4, size: 11); }
        if (count < 2) { Text(canvas, "Start simulation to acquire trace samples.", x0 + 18, y0 + 30, size: 13); return; }
        using var path = new SKPath();
        for (int i = 0; i < count; i++) { float x = x0 + i * w / (count - 1), y = y0 + h * (1 - (float)((samples[start + i].Values[slot] - minimum) / range)); if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y); }
        Color(Teal, true, 2); canvas.DrawPath(path, _paint);
        Text(canvas, (samples[start].VirtualMilliseconds / 1000).ToString("0.0") + " s", x0, y0 + h + 24, size: 11);
        Text(canvas, (samples[^1].VirtualMilliseconds / 1000).ToString("0.0") + " s", x0 + w - 35, y0 + h + 24, size: 11);
    }
    public void Dispose() { ClearTextRuns(); _font.Dispose(); _bold.Dispose(); _paint.Dispose(); _face.Dispose(); _boldFace.Dispose(); }
}
