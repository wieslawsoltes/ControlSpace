using ControlSpace.Core;
using ControlSpace.Simulation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
namespace ControlSpace.Controls.Uno;

public sealed class EngineeringTable : UserControl
{
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
    private readonly Dictionary<string, TextBlock> _values = new(StringComparer.OrdinalIgnoreCase);
    public event Action<string>? TagSelected;
    public EngineeringTable()
    {
        _list.ItemClick += (_, e) => { if (e.ClickedItem is Grid { Tag: string tag }) TagSelected?.Invoke(tag); };
        Content = _list;
    }
    private static Grid Row(params string[] columns)
    {
        var grid = new Grid { MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Stretch };
        var widths = new[] { 190d, 80, 100, 100, 320 };
        for (int i = 0; i < columns.Length; i++)
        {
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(widths[Math.Min(i, widths.Length - 1)]) });
            var text = EngineeringTheme.Label(columns[i]); text.Margin = new Thickness(7, 4, 7, 4); Grid.SetColumn(text, i); grid.Children.Add(text);
        }
        return grid;
    }
    public void SetTags(IReadOnlyList<PlcTag> tags, VirtualPlc? controller = null)
    {
        _list.Items.Clear(); _values.Clear();
        var header = Row("Name", "Data type", "Address", "Monitor value", "Comment"); header.Background = EngineeringTheme.Brush("DDE2E7"); _list.Items.Add(header);
        for (int i = 0; i < tags.Count; i++)
        {
            var tag = tags[i]; var row = Row(tag.Name, tag.Type.ToString(), tag.Address, PlcValues.Format(tag.Type, controller?.Read(tag.Name) ?? tag.InitialValue), tag.Comment); row.Tag = tag.Name;
            AutomationProperties.SetName(row, tag.Name); row.Background = EngineeringTheme.Brush(i % 2 == 0 ? "FFFFFF" : "F1F4F6");
            _values[tag.Name] = (TextBlock)row.Children[3]; _list.Items.Add(row);
        }
    }
    public void UpdateValues(IReadOnlyList<PlcTag> tags, VirtualPlc? controller)
    {
        foreach (var tag in tags) if (_values.TryGetValue(tag.Name, out var text)) { string value = PlcValues.Format(tag.Type, controller?.Read(tag.Name) ?? tag.InitialValue); if (text.Text != value) text.Text = value; }
    }
    public void SetReferences(IEnumerable<ControlSpace.Engineering.SymbolReference> references)
    {
        _list.Items.Clear(); _values.Clear(); _list.Items.Add(Row("Tag", "Access", "Block", "Instruction", "Network"));
        foreach (var r in references) _list.Items.Add(Row(r.Tag, r.Write ? "Write" : "Read", r.Block, r.Instruction, r.Network));
    }
}
