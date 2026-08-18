using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using EQBuddy.UI.Shared;

namespace EQBuddy;

/// <summary>
/// Draws <see cref="CardRow"/>s (Gate 5b) — the one place the widget's name-and-value row
/// is built.
///
/// It replaces `MainWindow.FillList` and the per-surface copies that grew beside it, and
/// it exists before the remaining cards are converted rather than after, because twelve
/// cards each inventing a row is precisely how the app arrived at 174 distinct spacing
/// tuples in the first place.
///
/// Item behaviour — the wiki popup, the cached-stats hover, the quest badge — comes in
/// through <see cref="ICardContext"/> and applies only to rows flagged
/// <see cref="CardRow.Item"/>. A card that shows no items passes no context and needs no
/// window to test.
/// </summary>
internal static class EqCardRows
{
    public static void Fill(ItemsControl list, IEnumerable<CardRow> rows,
        ICardContext? context = null)
    {
        list.Items.Clear();
        foreach (var row in rows) list.Items.Add(Build(row, context));
    }

    public static Grid Build(CardRow row, ICardContext? context = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = DesignSystem.Text(DesignTokens.TypeRole.Body);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Margin = new Thickness(row.Indent ? DesignTokens.Indent : 0, 1,
            DesignTokens.SpaceM, 1);
        if (row.Note is { Length: > 0 } note)
        {
            // A separate run, so the name a click looks up is unchanged.
            name.Inlines.Add(new Run(row.Name));
            var tag = new Run($" ({note})") { FontSize = MetadataSize };
            tag.SetResourceReference(TextElement.ForegroundProperty, "DimBrush");
            name.Inlines.Add(tag);
        }
        else name.Text = row.Name;
        // A trimmed name says its full self on hover (#182). An item overwrites this with
        // its stat block below, which is strictly more information.
        name.ToolTip = row.Name;

        if (row.Item && context is not null) MakeItem(name, row.Name, context);
        grid.Children.Add(name);

        if (row.Item && context is { } ctx && ctx.IsActiveQuestItem(row.Name)
            && QuestBadge(ctx, row.Name) is { } badge)
        {
            Grid.SetColumn(badge, 1);
            grid.Children.Add(badge);
        }

        var value = DesignSystem.Text(DesignTokens.TypeRole.Body, row.Value)
            .Ink(row.ValueInk ?? "DimBrush");
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        return grid;
    }

    /// <summary>Click for the wiki popup, hover for the cached stats.</summary>
    private static void MakeItem(TextBlock name, string item, ICardContext context)
    {
        if (context.QuestAwareTooltip(item, context.ItemHoverStats(item)) is { Length: > 0 } tip)
        {
            var text = new TextBlock { Text = tip, TextWrapping = TextWrapping.Wrap, MaxWidth = TipWidth };
            // Multi-line tips are stat blocks — monospace keeps their columns readable.
            if (tip.Contains('\n')) text.FontFamily = MainWindow.MonoFamily;
            name.ToolTip = new ToolTip { Content = text };
        }
        name.Cursor = Cursors.Hand;
        // Swallow the down so it can't start a window DragMove and eat the Up — the
        // discussion #46 failure mode.
        name.MouseLeftButtonDown += (_, e) => e.Handled = true;
        name.MouseLeftButtonUp += (_, _) => context.ShowItemInfo(item);
    }

    /// <summary>The quest marker beside an item: click for the Quest Tracker filtered to
    /// this item's quests, while the item's own name still opens its wiki page (David's
    /// shape, 2026-08-07). A vector — the emoji it replaces is what failed to render under
    /// Wine in #148 and #166.</summary>
    private static UIElement QuestBadge(ICardContext context, string item)
    {
        var badge = DesignSystem.Icon("Map", "GoodBrush", size: DesignTokens.IconInline);
        badge.Margin = new Thickness(0, 1, DesignTokens.SpaceS, 1);
        badge.Cursor = Cursors.Hand;
        badge.ToolTip = "Part of a quest — click for its quest info";
        badge.MouseLeftButtonDown += (_, e) => e.Handled = true;
        badge.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            context.OpenQuestInfoForItem(item);
        };
        return badge;
    }

    private static readonly double MetadataSize =
        DesignTokens.Spec(DesignTokens.TypeRole.Metadata).Size;

    /// <summary>The width at which a monospace stat block stops being a column and starts
    /// being a paragraph.</summary>
    private const double TipWidth = 340;
}
