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
    public static Border ToolSeparator() => new()
    {
        Width = 1, Height = 19, Margin = new Thickness(4, 3, 4, 3), Background = Brush("B7B9C2")
    };
    /// <summary>Compact engineering command chrome with full accessible captions.</summary>
    public static Button ToolButton(string text, Action action, string id)
    {
        var button = Button(text, action, id, text);
        button.BorderThickness = new Thickness(0); button.Background = Brush("E5E5EA");
        button.Padding = new Thickness(6, 3, 6, 3); button.MinHeight = 25;
        if (id is "new" or "open" or "save" or "undo" or "redo")
        {
            button.Content = Label(text.Trim()[..1], 15); button.Width = 29;
        }
        if (id is "add-contact" or "add-nc-contact")
        {
            var icon = new Canvas { Width = 28, Height = 17 };
            void Line(double x1, double y1, double x2, double y2) => icon.Children.Add(new Microsoft.UI.Xaml.Shapes.Line
            { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Brush("35465A"), StrokeThickness = 1.25 });
            Line(0, 8.5, 8, 8.5); Line(20, 8.5, 28, 8.5); Line(9, 2, 9, 15); Line(19, 2, 19, 15);
            if (id == "add-nc-contact") Line(6, 16, 22, 1);
            button.Content = icon;
            string caption = id == "add-contact" ? "Normally open contact (F9)" : "Normally closed contact (F10)";
            ToolTipService.SetToolTip(button, caption); AutomationProperties.SetName(button, caption);
        }
        return button;
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
