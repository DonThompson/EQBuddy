using System.Windows;
using System.Windows.Controls;
using EQBuddy.UI.Shared;

namespace EQBuddy;

/// <summary>
/// A card's heading: its icon and its name, on one baseline (Gate 5).
///
/// The widget's fourteen cards each wrote this by hand as a single TextBlock whose text
/// began with an emoji — <c>Text="&#x1F480; Kills" FontSize="13"</c>. That is two design
/// decisions (a glyph and a size) typed fourteen times, and it is the pattern this whole
/// effort exists to remove; the difference here is that it sits on the surface the player
/// looks at all session, and that emoji are what failed to render under Wine in #148 and
/// #166 — on the Linux and macOS builds that are EQBuddy's only uncontested ground.
///
/// A control rather than a Style because a heading has two variables (which icon, which
/// word) and a WPF Style cannot take arguments. XAML says
/// <c>&lt;local:EqCardTitle Icon="Skull" Text="Kills"/&gt;</c> and gets the type role, the
/// palette and the vector for free — and cannot get them wrong.
///
/// The icon NAME is a shape ("Skull"), not a card ("Kills"): see
/// <see cref="OverlaySections.Icon"/>, which is where the two are mapped for both UIs.
/// </summary>
internal sealed class EqCardTitle : StackPanel
{
    private readonly System.Windows.Shapes.Path _icon;
    private readonly TextBlock _label;

    public EqCardTitle()
    {
        Orientation = Orientation.Horizontal;
        // Safe as a StackPanel: a card's name is one or two words that never wrap, so the
        // infinite-width measure that clips wrapping text (trap 14) cannot bite here. Put
        // wrapping text beside an icon and it must be a two-column Grid instead.
        _icon = DesignSystem.Icon("Info", "TextBrush", size: DesignTokens.IconInline);
        _icon.Margin = new Thickness(0, 0, DesignTokens.SpaceS, 0);
        Children.Add(_icon);
        _label = DesignSystem.Text(DesignTokens.TypeRole.TitleSection);
        Children.Add(_label);
    }

    /// <summary>An <see cref="IconPaths"/> name. Unknown names fall back rather than
    /// throwing: a mistyped card heading should look plain, not take the widget down on
    /// startup.</summary>
    public string Icon
    {
        get;
        set
        {
            field = value;
            if (IconPaths.Names.Contains(value))
                _icon.Data = System.Windows.Media.Geometry.Parse(IconPaths.Path(value));
        }
    } = "Info";

    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }
}
