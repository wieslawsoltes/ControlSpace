using ControlSpace.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
namespace ControlSpace.Controls.Uno;

/// <summary>Compact engineering project navigator with keyboard-focusable entries.</summary>
public sealed class ProjectTree : UserControl
{
    private readonly StackPanel _rows = new() { Spacing = 1 };
    private string _filter = "";
    private ControlProject _project = DemoProject.Create();
    public event Action<string>? OpenRequested;
    public ProjectTree()
    {
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var search = new TextBox { PlaceholderText = "Search in project", FontSize = 12, MinHeight = 29, Margin = new Thickness(5) };
        AutomationProperties.SetName(search, "Search in project"); search.TextChanged += (_, _) => { _filter = search.Text; Rebuild(); }; root.Children.Add(search);
        var scroll = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); root.Children.Add(scroll); Content = root;
    }
    public void SetProject(ControlProject project) { _project = project; Rebuild(); }
    private void Rebuild()
    {
        _rows.Children.Clear();
        void Add(string caption, string command, int depth = 0, string icon = "▧")
        {
            if (_filter.Length > 0 && !caption.Contains(_filter, StringComparison.OrdinalIgnoreCase)) return;
            var button = EngineeringTheme.Button(new string(' ', depth * 3) + icon + "  " + caption, () => OpenRequested?.Invoke(command), "tree-" + command);
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Background = EngineeringTheme.Brush("F7F8F9"); button.BorderThickness = new Thickness(0); button.Padding = new Thickness(6, 3, 4, 3); button.MinHeight = 25;
            _rows.Children.Add(button);
        }
        Add(_project.Name, "portal", 0, "▾"); Add("Devices & networks", "devices", 1, "▦"); Add("PLC_1 [simulated controller]", "devices", 1, "▾");
        Add("Device configuration", "devices", 2); Add("Online & diagnostics", "diagnostics", 2, "◎"); Add("Program blocks", "blocks", 2, "▾");
        foreach (var block in _project.Blocks) Add(block.Name + " [" + (block.Language == BlockLanguage.LAD ? "OB" : "FC") + block.Number + "]", "block:" + block.Id, 3, "◈");
        Add("PLC tags", "tags", 2, "▾"); Add("Default tag table [" + _project.Tags.Count + "]", "tags", 3, "▤"); Add("Watch and force tables", "watch", 2, "▤");
        Add("Traces", "trace", 2, "⌁"); Add("Cross-references", "references", 2, "↔"); Add("HMI_1 [simulated panel]", "hmi", 1, "▾");
        Add("Screens", "hmi", 2, "▾"); foreach (var screen in _project.Screens) Add(screen.Name, "hmi", 3, "▣"); Add("Local library", "library", 1, "▧");
    }
}
