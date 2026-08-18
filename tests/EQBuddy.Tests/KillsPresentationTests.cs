using EQBuddy.Core;
using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// What the Kills card says (Gate 5b).
///
/// These strings were computed inline in <c>RefreshUi</c>, interleaved with the control
/// assignments, and so were untestable: docs/TestPlan.md §5 records that the whole WPF
/// layer has no unit tests, and every card carries a handful of numbers like these. This
/// is the first card whose CONTENT is asserted without launching anything.
/// </summary>
public class KillsPresentationTests
{
    private static StatsSnapshot Snapshot(
        double perHour = 0, double perActiveHour = 0,
        IReadOnlyList<NameCount>? yours = null,
        IReadOnlyList<MobSummary>? mobs = null,
        IReadOnlyList<NameCount>? party = null) =>
        new()
        {
            KillsPerHour = perHour,
            KillsPerActiveHour = perActiveHour,
            YourKills = yours is null ? [] : [.. yours],
            Mobs = mobs is null ? [] : [.. mobs],
            PartyKillsByKiller = party is null ? [] : [.. party],
        };

    // ---- the pace line ----

    /// <summary>Both rates, because they differ by exactly the downtime — which is the
    /// reason the card shows two numbers instead of one.</summary>
    [Fact]
    public void TheSummaryReportsSessionPaceAndActivePace()
    {
        var text = KillsPresentation.Summary(Snapshot(perHour: 42.35, perActiveHour: 61.5));
        Assert.Contains("42.4 kills/hr", text);
        Assert.Contains("61.5 active", text);
    }

    /// <summary>With no recent window there is nothing to say about one — and saying
    /// "last 0m: 0" would read as a stalled session rather than an absent measurement.</summary>
    [Fact]
    public void TheSummaryOmitsTheRecentWindowWhenThereIsntOne() =>
        Assert.DoesNotContain("last", KillsPresentation.Summary(Snapshot()));

    // ---- the farming block ----

    /// <summary>A drop hangs UNDER the creature that dropped it. The indent is a flag, not
    /// six literal spaces in the name — a proportional font renders those differently at
    /// every zoom level, and nothing could assert them.</summary>
    [Fact]
    public void DropsHangUnderTheCreatureThatDroppedThem()
    {
        var rows = KillsPresentation.Farming(Snapshot(mobs:
        [
            new MobSummary("a greater ice bones", Kills: 4, Encounters: 4,
                AvgFightSeconds: 12.4, XpPercent: 0.85, Copper: 1234,
                Loot: [new MobLoot("Bone Chips", 7, null)]),
        ]));

        Assert.Equal(2, rows.Count);
        Assert.Equal("a greater ice bones", rows[0].Name);
        Assert.False(rows[0].Indent);
        Assert.Equal("Bone Chips", rows[1].Name);
        Assert.True(rows[1].Indent);
        Assert.DoesNotContain("  ", rows[1].Name);   // no smuggled indent
    }

    /// <summary>A drop rate is only meaningful per creature, and only when we have one —
    /// a missing rate must print nothing rather than "0%", which is a claim.</summary>
    [Fact]
    public void ADropRateAppearsOnlyWhenItIsKnown()
    {
        var mob = new MobSummary("a puma", Kills: 10, Encounters: 10,
            AvgFightSeconds: 8, XpPercent: 0.4, Copper: 0,
            Loot:
            [
                new MobLoot("Puma Skin", 5, 50),
                new MobLoot("Puma Fang", 1, null),
            ]);
        var rows = KillsPresentation.Farming(Snapshot(mobs: [mob]));

        Assert.Contains("50%", rows[1].Value);
        Assert.DoesNotContain("%", rows[2].Value);
    }

    /// <summary>A creature you have not killed is not something you are farming.</summary>
    [Fact]
    public void CreaturesWithNoKillsAreNotFarming()
    {
        var snapshot = Snapshot(mobs: [new MobSummary("a puma", 0, 0, 0, 0, 0, [])]);
        Assert.Empty(KillsPresentation.Farming(snapshot));
        Assert.False(KillsPresentation.ShowFarming(snapshot));
    }

    // ---- the group ----

    /// <summary>Group kills are COUNTS, never a comparison and never a ranking: measuring
    /// other players is the one line this project does not cross. This test exists so that
    /// stays true if the card is ever rebuilt.</summary>
    [Fact]
    public void GroupKillsAreCountsAndNothingElse()
    {
        var rows = KillsPresentation.PartyKills(Snapshot(party:
            [new NameCount("Someone", 12), new NameCount("Someoneelse", 3)]));

        Assert.Equal(["×12", "×3"], rows.Select(r => r.Value));
        foreach (var row in rows)
        {
            Assert.DoesNotContain("%", row.Value);
            Assert.DoesNotContain("dps", row.Value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/hr", row.Value);
        }
    }

    [Fact]
    public void TheGroupBlockIsHiddenWhenNobodyElseKilledAnything() =>
        Assert.False(KillsPresentation.ShowPartyKills(Snapshot()));

    [Fact]
    public void YourOwnKillsAreCountedPerCreature()
    {
        var rows = KillsPresentation.YourKills(Snapshot(yours: [new NameCount("a puma", 9)]));
        var row = Assert.Single(rows);
        Assert.Equal("a puma", row.Name);
        Assert.Equal("×9", row.Value);
    }
}
