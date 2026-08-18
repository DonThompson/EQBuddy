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
        _summary = CardParts.Summary();
        _farmingLabel = CardParts.BlockLabel(KillsPresentation.FarmingLabel);
        _partyLabel = CardParts.BlockLabel(KillsPresentation.PartyKillsLabel);

        var body = new StackPanel();
        body.Children.Add(_summary);
        body.Children.Add(_kills);
        body.Children.Add(_farmingLabel);
        body.Children.Add(_farming);
        body.Children.Add(_partyLabel);
        body.Children.Add(_party);
        Body = body;
    }

    public void Render(StatsSnapshot s)
    {
        _summary.Text = KillsPresentation.Summary(s);
        EqCardRows.Fill(_kills, KillsPresentation.YourKills(s));

        _farmingLabel.Visibility = KillsPresentation.ShowFarming(s)
            ? Visibility.Visible : Visibility.Collapsed;
        EqCardRows.Fill(_farming, KillsPresentation.Farming(s));

        _partyLabel.Visibility = KillsPresentation.ShowPartyKills(s)
            ? Visibility.Visible : Visibility.Collapsed;
        EqCardRows.Fill(_party, KillsPresentation.PartyKills(s));
    }

}
