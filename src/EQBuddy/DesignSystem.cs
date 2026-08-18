using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    /// <summary>An icon that behaves like a button but reads like a glyph — the close /
    /// pin / report family. A real <see cref="Button"/>, so it is keyboard-reachable and
    /// has a hit area, rather than the click-handled TextBlocks these used to be.</summary>
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

    /// <summary>A clickable icon that sits INSIDE a line of text — the loot row's quest
    /// badge and its family. <see cref="IconButton"/> is the same idea at
    /// <see cref="DesignTokens.IconButtonSize"/>, which on the widget would make every
    /// loot row a third taller; this keeps the drawn size and widens only the target.
    ///
    /// The point is the transparent ground the button template paints: a bare
    /// <see cref="Path"/> only receives a click where it is painted, so the map-pin badge
    /// had holes you could click straight through (#211). It is a real Button rather than
    /// a handled Path so it is keyboard-reachable and shows the same hover as the rest of
    /// the icon family.</summary>
    public static Button InlineIconButton(string name, string tip, RoutedEventHandler onClick,
        string colorKey = "DimBrush", double size = DesignTokens.IconInline)
    {
        var button = IconButton(name, tip, onClick, colorKey);
        button.Width = button.Height = DesignTokens.IconInlineHit;
        if (button.Content is Path icon) icon.Width = icon.Height = size;
        return button;
    }
}

/// <summary>
/// THE selectable pill (gate 2b, docs/DesignSystem.md §11.2). Tabs, the class lens, the
/// quest mode strip, the loot view filter and the loot sort toggle are one shape doing one
/// job, and every one of them was hand-built — 16 across MainWindow.xaml and
/// BreakoutWindow.xaml, the most recent pair arriving in #198 six hours after Gate 2
/// deleted the pattern from the Quest Tracker.
///
/// Geometry and the selected-state vocabulary come from <see cref="ChipStyle"/>, so the
/// Avalonia chip is the same chip rather than a copy that drifts.
/// </summary>
internal sealed class EqChip : Border
{
    private readonly TextBlock _label;
    private readonly TextBlock? _badge;

    /// <summary>What this chip selects — a mode string, a QuestTab, a class name.
    /// Compared by the strip, never interpreted here.</summary>
    public object Key { get; }

    public EqChip(string text, object key, string? badge = null, string? tip = null,
        Action? onClick = null)
    {
        Key = key;
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        _label = DesignSystem.Text(ChipStyle.LabelRole, text);
        content.Children.Add(_label);
        if (badge is { Length: > 0 })
        {
            _badge = DesignSystem.Text(ChipStyle.BadgeRole, badge);
            _badge.Margin = new Thickness(DesignTokens.SpaceS, 1, 0, 0);
            content.Children.Add(_badge);
        }
        Child = content;
        CornerRadius = new CornerRadius(ChipStyle.Radius);
        Padding = new Thickness(ChipStyle.Padding.Left, ChipStyle.Padding.Top,
            ChipStyle.Padding.Right, ChipStyle.Padding.Bottom);
        Margin = new Thickness(0, 0, ChipStyle.Gap.Right, ChipStyle.Gap.Bottom);
        BorderThickness = new Thickness(ChipStyle.BorderThickness);
        Cursor = Cursors.Hand;
        if (tip is not null) ToolTip = tip;
        // Handled, or the window's own drag-to-move swallows the click.
        if (onClick is not null)
            MouseLeftButtonDown += (_, e) => { e.Handled = true; onClick(); };
        SetSelected(false);
    }

    public void SetSelected(bool on)
    {
        var ink = ChipStyle.For(on);
        SetResourceReference(BackgroundProperty, ink.Background);
        SetResourceReference(BorderBrushProperty, ink.Border);
        _label.Ink(ink.Label);
        if (_badge is null) return;
        _badge.Ink(ink.Badge);
        _badge.Opacity = ink.BadgeOpacity;
    }
}

/// <summary>A row of <see cref="EqChip"/> where exactly one is selected — the segmented
/// control. Owns the "which one is on" bookkeeping every hand-built strip wrote its own
/// copy of, usually as a foreach over a list of tuples.</summary>
internal sealed class EqSegmentedStrip(Panel host)
{
    private readonly List<EqChip> _chips = [];

    public int Count => _chips.Count;

    public void Clear()
    {
        host.Children.Clear();
        _chips.Clear();
    }

    public EqChip Add(string text, object key, string? badge = null, string? tip = null,
        Action? onClick = null)
    {
        var chip = new EqChip(text, key, badge, tip, onClick);
        host.Children.Add(chip);
        _chips.Add(chip);
        return chip;
    }

    /// <summary>One chip by its key, or null. A strip sometimes has to hide a segment
    /// rather than disable it — the Loot card withholds "recent" when nothing on screen
    /// carries a timestamp — and reaching for it by key beats every caller keeping its
    /// own field.</summary>
    public EqChip? Chip(object key) => _chips.FirstOrDefault(c => Equals(c.Key, key));

    /// <summary>Paints the selection. Compared with <see cref="object.Equals(object?,
    /// object?)"/> so strips keyed on strings, enums or null all work without the caller
    /// casting.</summary>
    public void Select(object? key)
    {
        foreach (var chip in _chips) chip.SetSelected(Equals(chip.Key, key));
    }
}
