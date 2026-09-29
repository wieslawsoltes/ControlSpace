using ControlSpace.Controls.Uno;
using ControlSpace.Core;
using ControlSpace.Engineering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using static ControlSpace.Controls.Uno.EngineeringTheme;
namespace ControlSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private void ShowPalette()
    {
        _palette.Children.Clear(); if (_taskPane is null) return;
        _taskPane.SetTitle(_layout.TaskCard == "Instructions" ? IsHmiView ? "Toolbox" : _view == "devices" ? "Hardware catalog" : "Instructions" : _layout.TaskCard);
        void Section(string text) => _palette.Children.Add(Header("▾  " + text));
        void Item(string text, Action action, InstructionKind? instruction = null)
        {
            if (_paletteFilter.Length > 0 && !text.Contains(_paletteFilter, StringComparison.OrdinalIgnoreCase)) return;
            if (instruction is InstructionKind k) { _palette.Children.Add(CreateInstructionPaletteItem(text, k)); return; }
            var b = Button(text, action, "palette-" + text); b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Left; b.BorderThickness = new Thickness(0); b.Background = Brush("F2F2F4"); b.MinHeight = 24; b.Padding = new Thickness(17, 2, 5, 2); _palette.Children.Add(b);
        }
        if (_layout.TaskCard == "Testing") { Section("Virtual CPU"); Item("Start simulation", Run); Item("Stop simulation", Stop); Item("Single scan", Step); Item("Watch and force table", () => Navigate("watch")); Item("Trace", () => Navigate("trace")); Item("Online & diagnostics", () => Navigate("diagnostics")); }
        else if (_layout.TaskCard == "Libraries") { Section("Project library"); foreach (var b in _workspace.Project.Blocks) Item(b.Name + " [" + b.Language + "]", () => Navigate("block:" + b.Id)); Section("Screens"); foreach (var screen in _workspace.Project.Screens) Item(screen.Name, () => Navigate("hmi:" + screen.Id)); }
        else if (IsHmiView) { Section("Basic objects"); foreach (var kind in Enum.GetValues<HmiKind>()) { var k = kind; Item(k.ToString(), () => AddHmi(k)); } }
        else if (_view == "devices") { Section("Virtual hardware"); Item("Add remote I/O station", AddDevice); Section("Configured devices"); foreach (var device in _workspace.Project.Devices) Item(device.Name + " · " + device.IpAddress, () => Select(device.Id)); }
        else
        {
            Section("Basic instructions"); _palette.Children.Add(Label("  ▾  Bit logic operations", 12, bold: true));
            foreach (var (name, kind) in new[] { ("Normally open contact", InstructionKind.Contact), ("Normally closed contact", InstructionKind.NegatedContact), ("Positive edge", InstructionKind.RisingEdge), ("Negative edge", InstructionKind.FallingEdge) }) Item(name, () => InsertContact(kind), kind);
            Section("Comparator operations"); foreach (var kind in new[] { InstructionKind.Greater, InstructionKind.Less, InstructionKind.Equal }) { var k = kind; Item(k.ToString(), () => InsertContact(k), k); }
            Section("Network operations"); Item("Insert network", AddNetwork); Item("Compile program", Compile);
            Section("Coils / timers / counters"); foreach (var kind in Enum.GetValues<InstructionKind>().Where(LadderInstructions.IsOutput)) { var k = kind; Item(InstructionCaption(k), () => ApplyLadderInstruction(k), k); }
        }
        var note = Label("Simulation only. No physical device connection.", 10, "73666B"); note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(4, 16, 4, 4); _palette.Children.Add(note);
    }
    private ControlProject? _instructionDragProject;
    private string _instructionDragState = "idle";
    private UIElement CreateInstructionPaletteItem(string caption, InstructionKind kind)
    {
        var item = new PaletteDragSource(caption, () => ApplyLadderInstruction(kind));
        Point CanvasPoint(Point point) => item.TransformToVisual(_canvas).TransformPoint(point);
        item.DragStarted += () => { _instructionDragProject = _workspace.Project; _instructionDragState = "started:" + kind; };
        item.DragMoved += point =>
        {
            if (_instructionDragProject is not null && _editor.Content == _graphics && _body.Visibility == Visibility.Visible)
                _canvas.PreviewInstructionDrop(kind, CanvasPoint(point), _instructionDragProject);
            else _canvas.ClearInstructionDropPreview();
        };
        item.Dropped += point =>
        {
            var expected = _instructionDragProject;
            string? target = expected is not null && _editor.Content == _graphics && _body.Visibility == Visibility.Visible
                ? _canvas.PreviewInstructionDrop(kind, CanvasPoint(point), expected) : null;
            _canvas.ClearInstructionDropPreview();
            if (target is not null) ApplyLadderInstruction(kind, target, expected);
            _instructionDragState = target is null || ReferenceEquals(expected, _workspace.Project) ? "completed:None" : "completed:Copy";
            _instructionDragProject = null;
        };
        item.DragCancelled += () => { _instructionDragState = "cancelled"; _instructionDragProject = null; _canvas.ClearInstructionDropPreview(); };
        item.Failed += ex => { _instructionDragState = "error:" + ex.Message; Message("Instruction drag failed: " + ex.Message); };
        ToolTipService.SetToolTip(item, "Click to insert at selection, or drag onto a LAD network. Escape cancels dragging.");
        return item;
    }
}
