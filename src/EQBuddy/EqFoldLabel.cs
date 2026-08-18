using System.Windows;
using System.Windows.Controls;
using EQBuddy.UI.Shared;

namespace EQBuddy;

/// <summary>
/// A fold's heading: a chevron that points down when it is open and right when it is
/// shut, followed by the label (Gate 5c).
///
/// The widget had seven of these and every one of them typed "▾" or "▸" into a string —
/// four in XAML as the initial content, and the rest re-typed in code on every refresh.
/// So converting the XAML alone would have been worse than leaving it: the first repaint
/// would put the glyph straight back.
///
/// The chevron is the AFFORDANCE, not decoration — it is the only thing on the row that
/// says whether clicking will open or close it — so it survives the conversion as a
/// chevron rather than being dropped with the glyph.
///
/// Usable as a <see cref="ContentControl.Content"/> (the fold buttons) or as a control in
/// its own right (the section labels), which is why it is a panel rather than a style.
/// </summary>
internal sealed class EqFoldLabel : StackPanel
{
    private readonly EqIcon _chevron;
    private readonly TextBlock _label;

    public EqFoldLabel()
    {
        Orientation = Orientation.Horizontal;
        // Safe as a StackPanel: a fold heading is a few words that never wrap, so the
        // infinite-width measure that clips wrapping text (trap 14) cannot bite. Put
        // WRAPPING text beside an icon and it must be a two-column Grid instead.
        _chevron = new EqIcon
        {
            Glyph = "ChevronDown",
            Size = DesignTokens.IconInline,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, DesignTokens.SpaceXs, 0),
        };
        Children.Add(_chevron);
        _label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        Children.Add(_label);
    }

    /// <summary>Open folds point down, shut folds point right.</summary>
    public bool Open
    {
        get;
        set { field = value; _chevron.Glyph = value ? "ChevronDown" : "ChevronRight"; }
    } = true;

    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    /// <summary>A <see cref="ThemePalettes"/> key applied to BOTH halves, so the chevron
    /// cannot end up a different colour from the words beside it.</summary>
    public string Ink
    {
        get;
        set
        {
            field = value;
            _chevron.Ink = value;
            _label.SetResourceReference(TextBlock.ForegroundProperty, value);
        }
    } = "TextBrush";

    /// <summary>Wear the folded-section heading look rather than body text.
    ///
    /// The Pet-abilities and All-AA headings were `Style="{StaticResource SectionLabel}"`
    /// TextBlocks; converting them to this control without carrying that over rendered
    /// them as plain body text — bigger, brighter, no longer reading as a heading.
    /// Invisible in a diff, obvious in a screenshot, and it took two goes because a
    /// direct resource lookup in a property setter runs before the control is in a tree.
    ///
    /// So it is expressed in TOKENS instead of by borrowing Theme.xaml's style: that is
    /// the migration doing its job. <c>SectionLabel</c> is 10.5px, which is not on the
    /// scale — <see cref="DesignTokens.TypeRole.Metadata"/> is, and this heading is
    /// metadata about the list under it.</summary>
    public bool Section
    {
        get;
        set
        {
            field = value;
            if (!value) return;
            var spec = DesignTokens.Spec(DesignTokens.TypeRole.Metadata);
            _label.FontSize = spec.Size;
            _label.FontWeight = FontWeights.SemiBold;
            _label.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
            // The chevron follows the words, or a dim heading grows a bright arrow.
            _chevron.Ink = "DimBrush";
            _chevron.Size = DesignTokens.IconInline;
        }
    }

    /// <summary>Set both at once — the shape every call site actually wants, and the one
    /// that makes "open" and the words impossible to disagree.</summary>
    public void Set(bool open, string text)
    {
        Open = open;
        Text = text;
    }
}
