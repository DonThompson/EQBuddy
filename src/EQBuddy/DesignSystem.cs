using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using EQBuddy.UI.Shared;

namespace EQBuddy;

/// <summary>
/// The WPF composition of <see cref="DesignTokens"/> and <see cref="IconPaths"/> — the
/// same job <see cref="ThemeManager"/> does for colour, for the other four axes.
///
/// Nothing here decides anything. Every number and every path comes out of UI.Shared, so
/// the Avalonia lane composes the SAME values into its own styles and the two cannot
/// drift. That is not a theoretical worry: the Avalonia chip stacks once shipped a
/// hand-copied older version of the WPF anchor and carried #122 and #152 to Linux and
/// macOS after Windows had already paid for both.
///
/// Token resources are static — a theme switch repaints, it doesn't re-scale — so this
/// dictionary is built once and merged at startup rather than swapped like the palette.
/// </summary>
internal static class DesignSystem
{
    /// <summary>Builds the token ResourceDictionary: every numeric token as a
    /// <c>double</c> under its own name, plus the <c>CornerRadius</c> and
    /// <c>Thickness</c> shapes XAML can't compute from a double.</summary>
    public static ResourceDictionary Tokens()
    {
        var d = new ResourceDictionary();
        foreach (var (key, value) in DesignTokens.Numbers) d[key] = value;

        // Radii as CornerRadius, so a style says {StaticResource CornerCard} rather than
        // re-typing the number. Named Corner* rather than Radius* so the double and the
        // shape can coexist under obvious names.
        d["CornerPanel"] = new CornerRadius(DesignTokens.RadiusPanel);
        d["CornerCard"] = new CornerRadius(DesignTokens.RadiusCard);
        d["CornerControl"] = new CornerRadius(DesignTokens.RadiusControl);
        d["CornerPill"] = new CornerRadius(DesignTokens.RadiusPill);

        // The spacing scale as Thickness, uniform and per-edge in the combinations the
        // migrated surfaces actually use. Anything not here is a new decision and should
        // be added by name rather than typed inline (that is how 174 distinct Thickness
        // tuples happened).
        d["PadCard"] = new Thickness(DesignTokens.SpaceL, DesignTokens.SpaceM,
            DesignTokens.SpaceL, DesignTokens.SpaceM);
        d["PadRow"] = new Thickness(DesignTokens.SpaceM, DesignTokens.SpaceS,
            DesignTokens.SpaceM, DesignTokens.SpaceS);
        d["PadControl"] = new Thickness(DesignTokens.SpaceM, DesignTokens.SpaceXs,
            DesignTokens.SpaceM, DesignTokens.SpaceXs);
        d["PadPill"] = new Thickness(DesignTokens.SpaceL, DesignTokens.SpaceXxs,
            DesignTokens.SpaceL, DesignTokens.SpaceXxs);
        d["PadWindow"] = new Thickness(DesignTokens.SpaceXl);
        d["GapXs"] = new Thickness(0, 0, DesignTokens.SpaceXs, 0);
        d["GapS"] = new Thickness(0, 0, DesignTokens.SpaceS, 0);
        d["StackXs"] = new Thickness(0, 0, 0, DesignTokens.SpaceXs);
        d["StackS"] = new Thickness(0, 0, 0, DesignTokens.SpaceS);
        d["StackM"] = new Thickness(0, 0, 0, DesignTokens.SpaceM);
        d["StackL"] = new Thickness(0, 0, 0, DesignTokens.SpaceL);
        return d;
    }

    // ---- typography ----

    private static readonly Dictionary<DesignTokens.TypeWeight, FontWeight> Weights = new()
    {
        [DesignTokens.TypeWeight.Regular] = FontWeights.Normal,
        [DesignTokens.TypeWeight.SemiBold] = FontWeights.SemiBold,
        [DesignTokens.TypeWeight.Bold] = FontWeights.Bold,
    };

    /// <summary>A TextBlock wearing a type ROLE — size, weight and default ink together,
    /// because "secondary" rendered in the primary ink is not secondary. Callers override
    /// the ink for STATE (a ready row goes GoodBrush) via <see cref="Ink"/>; overriding it
    /// for emphasis is how 612 independent size decisions happened in the first place.</summary>
    public static TextBlock Text(DesignTokens.TypeRole role, string text = "")
    {
        var spec = DesignTokens.Spec(role);
        var block = new TextBlock
        {
            Text = text,
            FontSize = spec.Size,
            FontWeight = Weights[spec.Weight],
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, spec.ColorKey);
        return block;
    }

    /// <summary>Repoints an element's foreground at a palette key, keeping it live across
    /// a theme switch. <c>SetResourceReference</c> rather than a fetched brush: a fetched
    /// brush is a snapshot, and the window would keep the old theme's colour.</summary>
    public static T Ink<T>(this T element, string colorKey) where T : DependencyObject
    {
        switch (element)
        {
            case TextBlock t: t.SetResourceReference(TextBlock.ForegroundProperty, colorKey); break;
            case Control c: c.SetResourceReference(Control.ForegroundProperty, colorKey); break;
            case Shape s: s.SetResourceReference(Shape.FillProperty, colorKey); break;
        }
        return element;
    }

    // ---- icons ----

    /// <summary>One icon from <see cref="IconPaths"/>, as a vector — never a glyph.
    ///
    /// Emoji and dingbats render at a size and weight the app does not control, and PRs
    /// #148 and #166 exist because they failed to render at all in Wine prefixes, on the
    /// platforms that are EQBuddy's only uncontested ground. A Path takes the palette as
    /// its fill and the size we ask for, on every platform.</summary>
    public static Path Icon(string name, string colorKey = "DimBrush",
        double size = 14, double opacity = 1.0)
    {
        var icon = new Path
        {
            Data = Geometry.Parse(IconPaths.Path(name)),
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            Opacity = opacity,
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(Shape.FillProperty, colorKey);
        return icon;
    }

    /// <summary>An icon that behaves like a button but reads like a glyph — the ✕ / 📌 /
    /// ⚑ family. A real <see cref="Button"/>, so it is keyboard-reachable and has a hit
    /// area, rather than the click-handled TextBlocks these used to be.</summary>
    public static Button IconButton(string name, string tip, RoutedEventHandler onClick,
        string colorKey = "DimBrush", double opacity = 1.0)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("EqIconButton"),
            Content = Icon(name, colorKey, opacity: opacity),
            ToolTip = tip,
        };
        button.Click += onClick;
        return button;
    }

}
