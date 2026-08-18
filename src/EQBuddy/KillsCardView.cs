using System.Windows;
using System.Windows.Controls;
using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy;

/// <summary>
/// The Kills card (Gate 5b) — and the first card built as an <see cref="IWidgetCard"/>
/// against an <see cref="ICardContext"/> rather than against <c>MainWindow</c>.
///
/// It was chosen to go first precisely because it asks the widget for NOTHING: no item
/// popups, no wiki lookups, no repaint requests. That makes it the clean proof of the card
/// seam — if this cannot be built and tested without a window, nothing can, and we learn
/// it for the price of one card instead of fourteen. The cards that do need services
/// (Motes, Money) exercise the context interface next.
///
/// Everything it SHOWS lives in <see cref="KillsPresentation"/>, so the strings are tested
/// without a window too. What is left here is only the drawing.
/// </summary>
internal sealed class KillsCardView : IWidgetCard
{
    private readonly TextBlock _summary;
    private readonly ItemsControl _kills = new();
    private readonly TextBlock _farmingLabel;
    private readonly ItemsControl _farming = new();
    private readonly TextBlock _partyLabel;
    private readonly ItemsControl _party = new();

    public string Key => "kills";

    public UIElement Body { get; }

    /// <summary>Rendered row counts, for the <c>EQBUDDY_EXPAND</c> dump the E2E suite
    /// asserts on.</summary>
    public int KillRowCount => _kills.Items.Count;
    public int PartyRowCount => _party.Items.Count;

    public KillsCardView()
    {
        _summary = DesignSystem.Text(DesignTokens.TypeRole.BodySecondary);
        _summary.Margin = new Thickness(0, DesignTokens.SpaceXxs, 0, DesignTokens.SpaceXs);
        _farmingLabel = SectionLabel(KillsPresentation.FarmingLabel);
        _partyLabel = SectionLabel(KillsPresentation.PartyKillsLabel);

        var body = new StackPanel();
        body.Children.Add(_summary);
        body.Children.Add(_kills);
        body.Children.Add(_farmingLabel);
        body.Children.Add(_farming);
        body.Children.Add(_partyLabel);
        body.Children.Add(_party);
        Body = body;
    }

    private static TextBlock SectionLabel(string text)
    {
        var label = DesignSystem.Text(DesignTokens.TypeRole.Caption, text);
        label.FontWeight = FontWeights.SemiBold;
        label.Margin = new Thickness(0, DesignTokens.SpaceS, 0, DesignTokens.SpaceXxs);
        label.Visibility = Visibility.Collapsed;
        return label;
    }

    public void Render(StatsSnapshot s)
    {
        _summary.Text = KillsPresentation.Summary(s);
        Fill(_kills, KillsPresentation.YourKills(s));

        _farmingLabel.Visibility = KillsPresentation.ShowFarming(s)
            ? Visibility.Visible : Visibility.Collapsed;
        Fill(_farming, KillsPresentation.Farming(s));

        _partyLabel.Visibility = KillsPresentation.ShowPartyKills(s)
            ? Visibility.Visible : Visibility.Collapsed;
        Fill(_party, KillsPresentation.PartyKills(s));
    }

    /// <summary>A name and a value on one row, the value hard right.</summary>
    private static void Fill(ItemsControl list, IReadOnlyList<KillRow> rows)
    {
        list.Items.Clear();
        foreach (var row in rows)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = DesignSystem.Text(DesignTokens.TypeRole.Body, row.Name);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.ToolTip = row.Name;
            // A drop hangs under the creature that dropped it. Real indentation, from the
            // scale — it was six literal spaces in the name, which a proportional font
            // renders differently at every zoom level and no test could see.
            name.Margin = new Thickness(row.Indent ? DesignTokens.Indent : 0, 1,
                DesignTokens.SpaceM, 1);
            grid.Children.Add(name);

            var value = DesignSystem.Text(DesignTokens.TypeRole.Body, row.Value).Ink("DimBrush");
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
            list.Items.Add(grid);
        }
    }
}
