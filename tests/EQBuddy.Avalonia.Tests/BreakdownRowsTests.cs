using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using EQBuddy.Core;

namespace EQBuddy.Avalonia.Tests;

/// <summary>The ported BreakdownRows: the 2026-08-11 underline layout and the 1.63.0
/// columns (miss %, hit ranges, resist share, spoken overflow) — asserted here because
/// the Avalonia side previously had a simplified inline row builder and none of this.</summary>
[Collection("avalonia")]
public sealed class BreakdownRowsTests
{
    private static List<string?> Texts(Panel panel) =>
        panel.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text)
            .Concat(panel.Children.OfType<TextBlock>().Select(t => t.Text)).ToList();

    [AvaloniaFact]
    public void RowSplitsTheHeadlineFromItsDimContext()
    {
        var row = BreakdownRows.Row("Slash", "1,234 · ×10 · avg 123.4", 0.5,
            BreakdownRows.BarBrush(), "tip");
        var texts = row.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Slash", texts);
        Assert.Contains("1,234", texts);            // headline: the first " · " segment
        Assert.Contains("×10 · avg 123.4", texts);  // the rest reads dim beside it
    }

    [AvaloniaFact]
    public void MeleeRowsOwnTheirMissesAndSpellsTheirResists()
    {
        var panel = new StackPanel();
        var stats = new List<SourceDamage>
        {
            new("Slash", Hits: 6, Total: 600) { Misses = 2, MinHit = 50, MaxHit = 150 },
            new("Shock of Frost", Hits: 3, Total: 300),
        };
        var resists = new Dictionary<string, (int Casts, int Resists)>(StringComparer.OrdinalIgnoreCase)
        { ["Shock of Frost"] = (4, 1) };

        BreakdownRows.FillAbilityRowsSorted(panel, stats, StatSort.Total, 60, "dps",
            resists: resists);

        var texts = Texts(panel);
        // 2 misses out of 8 attempts — the % a player means, out of attempts.
        Assert.Contains(texts, t => t?.Contains("25% miss") == true);
        Assert.Contains(texts, t => t?.Contains("25% resist") == true);
        // Miss % never stamps the spell row, resist % never the melee row.
        Assert.DoesNotContain(texts, t => t?.Contains("miss") == true && t.Contains("resist"));
    }

    [AvaloniaFact]
    public void OverflowIsSaidOutLoudNeverSilentlyTruncated()
    {
        var panel = new StackPanel();
        var stats = Enumerable.Range(1, 12)
            .Select(i => new SourceDamage($"Skill {i}", Hits: i, Total: i * 100)).ToList();

        BreakdownRows.FillAbilityRowsSorted(panel, stats, StatSort.Total, 60, "dps", max: 10);

        Assert.Contains(Texts(panel), t => t?.StartsWith("…2 more") == true);
        Assert.Equal(11, panel.Children.Count);   // 10 rows + the overflow line
    }
}
