using ControlSpace.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Windows.System;
using static ControlSpace.Controls.Uno.EngineeringTheme;
namespace ControlSpace.Controls.Uno;

[Bindable]
public sealed record HmiExplorerRow(string Id, string Name, string Detail);

/// <summary>Virtualized screen directory or multi-selection object list. Paint order is bottom to top in the model.</summary>
public sealed class HmiExplorer : UserControl
{
    private readonly ListView _list;
    private readonly TextBox _search = new() { PlaceholderText = "Search", MinHeight = 28, FontSize = 12, Margin = new Thickness(4) };
    private readonly bool _objects;
    private HmiExplorerRow[] _rows = [];
    private bool _sync;
    public IReadOnlyList<string> SelectedIds => _list.SelectedItems.OfType<HmiExplorerRow>().Select(r => r.Id).ToArray();
    public event Action<string, IReadOnlyList<string>>? CommandRequested;
    public HmiExplorer(bool objects = false)
    {
        _objects = objects;
        _list = new ListView { SelectionMode = objects ? ListViewSelectionMode.Extended : ListViewSelectionMode.Single };
        var grid = new Grid { Background = Brush("F7F7F9") };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(_search); Grid.SetRow(_list, 1); grid.Children.Add(_list); Content = grid;
        _list.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <StackPanel Height="42" Margin="8,2" Spacing="2">
                <TextBlock Text="{Binding Name}" FontSize="12" Foreground="#24557D" TextTrimming="CharacterEllipsis"/>
                <TextBlock Text="{Binding Detail}" FontSize="10" Foreground="#666A73" TextTrimming="CharacterEllipsis"/>
              </StackPanel>
            </DataTemplate>
            """);
        _list.ItemContainerStyle = (Style)XamlReader.Load("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
              <Setter Property="Padding" Value="0"/><Setter Property="MinHeight" Value="44"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/>
            </Style>
            """);
        _list.ContainerContentChanging += (_, e) => { if (e.Item is HmiExplorerRow row) { AutomationProperties.SetAutomationId(e.ItemContainer, (_objects ? "hmi-object-row-" : "hmi-screen-row-") + row.Id); AutomationProperties.SetName(e.ItemContainer, row.Name + ", " + row.Detail); } };
        _list.SelectionChanged += (_, _) => { if (!_sync) CommandRequested?.Invoke("select", SelectedIds); };
        _list.DoubleTapped += (_, _) => CommandRequested?.Invoke(_objects ? "properties" : "open", SelectedIds);
        _list.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { CommandRequested?.Invoke(_objects ? "properties" : "open", SelectedIds); e.Handled = true; } };
        _search.TextChanged += (_, _) => Rebuild();
        AutomationProperties.SetAutomationId(_search, objects ? "hmi-object-search" : "hmi-screen-search"); AutomationProperties.SetAutomationId(_list, objects ? "hmi-object-list" : "hmi-screen-list");
    }
    public void SetProject(ControlProject p)
    {
        _rows = p.Screens.Select(s => new HmiExplorerRow(s.Id, s.Name, $"{s.Width:0} × {s.Height:0} px  ·  {s.Objects.Count} objects")).ToArray(); Rebuild();
    }
    public void SetScreen(HmiScreen screen)
    {
        _rows = screen.Objects.AsEnumerable().Reverse().Select(o => new HmiExplorerRow(o.Id, o.Text.Length == 0 ? o.Kind.ToString() : o.Text, $"{o.Kind}  ·  {o.X:0}, {o.Y:0}  ·  {o.Width:0} × {o.Height:0}  {o.Tag}")).ToArray(); Rebuild();
    }
    private void Rebuild()
    {
        var selected = SelectedIds; _sync = true;
        try { _list.ItemsSource = _rows.Where(r => r.Name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase) || r.Detail.Contains(_search.Text, StringComparison.OrdinalIgnoreCase)).ToArray(); }
        finally { _sync = false; }
        Select(selected);
    }
    public void Select(IEnumerable<string> ids)
    {
        var selected = ids.ToHashSet(); _sync = true;
        try
        {
            var rows = (_list.ItemsSource as IEnumerable<HmiExplorerRow>) ?? [];
            if (!_objects) _list.SelectedItem = rows.FirstOrDefault(r => selected.Contains(r.Id));
            else if (rows.Any() && rows.All(row => selected.Contains(row.Id))) _list.SelectAll();
            else { _list.SelectedItems.Clear(); foreach (var row in rows) if (selected.Contains(row.Id)) _list.SelectedItems.Add(row); }
        }
        finally { _sync = false; }
    }
}
