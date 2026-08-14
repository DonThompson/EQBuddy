using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Tests;

public class WatchAlertTextTests
{
    [Fact]
    public void SpellFadeWithTargetSaysWhatItCameOff()
    {
        var rule = new TrackedRule { Kind = WatchKind.SpellFade };
        var result = Result("Spirit of the Puma (Drucilog)");

        Assert.Equal("Spirit of the Puma faded off Drucilog",
            WatchAlertText.MatchLabel(rule, result, 1));
    }

    [Fact]
    public void SelfBuffFadeSaysItCameOffYou()
    {
        var rule = new TrackedRule { Kind = WatchKind.SpellFade };
        var result = Result("Spirit of the Puma");

        Assert.Equal("Spirit of the Puma faded off you",
            WatchAlertText.MatchLabel(rule, result, 1));
    }

    [Fact]
    public void CountsUseTheAppsMultiplicationSignButSpeakAsWords()
    {
        var rule = new TrackedRule { Kind = WatchKind.Loot };
        var label = WatchAlertText.MatchLabel(rule, Result("Rusty Sword"), 3);

        Assert.Equal("Rusty Sword ×3", label);                       // matches every other count in the UI
        Assert.Equal("Rusty Sword 3 times", SpokenAlerts.Speakable(label));
    }

    // ---- multi-item bursts (#137, bjstrange) ----

    /// <summary>#137 (bjstrange): "Wind Rune" catching Heda AND Meda off one corpse
    /// said "Wind Rune Meda ×2" — the last name twice, the other drop never named.</summary>
    [Fact]
    public void TwoNewItemsAreEachNamed()
    {
        var rule = new TrackedRule { Kind = WatchKind.Loot };
        var result = Result("Wind Rune Meda",
            new NameCount("Wind Rune Heda", 1), new NameCount("Wind Rune Meda", 1));

        Assert.Equal("Wind Rune Heda, Wind Rune Meda",
            WatchAlertText.MatchLabel(rule, result, 2, new Dictionary<string, int>()));
    }

    [Fact]
    public void ASingleItemBurstKeepsItsOwnCount()
    {
        var rule = new TrackedRule { Kind = WatchKind.Loot };
        var result = Result("Bone Chips", new NameCount("Bone Chips", 5));
        var previous = new Dictionary<string, int> { ["Bone Chips"] = 3 };

        Assert.Equal("Bone Chips ×2", WatchAlertText.MatchLabel(rule, result, 2, previous));
    }

    [Fact]
    public void FadePhrasingAppliesToEachItem()
    {
        var rule = new TrackedRule { Kind = WatchKind.SpellFade };
        var result = Result("Haste",
            new NameCount("Spirit of the Puma (Drucilog)", 1), new NameCount("Haste", 1));

        Assert.Equal("Spirit of the Puma faded off Drucilog, Haste faded off you",
            WatchAlertText.MatchLabel(rule, result, 2, new Dictionary<string, int>()));
    }

    /// <summary>No previous counts (fresh baseline, rule added mid-session): the
    /// old last-item label is the only honest thing to say.</summary>
    [Fact]
    public void MissingPreviousCountsFallBackToTheLastItem()
    {
        var rule = new TrackedRule { Kind = WatchKind.Loot };
        var result = Result("Wind Rune Meda",
            new NameCount("Wind Rune Heda", 1), new NameCount("Wind Rune Meda", 1));

        Assert.Equal("Wind Rune Meda ×2", WatchAlertText.MatchLabel(rule, result, 2, null));
    }

    /// <summary>The label feeds a banner and the voice — past three names the rest
    /// collapse to "+N more". Items arrive largest-first, so the biggest are kept.</summary>
    [Fact]
    public void AFourItemBurstCapsAtThreeNamesPlusMore()
    {
        var rule = new TrackedRule { Kind = WatchKind.Loot };
        var result = Result("Words of Crippling Force",
            new NameCount("Rune of Ap`Sagor", 3), new NameCount("Rune of Rathe", 2),
            new NameCount("Words of Acquisition", 1), new NameCount("Words of Crippling Force", 1));

        Assert.Equal("Rune of Ap`Sagor ×3, Rune of Rathe ×2, Words of Acquisition +1 more",
            WatchAlertText.MatchLabel(rule, result, 7, new Dictionary<string, int>()));
    }

    /// <summary>A total that moved while no item grew (history trim skew) must not
    /// alert about nothing — the last-item fallback keeps the label meaningful.</summary>
    [Fact]
    public void ATotalMoveWithoutItemGrowthFallsBack()
    {
        var rule = new TrackedRule { Kind = WatchKind.Loot };
        var result = Result("Bone Chips", new NameCount("Bone Chips", 3));
        var previous = new Dictionary<string, int> { ["Bone Chips"] = 3 };

        Assert.Equal("Bone Chips", WatchAlertText.MatchLabel(rule, result, 1, previous));
    }

    private static TrackedRuleResult Result(string lastItem) =>
        new("Buff dropped", 1, [], 0, 0, null, DateTime.Now, lastItem);

    private static TrackedRuleResult Result(string lastItem, params NameCount[] items) =>
        new("Watch", items.Sum(i => i.Count), [.. items], 0, 0, null, DateTime.Now, lastItem);
}
