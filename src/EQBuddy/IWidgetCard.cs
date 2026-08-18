using System.Windows;
using EQBuddy.Core;

namespace EQBuddy;

/// <summary>
/// A card on the widget (Gate 5b).
///
/// The lifts before this one — <c>QuestChecklistView</c>, <c>LootCardView</c> — each
/// invented their own shape, and each took <c>MainWindow</c> as a constructor argument.
/// That made the files shorter and the coupling WIDER: MainWindow carries 61 internal
/// members, most of them there so a lifted view can reach back. Moving lines without
/// moving dependencies just relocates the problem, and doing it another thirteen times
/// would end with a small host class and an enormous service surface — which the line
/// ratchet would be perfectly happy about.
///
/// So a card is an abstraction now, not a convention: a key, a body, and "paint yourself
/// from this snapshot". The host orders them, hides them and renders the expanded ones,
/// and adding a fifteenth card touches the host nowhere.
/// </summary>
internal interface IWidgetCard
{
    /// <summary>The section key — the same one <see cref="UI.Shared.OverlaySections"/>,
    /// <c>SectionOrder</c> and <c>HiddenSections</c> use, so a card needs no separate
    /// identity and cannot disagree with the settings about who it is.</summary>
    string Key { get; }

    /// <summary>What hangs inside the card's expander.</summary>
    UIElement Body { get; }

    /// <summary>Paint from this tick. Called only while the card is expanded — a collapsed
    /// card costs nothing, which is the rule the widget has always had.</summary>
    void Render(StatsSnapshot snapshot);
}

/// <summary>
/// What a card is allowed to ask the widget for.
///
/// **This is the half that matters.** A card that takes <c>MainWindow</c> depends on a
/// 4,500-line class and can only be exercised by launching one — which is why
/// docs/TestPlan.md §5 records that the entire WPF layer has no unit tests. A card that
/// takes THIS depends on six methods and can be tested against a fake in a few lines.
///
/// Deliberately small, and it should stay that way. Every method here is one a card
/// genuinely cannot answer for itself: opening a shared window, reaching the wiki cache,
/// or asking for a repaint. Anything a card can compute from its snapshot belongs in
/// UI.Shared with the rest of the presentation logic, where it is testable without even
/// this. **If this interface starts growing, that is the signal that something is being
/// pushed into cards that should have gone to UI.Shared instead.**
/// </summary>
internal interface ICardContext
{
    /// <summary>Open the shared item-info popup on this item.</summary>
    void ShowItemInfo(string itemName);

    /// <summary>Does any tracked quest want this item? Drives the quest badge.</summary>
    bool IsActiveQuestItem(string itemName);

    /// <summary>Hover text for an item, quest marker included.</summary>
    string? QuestAwareTooltip(string itemName, string? baseTip);

    /// <summary>Cached wiki stats for an item, or the hint that a click fetches them.</summary>
    string ItemHoverStats(string itemName);

    /// <summary>Open the Quest Tracker filtered to this item's quests.</summary>
    void OpenQuestInfoForItem(string itemName);

    /// <summary>The tick most recently painted, for a card that needs to repaint itself
    /// after the player changes one of its own filters.</summary>
    StatsSnapshot CurrentSnapshot();
}
