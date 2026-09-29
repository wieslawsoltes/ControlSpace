using ControlSpace.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using static ControlSpace.Controls.Uno.EngineeringTheme;
namespace ControlSpace.Controls.Uno;

[Bindable]
public sealed class ProgramBlockRow(ProgramBlock block)
{
    public string Id => block.Id;
    public string Name => block.Name;
    public int Number => block.Number;
    public string Language => block.Language.ToString();
    public string Execution => block.Cyclic ? "Cyclic" : "Offline";
    public int Networks => block.Networks.Count;
}

/// <summary>Virtualized program-block directory. Commands are routed to the composing host.</summary>
public sealed class ProgramBlockBrowser : UserControl
{
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly TextBox _filter = new() { PlaceholderText = "Search program blocks", Width = 220, MinHeight = 28, FontSize = 12 };
    private readonly TextBlock _summary = Label("", 11);
    private ControlProject? _project;
    public string? SelectedId => (_list.SelectedItem as ProgramBlockRow)?.Id;
    public event Action<string, string?>? CommandRequested;
    public ProgramBlockBrowser()
    {
        var root = new Grid { Background = Brush("FFFFFF") };
        foreach (var height in new[] { 36d, 29, -1, 25 }) root.RowDefinitions.Add(new() { Height = height < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(height) });
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Padding = new Thickness(4) };
        foreach (var (text, command) in new[] { ("+ Add new block", "new"), ("Open", "open"), ("Properties", "properties"), ("Duplicate", "duplicate"), ("Delete", "delete"), ("↑", "up"), ("↓", "down") })
            tools.Children.Add(Button(text, () => CommandRequested?.Invoke(command, SelectedId), "blocks-" + command));
        tools.Children.Add(_filter); root.Children.Add(new ScrollViewer { Content = tools, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var header = new StackPanel { Orientation = Orientation.Horizontal, Background = Brush("DADAE0") };
        foreach (var (title, width) in new[] { ("Name", 230d), ("Number", 75d), ("Language", 85d), ("Simulation", 110d), ("Networks", 90d) })
        { var label = Label(title, 12, bold: true); header.Children.Add(new Border { Width = width, Padding = new Thickness(8, 2, 0, 2), Child = label }); }
        Grid.SetRow(header, 1); root.Children.Add(header);
        _list.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid Height="28" MinWidth="590">
                <Grid.ColumnDefinitions><ColumnDefinition Width="230"/><ColumnDefinition Width="75"/><ColumnDefinition Width="85"/><ColumnDefinition Width="110"/><ColumnDefinition Width="90"/></Grid.ColumnDefinitions>
                <TextBlock Text="{Binding Name}" FontSize="12" Margin="8,0" VerticalAlignment="Center" Foreground="#15639B" TextTrimming="CharacterEllipsis"/>
                <TextBlock Text="{Binding Number}" Grid.Column="1" FontSize="12" Margin="8,0" VerticalAlignment="Center"/>
                <TextBlock Text="{Binding Language}" Grid.Column="2" FontSize="12" Margin="8,0" VerticalAlignment="Center"/>
                <TextBlock Text="{Binding Execution}" Grid.Column="3" FontSize="12" Margin="8,0" VerticalAlignment="Center"/>
                <TextBlock Text="{Binding Networks}" Grid.Column="4" FontSize="12" Margin="8,0" VerticalAlignment="Center"/>
              </Grid>
            </DataTemplate>
            """);
        _list.ItemContainerStyle = (Style)XamlReader.Load("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
                <Setter Property="Padding" Value="0"/><Setter Property="MinHeight" Value="28"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/>
            </Style>
            """);
        _list.ContainerContentChanging += (_, e) => { if (e.Item is ProgramBlockRow row) { AutomationProperties.SetAutomationId(e.ItemContainer, "block-row-" + row.Id); AutomationProperties.SetName(e.ItemContainer, row.Name); } };
        _list.DoubleTapped += (_, _) => { if (SelectedId is not null) CommandRequested?.Invoke("open", SelectedId); };
        _list.SelectionChanged += (_, _) => { if (SelectedId is not null) CommandRequested?.Invoke("select", SelectedId); };
        AutomationProperties.SetAutomationId(_list, "program-block-list"); AutomationProperties.SetAutomationId(_filter, "program-block-filter");
        _filter.TextChanged += (_, _) => Rebuild(); Grid.SetRow(_list, 2); root.Children.Add(_list);
        _summary.Margin = new Thickness(8, 0, 0, 0); Grid.SetRow(_summary, 3); root.Children.Add(_summary); Content = root;
    }
    public void SetProject(ControlProject project) { if (ReferenceEquals(_project, project)) return; _project = project; Rebuild(); }
    public void Select(string id) { _list.SelectedItem = (_list.ItemsSource as IEnumerable<ProgramBlockRow>)?.FirstOrDefault(b => b.Id == id); }
    private void Rebuild()
    {
        var selected = SelectedId; var rows = (_project?.Blocks ?? []).Where(b => b.Name.Contains(_filter.Text, StringComparison.OrdinalIgnoreCase) || b.Language.ToString().Contains(_filter.Text, StringComparison.OrdinalIgnoreCase)).Select(b => new ProgramBlockRow(b)).ToArray();
        _list.ItemsSource = rows; _list.SelectedItem = rows.FirstOrDefault(b => b.Id == selected) ?? rows.FirstOrDefault();
        _summary.Text = $"{rows.Length} blocks  ·  Order is the simulator's cyclic execution order. Double-click to open.";
    }
}
