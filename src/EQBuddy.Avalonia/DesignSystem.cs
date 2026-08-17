using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using EQBuddy.UI.Shared;

namespace EQBuddy.Avalonia;

/// <summary>
/// The Avalonia composition of <see cref="DesignTokens"/> and <see cref="IconPaths"/> —
/// the same job <see cref="AppTheme"/> does for colour, for the other four axes, and the
/// exact mirror of the WPF <c>DesignSystem</c>.
///
/// Nothing here decides anything. Every number and every path comes out of UI.Shared, so
/// the two windows cannot disagree about what "Body" means or what a card's radius is.
/// The alternative is what actually happened once: the Avalonia chip stacks shipped a
/// hand-copied older version of the WPF anchor and carried #122 and #152 to Linux and
/// macOS after Windows had already paid for both.
/// </summary>
internal static class DesignSystem
{
    private static readonly Dictionary<DesignTokens.TypeWeight, FontWeight> Weights = new()
    {
        [DesignTokens.TypeWeight.Regular] = FontWeight.Normal,
        [DesignTokens.TypeWeight.SemiBold] = FontWeight.SemiBold,
        [DesignTokens.TypeWeight.Bold] = FontWeight.Bold,
    };

    /// <summary>A TextBlock wearing a type ROLE — size, weight and default ink together,
    /// because "secondary" rendered in the primary ink is not secondary.</summary>
    public static TextBlock Text(DesignTokens.TypeRole role, string text = "")
    {
        var spec = DesignTokens.Spec(role);
        return new TextBlock
        {
            Text = text,
            FontSize = spec.Size,
            FontWeight = Weights[spec.Weight],
            Foreground = AppTheme.BrushFor(spec.ColorKey),
        };
    }

    /// <summary>One icon from <see cref="IconPaths"/>, as a vector — never a glyph.
    /// Emoji render at a size and weight the app does not control, and PRs #148 and #166
    /// exist because they failed to render at all in Wine prefixes.</summary>
    public static PathIcon Icon(string name, string colorKey = "DimBrush",
        double size = 14, double opacity = 1.0) => new()
    {
        Data = StreamGeometry.Parse(IconPaths.Path(name)),
        Foreground = AppTheme.BrushFor(colorKey),
        Width = size,
        Height = size,
        Opacity = opacity,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>An icon that behaves like a button but reads like a glyph. A real Button,
    /// so it is keyboard-reachable and has a hit area — the controls this replaces were
    /// click-handled TextBlocks, one set per card, on every card in the list.</summary>
    public static Button IconButton(string name, string tip, Action onClick,
        string colorKey = "DimBrush", double opacity = 1.0)
    {
        var button = new Button
        {
            Content = Icon(name, colorKey, opacity: opacity),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Width = DesignTokens.IconButtonSize,
            Height = DesignTokens.IconButtonSize,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>An icon and a word on one baseline — the shape every textual button in
    /// the migrated surfaces takes.</summary>
    public static StackPanel IconLabel(string icon, string label, string colorKey = "DimBrush")
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(Icon(icon, colorKey, size: 12));
        var text = Text(DesignTokens.TypeRole.Caption, label);
        text.Margin = new Thickness(DesignTokens.SpaceXs, 0, 0, 0);
        panel.Children.Add(text);
        return panel;
    }
}
