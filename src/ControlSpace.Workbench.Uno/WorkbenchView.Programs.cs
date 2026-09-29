using System.Globalization;
using ControlSpace.Core;
using ControlSpace.Engineering;
using ControlSpace.Controls.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using static ControlSpace.Controls.Uno.EngineeringTheme;
namespace ControlSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private readonly ProgramBlockBrowser _programs = new();
    private ContentDialog? _programDialog;
    private string _programErrorText = "";
    private ProgramEditor Programs => new(_workspace);
    private ProgramBlock ActiveLadder => _view.StartsWith("block:") && _workspace.Project.Blocks.FirstOrDefault(b => b.Id == _view[6..] && b.Language == BlockLanguage.LAD) is ProgramBlock b ? b : throw new InvalidOperationException("Open a LAD block to edit networks.");
    private LadderNetwork SelectedNetwork => ProgramEditor.FindNetwork(ActiveLadder, _selection) ?? ActiveLadder.Networks.FirstOrDefault() ?? throw new InvalidOperationException("Insert a network first.");
    private static TextBox DialogField(StackPanel content, string title, string text, string id, bool multiline = false)
    {
        content.Children.Add(Label(title, 12, bold: true));
        var field = new TextBox { AcceptsReturn = multiline, Text = text, FontSize = 13, MinHeight = multiline ? 84 : 30, MaxHeight = multiline ? 170 : 32, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
        AutomationProperties.SetAutomationId(field, id); AutomationProperties.SetName(field, title); content.Children.Add(field); return field;
    }
    private async Task ProgramDialogAsync(string title, StackPanel content, string action, Action apply)
    {
        if (_programDialog is not null) return;
        _programErrorText = "";
        var error = Label("", 12, "A42B2B"); error.TextWrapping = TextWrapping.Wrap;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = new ScrollViewer { Content = content, MaxHeight = 540 }, RequestedTheme = ElementTheme.Light, CornerRadius = new CornerRadius(0) };
        footer.Children.Add(Button(action, () => { try { apply(); dialog.Hide(); } catch (Exception ex) { error.Text = _programErrorText = ex.Message; } }, "program-dialog-apply"));
        footer.Children.Add(Button("Cancel", () => dialog.Hide(), "program-dialog-cancel")); content.Children.Add(error); content.Children.Add(footer);
        AutomationProperties.SetAutomationId(dialog, "program-dialog"); AutomationProperties.SetAutomationId(error, "program-dialog-error");
        _programDialog = dialog;
        try { await dialog.ShowAsync(); } catch (Exception ex) { Message(ex.Message); } finally { _programDialog = null; }
    }
    private void ProgramCommand(string command, string? id)
    {
        if (command == "new") { _ = EditBlockAsync(null); return; }
        if (id is null) { Message("Select a program block first."); return; }
        if (command == "properties") { _ = EditBlockAsync(id); return; }
        if (command == "delete") { _ = DeleteBlockAsync(id); return; }
        Safe(() =>
        {
            if (command == "open") Navigate("block:" + id);
            else if (command == "select") { var block = ProgramEditor.Block(_workspace.Project, id); _details.Text = $"{block.Name}\n{block.Language} · number {block.Number}\n{block.Networks.Count} networks\n{(block.Cyclic ? "Cyclic simulation" : "Offline block")}"; }
            else if (command == "duplicate") { CommitSource(); string copy = Programs.DuplicateBlock(_workspace.Project, id); Navigate("block:" + copy); }
            else if (command is "up" or "down") { CommitSource(); Programs.MoveBlock(_workspace.Project, id, command == "up" ? -1 : 1); _programs.Select(id); }
        });
    }
    private async Task EditBlockAsync(string? id)
    {
        try
        {
            CommitSource(); var expected = _workspace.Project; var block = id is null ? null : ProgramEditor.Block(expected, id);
            var content = new StackPanel { Spacing = 7, MinWidth = 350, MaxWidth = 490 };
            var name = DialogField(content, "Name", block?.Name ?? ProgramEditor.NextName(expected), "block-name");
            content.Children.Add(Label("Language", 12, bold: true)); var language = new ComboBox { MinWidth = 180, IsEnabled = block is null };
            language.Items.Add(BlockLanguage.LAD); language.Items.Add(BlockLanguage.SCL); language.SelectedItem = block?.Language ?? BlockLanguage.LAD;
            AutomationProperties.SetAutomationId(language, "block-language"); content.Children.Add(language);
            var number = DialogField(content, "Number", (block?.Number ?? ProgramEditor.NextNumber(expected)).ToString(CultureInfo.InvariantCulture), "block-number");
            var automatic = new CheckBox { Content = "Automatic number", IsChecked = block is null, Visibility = block is null ? Visibility.Visible : Visibility.Collapsed }; number.IsEnabled = block is not null;
            automatic.Checked += (_, _) => number.IsEnabled = false; automatic.Unchecked += (_, _) => number.IsEnabled = true; content.Children.Add(automatic); AutomationProperties.SetAutomationId(automatic, "block-automatic");
            var cyclic = new CheckBox { Content = "Execute cyclically in the simulator", IsChecked = block?.Cyclic ?? false }; content.Children.Add(cyclic); AutomationProperties.SetAutomationId(cyclic, "block-cyclic");
            var open = new CheckBox { Content = "Add new and open", IsChecked = true, Visibility = block is null ? Visibility.Visible : Visibility.Collapsed }; content.Children.Add(open); AutomationProperties.SetAutomationId(open, "block-open");
            var note = Label("Supported LAD/SCL programs only. FB/DB interfaces, block calls and Siemens execution classes are not emulated.", 11, "6E6E79"); note.TextWrapping = TextWrapping.Wrap; content.Children.Add(note);
            await ProgramDialogAsync(block is null ? "Add new block" : "Block properties", content, block is null ? "Create" : "Apply", () =>
            {
                int? n = block is null && automatic.IsChecked == true ? null : int.Parse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture);
                if (block is null) { var created = Programs.AddBlock(expected, name.Text, (BlockLanguage)language.SelectedItem, n, cyclic.IsChecked == true); if (open.IsChecked == true) Navigate("block:" + created); }
                else Programs.UpdateBlock(expected, block.Id, name.Text, n!.Value, cyclic.IsChecked == true);
            });
        }
        catch (Exception ex) { Message(ex.Message); }
    }
    private async Task DeleteBlockAsync(string id)
    {
        try
        {
            CommitSource(); var expected = _workspace.Project; var b = ProgramEditor.Block(expected, id); var content = new StackPanel { Spacing = 10, MinWidth = 330 };
            var text = Label($"Delete {b.Name} and all {b.Networks.Count} networks?\nThe deletion is undoable.", 13); text.TextWrapping = TextWrapping.Wrap; content.Children.Add(text);
            await ProgramDialogAsync("Delete program block", content, "Delete", () => Programs.DeleteBlock(expected, id));
        }
        catch (Exception ex) { Message(ex.Message); }
    }
    private bool ShowLadderProperties(string id)
    {
        var block = _workspace.Project.Blocks.FirstOrDefault(b => b.Language == BlockLanguage.LAD && ProgramEditor.FindNetwork(b, id) is not null); if (block is null) return false;
        var n = ProgramEditor.FindNetwork(block, id)!;
        var instruction = n.Branches.SelectMany(b => b).Append(n.Output).FirstOrDefault(i => i.Id == id);
        _properties.Children.Add(Label($"Network {block.Networks.IndexOf(n) + 1} · {n.Title}", 14, bold: true));
        if (instruction is not null)
        {
            _properties.Children.Add(PropertyRow("Instruction", Label(InstructionCaption(instruction.Kind)))); _properties.Children.Add(PropertyRow("Operand", Label(instruction.Tag)));
            _properties.Children.Add(Button("Edit instruction / operand…", () => _ = EditLadderAsync(id), "instruction-properties"));
            if (n.Output.Id != id)
            {
                var moves = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                moves.Children.Add(Button("Move left", () => LadderCommand("contact-left"), "contact-left")); moves.Children.Add(Button("Move right", () => LadderCommand("contact-right"), "contact-right"));
                moves.Children.Add(Button("Delete contact", () => LadderCommand("delete"), "contact-delete")); _properties.Children.Add(moves);
            }
        }
        else
        {
            _properties.Children.Add(PropertyRow("Parallel paths", Label(n.Branches.Count.ToString())));
            _properties.Children.Add(Button("Edit title and comment…", () => _ = EditLadderAsync(id), "network-properties"));
        }
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        tools.Children.Add(Button("Add parallel path", () => LadderCommand("branch-add"), "branch-add"));
        tools.Children.Add(Button("Delete parallel path", () => LadderCommand("branch-delete"), "branch-delete"));
        tools.Children.Add(Button("Duplicate network", () => LadderCommand("network-duplicate"), "network-duplicate"));
        tools.Children.Add(Button("Delete network…", () => LadderCommand("network-delete"), "network-delete")); _properties.Children.Add(tools);
        return true;
    }
    private static string InstructionCaption(InstructionKind kind) => kind switch
    {
        InstructionKind.Contact => "Normally open contact", InstructionKind.NegatedContact => "Normally closed contact",
        InstructionKind.RisingEdge => "Positive edge", InstructionKind.FallingEdge => "Negative edge", InstructionKind.Coil => "Assignment coil",
        InstructionKind.SetCoil => "Set coil", InstructionKind.ResetCoil => "Reset coil", InstructionKind.TimerOn => "TON · On-delay",
        InstructionKind.TimerOff => "TOF · Off-delay", InstructionKind.Pulse => "TP · Pulse", InstructionKind.CountUp => "CTU · Count up", InstructionKind.Move => "MOVE · Constant", _ => kind.ToString()
    };
    private async Task EditLadderAsync(string id)
    {
        try
        {
            var expected = _workspace.Project; var b = ActiveLadder; var n = ProgramEditor.FindNetwork(b, id) ?? throw new ArgumentException("Select a network or instruction.");
            var content = new StackPanel { Spacing = 7, MinWidth = 350, MaxWidth = 490 };
            if (n.Id == id)
            {
                var title = DialogField(content, "Network title", n.Title, "network-title");
                var comment = DialogField(content, "Comment", n.Comment, "network-comment", true);
                await ProgramDialogAsync($"Network {b.Networks.IndexOf(n) + 1}", content, "Apply", () => { Programs.UpdateNetwork(expected, b.Id, n.Id, title.Text, comment.Text); SelectLadder(n.Id); });
                return;
            }
            var instruction = n.Branches.SelectMany(b => b).Append(n.Output).First(i => i.Id == id); bool output = n.Output.Id == id;
            content.Children.Add(Label("Instruction", 12, bold: true)); var kind = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var value in Enum.GetValues<InstructionKind>().Where(k => output ? LadderInstructions.IsOutput(k) : LadderInstructions.IsContact(k))) { var item = new ComboBoxItem { Content = InstructionCaption(value), Tag = value }; kind.Items.Add(item); if (value == instruction.Kind) kind.SelectedItem = item; }
            AutomationProperties.SetAutomationId(kind, "instruction-kind"); content.Children.Add(kind);
            content.Children.Add(Label("Operand / PLC tag", 12, bold: true));
            var operand = new AutoSuggestBox { Text = instruction.Tag, PlaceholderText = "Type a tag name", MinHeight = 32 }; AutomationProperties.SetAutomationId(operand, "instruction-operand"); content.Children.Add(operand);
            var parameter = DialogField(content, "Preset (milliseconds) / comparison or MOVE constant", instruction.Parameter.ToString(CultureInfo.InvariantCulture), "instruction-parameter");
            var auxiliary = DialogField(content, "Elapsed TIME / reset BOOL tag (optional)", instruction.Auxiliary, "instruction-auxiliary");
            InstructionKind Current() => (InstructionKind)((ComboBoxItem)kind.SelectedItem).Tag;
            void Fields()
            {
                var k = Current(); parameter.IsEnabled = LadderInstructions.IsTimer(k) || k is InstructionKind.Greater or InstructionKind.Less or InstructionKind.Equal or InstructionKind.Move;
                auxiliary.IsEnabled = LadderInstructions.IsTimer(k) || k == InstructionKind.CountUp;
            }
            Fields(); kind.SelectionChanged += (_, _) =>
            {
                var k = Current(); if (!expected.Tags.Any(t => t.Name.Equals(operand.Text, StringComparison.OrdinalIgnoreCase) && LadderInstructions.Accepts(k, t))) operand.Text = expected.Tags.FirstOrDefault(t => LadderInstructions.Accepts(k, t))?.Name ?? "";
                parameter.Text = LadderInstructions.IsTimer(k) ? "1000" : "0"; auxiliary.Text = ""; Fields();
            };
            operand.TextChanged += (_, args) => { if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) operand.ItemsSource = expected.Tags.Where(t => LadderInstructions.Accepts(Current(), t) && (t.Name.Contains(operand.Text, StringComparison.OrdinalIgnoreCase) || t.Address.Contains(operand.Text, StringComparison.OrdinalIgnoreCase))).Take(40).Select(t => t.Name).ToArray(); };
            operand.SuggestionChosen += (_, args) => operand.Text = (string)args.SelectedItem;
            await ProgramDialogAsync("Instruction properties", content, "Apply", () =>
            {
                var next = instruction with { Kind = Current(), Tag = operand.Text.Trim(), Parameter = parameter.IsEnabled ? double.Parse(parameter.Text, NumberStyles.Float, CultureInfo.InvariantCulture) : 0, Auxiliary = auxiliary.IsEnabled ? auxiliary.Text.Trim() : "" };
                Programs.UpdateInstruction(expected, b.Id, n.Id, next); SelectLadder(id);
            });
        }
        catch (Exception ex) { Message(ex.Message); }
    }
    private void SelectLadder(string id) { _selection = id; _canvas.Reveal(id); Select(id); }
    private void ApplyLadderInstruction(InstructionKind kind, string? selection = null, ControlProject? expected = null) => Safe(() =>
    {
        if (expected is not null) expected = _instructionDragProject ?? expected;
        if (expected is not null && !ReferenceEquals(expected, _workspace.Project)) throw new InvalidOperationException("Project changed during drag.");
        var b = ActiveLadder; var n = selection is null ? SelectedNetwork : ProgramEditor.FindNetwork(b, selection) ?? throw new ArgumentException("Drop onto a LAD network.");
        string chosen = selection ?? _selection;
        if (LadderInstructions.IsContact(kind)) { string? after = n.Branches.SelectMany(p => p).Any(i => i.Id == chosen) ? chosen : null; SelectLadder(Programs.InsertContact(_workspace.Project, b.Id, n.Id, kind, after)); }
        else
        {
            string? tag = _workspace.Project.Tags.Any(t => t.Name.Equals(n.Output.Tag, StringComparison.OrdinalIgnoreCase) && LadderInstructions.Accepts(kind, t)) ? n.Output.Tag : null;
            var instruction = LadderInstructions.Create(_workspace.Project, kind, tag) with { Id = n.Output.Id }; Programs.UpdateInstruction(_workspace.Project, b.Id, n.Id, instruction); SelectLadder(instruction.Id);
        }
    });
    private void LadderCommand(string command)
    {
        if (command == "edit") { Safe(() => _ = EditLadderAsync(_selection.Length == 0 ? SelectedNetwork.Id : _selection)); return; }
        if (command == "network-delete" || command == "delete" && _view.StartsWith("block:") && _workspace.Project.Blocks.FirstOrDefault(b => "block:" + b.Id == _view)?.Networks.Any(n => n.Id == _selection) == true) { _ = DeleteNetworkAsync(); return; }
        Safe(() =>
        {
            if (command == "contact") { ApplyLadderInstruction(InstructionKind.Contact); return; }
            if (command == "negated") { ApplyLadderInstruction(InstructionKind.NegatedContact); return; }
            if (command == "network-add") { var block = ActiveLadder; var after = ProgramEditor.FindNetwork(block, _selection)?.Id; SelectLadder(Programs.AddNetwork(_workspace.Project, block.Id, after)); return; }
            var b = ActiveLadder; var n = SelectedNetwork;
            if (command == "network-duplicate") SelectLadder(Programs.DuplicateNetwork(_workspace.Project, b.Id, n.Id));
            else if (command is "network-up" or "network-down") { Programs.MoveNetwork(_workspace.Project, b.Id, n.Id, command == "network-up" ? -1 : 1); SelectLadder(n.Id); }
            else if (command == "branch-add") { int branch = n.Branches.FindIndex(p => p.Any(i => i.Id == _selection)); SelectLadder(Programs.AddBranch(_workspace.Project, b.Id, n.Id, branch)); }
            else if (command == "branch-delete") { int branch = n.Branches.FindIndex(p => p.Any(i => i.Id == _selection)); if (branch < 0) throw new InvalidOperationException("Select a contact in the path to delete."); Programs.DeleteBranch(_workspace.Project, b.Id, n.Id, branch); SelectLadder(n.Id); }
            else if (command == "delete") { Programs.DeleteContact(_workspace.Project, b.Id, n.Id, _selection); SelectLadder(n.Id); }
            else if (command is "contact-left" or "contact-right") { string selected = _selection; Programs.MoveContact(_workspace.Project, b.Id, n.Id, selected, command == "contact-left" ? -1 : 1); SelectLadder(selected); }
        });
    }
    private async Task DeleteNetworkAsync()
    {
        try
        {
            var expected = _workspace.Project; var b = ActiveLadder; var n = SelectedNetwork;
            var content = new StackPanel { Spacing = 8, MinWidth = 330 }; content.Children.Add(Label($"Delete network {b.Networks.IndexOf(n) + 1}: {n.Title}?", 13)); content.Children.Add(Label("The network and all its instructions can be restored with Undo.", 11));
            await ProgramDialogAsync("Delete network", content, "Delete", () => { Programs.DeleteNetwork(expected, b.Id, n.Id); _selection = ""; _canvas.Selection = null; _canvas.ChangeView(); ShowGeneralProperties(); });
        }
        catch (Exception ex) { Message(ex.Message); }
    }
    private void LadderContext(string id, Point point)
    {
        var menu = new MenuFlyout();
        foreach (var (text, command) in new[] { ("Properties…", "edit"), ("Insert network", "network-add"), ("Duplicate network", "network-duplicate"), ("Move network up", "network-up"), ("Move network down", "network-down"), ("Add parallel path", "branch-add"), ("Delete parallel path", "branch-delete"), ("Delete contact", "delete"), ("Delete network…", "network-delete") })
        { var item = new MenuFlyoutItem { Text = text }; item.Click += (_, _) => LadderCommand(command); menu.Items.Add(item); }
        menu.ShowAt(_canvas, new FlyoutShowOptions { Position = point });
    }
}
