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
    private static readonly FontFamily Symbols = new("ms-appx:///Uno.Fonts.Fluent/Fonts/uno-fluentui-assets.ttf");
    private static readonly Dictionary<char, string> Glyphs = new()
    {
        ['▱'] = "\uE8B7", ['▰'] = "\uE8E5", ['▣'] = "\uE74E", ['▦'] = "\uE968",
        ['↶'] = "\uE7A7", ['↷'] = "\uE7A6", ['✓'] = "\uE73E", ['▶'] = "\uE768",
        ['■'] = "\uE71A", ['▷'] = "\uE893", ['◉'] = "\uE7F4", ['◇'] = "\uE8A5",
        ['≡'] = "\uE943", ['▤'] = "\uE8A1", ['∿'] = "\uE9D9", ['↔'] = "\uE8AB",
        ['▥'] = "\uE8F1", ['▾'] = "\uE70D", ['▸'] = "\uE76C", ['□'] = "\uE922", ['◀'] = "\uE76B"
    };
    // Use the host's packaged text font and symbol font, not OS-specific fallbacks.
    public static TextBlock Label(string text, double size = 12, string color = "26333E", bool bold = false)
    {
        var label = new TextBlock
        {
            Text = text, FontSize = size, Foreground = Brush(color),
            FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (text.Length == 1 && Glyphs.TryGetValue(text[0], out var glyph)) { label.Text = glyph; label.FontFamily = Symbols; }
        return label;
    }
    private static UIElement Caption(string text, double size = 12, bool bold = false)
    {
        string trimmed = text.TrimStart();
        if (trimmed.Length > 1 && Glyphs.ContainsKey(trimmed[0]) && char.IsWhiteSpace(trimmed[1]))
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            panel.Children.Add(Label(trimmed[..1], size)); panel.Children.Add(Label(trimmed[1..].TrimStart(), size, bold: bold)); return panel;
        }
        return Label(text, size, bold: bold);
    }
    public static Button Button(string text, Action action, string? automationId = null, string? tip = null)
    {
        var button = new Button { Content = Caption(text), FontSize = 12, Padding = new Thickness(7, 3, 7, 3), MinHeight = 26, MinWidth = 24, CornerRadius = new CornerRadius(0), Background = Brush("E7E7EA"), BorderBrush = Brush("B7B7BF"), BorderThickness = new Thickness(1) };
        button.Click += (_, _) => action(); AutomationProperties.SetName(button, text); if (automationId is not null) AutomationProperties.SetAutomationId(button, automationId);
        if (tip is not null) ToolTipService.SetToolTip(button, tip); return button;
    }
    public static Border Header(string caption, string color = "D7D7DC") => new()
    {
        Background = Brush(color), Padding = new Thickness(7, 3, 7, 3), BorderBrush = Brush("ABB6BE"), BorderThickness = new Thickness(0, 0, 0, 1), Child = Caption(caption, 12, bold: true)
    };
    public static Border Pane(string title, UIElement content)
    {
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(Header(title)); Grid.SetRow(content, 1); grid.Children.Add(content);
        return new() { Child = grid, Background = Brush("F7F8F9"), BorderBrush = Brush("9FAEB9"), BorderThickness = new Thickness(1) };
    }
}
