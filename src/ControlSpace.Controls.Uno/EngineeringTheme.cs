using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace ControlSpace.Controls.Uno;

public static class EngineeringTheme
{
    public static SolidColorBrush Brush(string hex)
    {
        hex = hex.TrimStart('#'); byte r = Convert.ToByte(hex[..2], 16), g = Convert.ToByte(hex[2..4], 16), b = Convert.ToByte(hex[4..6], 16);
        return new(Windows.UI.Color.FromArgb(255, r, g, b));
    }
    public static TextBlock Label(string text, double size = 12, string color = "26333E", bool bold = false) => new()
    {
        Text = text, FontSize = size, Foreground = Brush(color), FontFamily = new FontFamily("Arial"),
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
    };
    public static Button Button(string text, Action action, string? automationId = null, string? tip = null)
    {
        var button = new Button { Content = text, FontSize = 12, FontFamily = new FontFamily("Arial"), Padding = new Thickness(7, 3, 7, 3), MinHeight = 26, MinWidth = 24, CornerRadius = new CornerRadius(0), Background = Brush("E7E7EA"), BorderBrush = Brush("B7B7BF"), BorderThickness = new Thickness(1) };
        button.Click += (_, _) => action(); AutomationProperties.SetName(button, text); if (automationId is not null) AutomationProperties.SetAutomationId(button, automationId);
        if (tip is not null) ToolTipService.SetToolTip(button, tip); return button;
    }
    public static Border Header(string caption, string color = "D7D7DC") => new()
    {
        Background = Brush(color), Padding = new Thickness(7, 3, 7, 3), BorderBrush = Brush("ABB6BE"), BorderThickness = new Thickness(0, 0, 0, 1), Child = Label(caption, 12, bold: true)
    };
    public static Border Pane(string title, UIElement content)
    {
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(Header(title)); Grid.SetRow(content, 1); grid.Children.Add(content);
        return new() { Child = grid, Background = Brush("F7F8F9"), BorderBrush = Brush("9FAEB9"), BorderThickness = new Thickness(1) };
    }
}
