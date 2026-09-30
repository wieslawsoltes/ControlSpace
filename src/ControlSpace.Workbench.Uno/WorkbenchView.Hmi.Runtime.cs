using System.Globalization;
using ControlSpace.Core;
using ControlSpace.Simulation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using static ControlSpace.Controls.Uno.EngineeringTheme;
namespace ControlSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private HmiRuntimeSession? _hmiSession;
    private bool _hmiRuntimeNavigation, _hmiNumericDialog;

    private void EndHmiRuntime()
    {
        SuspendHmiInput(); _hmiSession?.Dispose(); _hmiSession = null;
        _hmiRuntime = false; _canvas.HmiRuntime = false;
    }
    private void ValidateHmiSession()
    {
        if (_hmiSession is not null && (!ReferenceEquals(_hmiSession.Project, _workspace.Project) ||
            !ReferenceEquals(_hmiSession.Controller, _workspace.Controller))) EndHmiRuntime();
    }
    private void ApplyRuntimeScreen(HmiRuntimeSession session)
    {
        if (session.ScreenId == (_view.StartsWith("hmi:") ? _view[4..] : "")) return;
        _hmiRuntimeNavigation = true;
        try { Navigate("hmi:" + session.ScreenId); }
        finally { _hmiRuntimeNavigation = false; }
        _status.Text = "Runtime screen: " + ActiveHmi.Name + " · simulation only";
    }
    private async Task ActivateHmiRuntimeAsync(HmiRuntimeActivation activation)
    {
        try
        {
            if (!_hmiRuntime || _hmiSession is not { } session || _hmiNumericDialog || _programDialog is not null) return;
            if (!ReferenceEquals(_workspace.Project, activation.Project) || !ReferenceEquals(_workspace.Controller, activation.Controller) ||
                _view != "hmi:" + activation.ScreenId) return;
            session.ValidateActivation(activation);
            var o = ActiveHmi.Objects.First(o => o.Id == activation.ObjectId);
            if (HmiRuntimeRules.IsNumericInput(o))
            {
                using var entry = session.BeginNumericInput(o.Id);
                var panel = new StackPanel { Spacing = 8, MinWidth = 330, MaxWidth = 440 };
                var r = o.Runtime!;
                panel.Children.Add(Label(o.Tag, 14, "008C95", true));
                var limits = Label($"Range: {r.Minimum.ToString(CultureInfo.InvariantCulture)} … {r.Maximum.ToString(CultureInfo.InvariantCulture)} {r.Unit}\nMaximum decimal places: {r.DecimalPlaces}", 12);
                limits.TextWrapping = TextWrapping.Wrap; panel.Children.Add(limits);
                var field = DialogField(panel, "Value", entry.InitialText, "hmi-runtime-value");
                var note = Label("Input/marker write to the virtual controller only. The next program scan may overwrite a marker value. No forcing is created.", 11, "666676");
                note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
                _hmiNumericDialog = true;
                void Commit()
                {
                    if (!_hmiRuntime || !ReferenceEquals(session, _hmiSession) || !ReferenceEquals(session.Project, _workspace.Project) ||
                        !ReferenceEquals(session.Controller, _workspace.Controller) || _view != "hmi:" + session.ScreenId)
                        throw new InvalidOperationException("The runtime context changed. Reopen the numeric field.");
                    entry.Commit(field.Text); UpdateRuntime();
                }
                // Enter confirms, Escape cancels. Neither blur nor partial typing writes.
                field.KeyDown += (_, e) =>
                {
                    if (e.Key == VirtualKey.Enter)
                    {
                        try { Commit(); _programDialog?.Hide(); }
                        catch (Exception ex)
                        {
                            _programErrorText = ex.Message;
                            if (panel.Children.OfType<TextBlock>().LastOrDefault() is { } error) error.Text = ex.Message;
                        }
                        e.Handled = true;
                    }
                    else if (e.Key == VirtualKey.Escape) { _programDialog?.Hide(); e.Handled = true; }
                };
                field.Loaded += (_, _) => { field.Focus(FocusState.Keyboard); field.SelectAll(); };
                try { await ProgramDialogAsync("Enter process value", panel, "Write value", Commit); }
                finally { _hmiNumericDialog = false; }
            }
            else
            {
                session.ActivateButton(o.Id); ApplyRuntimeScreen(session); UpdateRuntime();
            }
        }
        catch (Exception ex) { Message(ex.Message); }
    }

    private sealed record HmiScreenChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }
    private static ComboBox HmiChoice<T>(StackPanel panel, string title, string id, T selected) where T : struct, Enum
    {
        panel.Children.Add(Label(title, 12, bold: true));
        var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 30 };
        foreach (var value in Enum.GetValues<T>()) box.Items.Add(value);
        box.SelectedItem = selected; AutomationProperties.SetAutomationId(box, id); AutomationProperties.SetName(box, title); panel.Children.Add(box);
        return box;
    }
    private async Task EditHmiRuntimeOptionsAsync()
    {
        try
        {
            RequireHmiDesign(); _canvas.CancelHmiInteraction(); CommitSource();
            var expected = _workspace.Project; var screen = ActiveHmi;
            var o = screen.Objects.FirstOrDefault(o => o.Id == _canvas.HmiSelection.FirstOrDefault()) ?? throw new InvalidOperationException("Select one HMI object first.");
            if (o.Kind != HmiKind.Button && !HmiRuntimeRules.IsNumeric(o.Kind)) throw new InvalidOperationException("Runtime settings are available for buttons and numeric displays.");
            var panel = new StackPanel { Spacing = 7, MinWidth = 360, MaxWidth = 480 };
            panel.Children.Add(Label(o.Kind + " · " + o.Text, 14, "008C95", true));
            var enabled = new CheckBox { Content = "Use configured runtime behavior", IsChecked = true }; panel.Children.Add(enabled); AutomationProperties.SetAutomationId(enabled, "hmi-options-enabled");
            var body = new StackPanel { Spacing = 6 };
            var settingsHost = new ContentControl { Content = body, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            panel.Children.Add(settingsHost);
            enabled.Checked += (_, _) => settingsHost.IsEnabled = true; enabled.Unchecked += (_, _) => settingsHost.IsEnabled = false;
            var r = o.Runtime ?? new HmiRuntimeOptions();
            ComboBox? action = null, mode = null, target = null;
            TextBox? write = null, minimum = null, maximum = null, places = null, unit = null;
            if (o.Kind == HmiKind.Button)
            {
                action = HmiChoice(body, "On completed click (Momentary uses press/release)", "hmi-options-action", r.Action);
                write = DialogField(body, "SetValue constant", r.WriteValue.ToString(CultureInfo.InvariantCulture), "hmi-options-value");
                body.Children.Add(Label("Target screen", 12, bold: true));
                target = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 30 };
                foreach (var s in expected.Screens) target.Items.Add(new HmiScreenChoice(s.Id, s.Name));
                target.SelectedItem = target.Items.OfType<HmiScreenChoice>().FirstOrDefault(s => s.Id == r.ScreenId);
                AutomationProperties.SetAutomationId(target, "hmi-options-screen"); AutomationProperties.SetName(target, "Target screen"); body.Children.Add(target);
            }
            else
            {
                if (o.Kind == HmiKind.Numeric) mode = HmiChoice(body, "I/O mode", "hmi-options-mode", r.IoMode);
                var geometry = new Grid { ColumnSpacing = 8 }; geometry.ColumnDefinitions.Add(new()); geometry.ColumnDefinitions.Add(new());
                var left = new StackPanel { Spacing = 6 }; var right = new StackPanel { Spacing = 6 }; geometry.Children.Add(left); Grid.SetColumn(right, 1); geometry.Children.Add(right); body.Children.Add(geometry);
                minimum = DialogField(left, "Minimum", r.Minimum.ToString(CultureInfo.InvariantCulture), "hmi-options-min");
                maximum = DialogField(right, "Maximum", r.Maximum.ToString(CultureInfo.InvariantCulture), "hmi-options-max");
                places = DialogField(left, "Decimal places (0–6)", r.DecimalPlaces.ToString(CultureInfo.InvariantCulture), "hmi-options-decimals");
                unit = DialogField(right, "Unit", r.Unit, "hmi-options-unit");
            }
            body.Children.Add(Label("Tag binding", 12, bold: true));
            var binding = new AutoSuggestBox { Text = o.Tag, PlaceholderText = "Unbound", MinHeight = 30 };
            AutomationProperties.SetAutomationId(binding, "hmi-options-tag"); AutomationProperties.SetName(binding, "Runtime tag binding"); body.Children.Add(binding);
            HmiRuntimeOptions Read()
            {
                if (o.Kind == HmiKind.Button)
                {
                    var a = (HmiButtonAction)action!.SelectedItem;
                    return new() { Action = a, WriteValue = a == HmiButtonAction.SetValue ? double.Parse(write!.Text, NumberStyles.Float, CultureInfo.InvariantCulture) : 0,
                        ScreenId = a == HmiButtonAction.ActivateScreen ? (target!.SelectedItem as HmiScreenChoice)?.Id ?? "" : "" };
                }
                return new() { IoMode = mode?.SelectedItem is HmiIoMode selected ? selected : HmiIoMode.Output,
                    Minimum = double.Parse(minimum!.Text, NumberStyles.Float, CultureInfo.InvariantCulture), Maximum = double.Parse(maximum!.Text, NumberStyles.Float, CultureInfo.InvariantCulture),
                    DecimalPlaces = int.Parse(places!.Text, NumberStyles.Integer, CultureInfo.InvariantCulture), Unit = unit!.Text.Trim() };
            }
            void UpdateChoices()
            {
                var a = action?.SelectedItem is HmiButtonAction selected ? selected : HmiButtonAction.Momentary;
                bool navigation = o.Kind == HmiKind.Button && a is HmiButtonAction.ActivateScreen or HmiButtonAction.PreviousScreen;
                if (write is not null) write.IsEnabled = a == HmiButtonAction.SetValue;
                if (target is not null) target.IsEnabled = a == HmiButtonAction.ActivateScreen;
                binding.IsEnabled = !navigation;
                if (navigation) binding.Text = "";
            }
            binding.TextChanged += (_, _) =>
            {
                var config = new HmiRuntimeOptions { Action = action?.SelectedItem is HmiButtonAction a ? a : HmiButtonAction.Momentary,
                    IoMode = mode?.SelectedItem is HmiIoMode m ? m : HmiIoMode.Output };
                binding.ItemsSource = expected.Tags.Where(t => HmiRuntimeRules.Accepts(o with { Runtime = config }, t) && t.Name.Contains(binding.Text, StringComparison.OrdinalIgnoreCase)).Take(30).Select(t => t.Name).ToArray();
            };
            binding.SuggestionChosen += (_, e) => binding.Text = e.SelectedItem?.ToString() ?? "";
            if (action is not null) action.SelectionChanged += (_, _) => UpdateChoices();
            UpdateChoices();
            var note = Label("Writes are limited to simulated inputs and markers. Output-image tags are read-only; forces must be released first. Numeric entries require explicit confirmation. Uncheck to restore legacy behavior.", 11, "666676"); note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
            await ProgramDialogAsync("HMI runtime settings", panel, "Apply", () =>
            {
                RequireHmiDesign();
                Hmi.UpdateObject(expected, screen.Id, o with { Runtime = enabled.IsChecked == true ? Read() : null, Tag = enabled.IsChecked == true ? binding.Text.Trim() : o.Tag });
                ShowHmiProperties();
            });
        }
        catch (Exception ex) { Message(ex.Message); }
    }
}
