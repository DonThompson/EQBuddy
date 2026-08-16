using EQBuddy.Core;

namespace EQBuddy.Tests;

public class MotesTests
{
    private static LootDetail L(string item, int count) => new(item, count, "an orc");

    [Fact]
    public void OnlyThePotentialFamilyCounts()
    {
        Assert.True(Motes.IsMote("Mote of Minor Potential"));
        Assert.True(Motes.IsMote("Mote of Potential"));
        Assert.True(Motes.IsMote("mote of GRAND potential"));   // log case drift
        Assert.False(Motes.IsMote("Crystallized Fire Mote"));
        Assert.False(Motes.IsMote("Faint Mote of Shadow"));
        Assert.False(Motes.IsMote("Remote of Potential"));      // anchored, not substring
        Assert.False(Motes.IsMote("Mote of Utter Potentiality"));
    }

    [Fact]
    public void TiersSortByLadderNotAlphabet()
    {
        var s = Motes.Summarize(
            [L("Mote of Major Potential", 1), L("Mote of Infinitesimal Potential", 4),
             L("Mote of Lesser Potential", 2), L("Mote of Minor Potential", 3)],
            TimeSpan.FromHours(2));
        Assert.Equal(
            ["Mote of Infinitesimal Potential", "Mote of Minor Potential",
             "Mote of Lesser Potential", "Mote of Major Potential"],
            s.Tiers.Select(t => t.Item).ToArray());
        Assert.Equal(10, s.Total);
        Assert.Equal(5.0, s.PerHour, 3);
    }

    [Fact]
    public void UnknownTierSurvivesAfterTheLadder()
    {
        var s = Motes.Summarize(
            [L("Mote of Zenith Potential", 1), L("Mote of Infinite Potential", 1),
             L("Mote of Potential", 5)],
            TimeSpan.FromHours(1));
        Assert.Equal(
            ["Mote of Potential", "Mote of Infinite Potential", "Mote of Zenith Potential"],
            s.Tiers.Select(t => t.Item).ToArray());
        // A tier the wiki hasn't taught us weighs nothing rather than guessing.
        Assert.Equal(0, Motes.PotencyOf("Mote of Zenith Potential"));
    }

    /// <summary>The ladder, verified against the wiki's Mote Guide table on 2026-08-16
    /// (#154, EzraSmith). Two rungs were wrong for nine days and nobody could see it,
    /// because the card counted motes and never weighed them: Major outranks Greater,
    /// and the bare "Mote of Potential" is the FOURTH rung rather than the bottom.</summary>
    [Fact]
    public void TheLadderIsTheWikisOrderIncludingMajorAboveGreater()
    {
        var s = Motes.Summarize(
            [L("Mote of Greater Potential", 1), L("Mote of Major Potential", 1),
             L("Mote of Lesser Potential", 1), L("Mote of Potential", 1)],
            TimeSpan.FromHours(1));
        Assert.Equal(
            ["Mote of Lesser Potential", "Mote of Potential",
             "Mote of Major Potential", "Mote of Greater Potential"],
            s.Tiers.Select(t => t.Item).ToArray());
    }

    /// <summary>The "Exp per Mote" column, copied rather than derived — the jump from 2
    /// to 4 skips 3, and a formula that looked tidier than the source would be exactly
    /// the "uniquely wrong" the match-the-wiki rule guards against.</summary>
    [Theory]
    [InlineData("Mote of Infinitesimal Potential", 1)]
    [InlineData("Mote of Minor Potential", 1)]
    [InlineData("Mote of Lesser Potential", 2)]
    [InlineData("Mote of Potential", 4)]
    [InlineData("Mote of Major Potential", 5)]
    [InlineData("Mote of Greater Potential", 6)]
    [InlineData("Mote of Superior Potential", 7)]
    [InlineData("Mote of Grand Potential", 8)]
    [InlineData("Mote of Ascendant Potential", 9)]
    [InlineData("Mote of Infinite Potential", 10)]
    public void EachRungIsWorthWhatTheWikiSays(string item, int exp) =>
        Assert.Equal(exp, Motes.PotencyOf(item));

    [Fact]
    public void PotencyWeighsTheHourRatherThanCountingIt()
    {
        // The whole point of #154: ten Infinitesimal motes are not ten Infinite ones.
        var cheap = Motes.Summarize([L("Mote of Infinitesimal Potential", 10)], TimeSpan.FromHours(1));
        var rich = Motes.Summarize([L("Mote of Infinite Potential", 10)], TimeSpan.FromHours(1));
        Assert.Equal(cheap.Total, rich.Total);
        Assert.Equal(10, cheap.Potency);
        Assert.Equal(100, rich.Potency);
        Assert.Equal(100, rich.PotencyPerHour, 3);

        // Mixed, and against the same one-minute floor the count rate uses.
        var mixed = Motes.Summarize(
            [L("Mote of Lesser Potential", 3), L("Mote of Major Potential", 2)],
            TimeSpan.FromHours(2));
        Assert.Equal(16, mixed.Potency);          // 3×2 + 2×5
        Assert.Equal(8, mixed.PotencyPerHour, 3);
    }

    /// <summary>The raid mote is worth a whole TIER rather than a number of points, so
    /// it counts on the card and weighs nothing — and its name carries no "Mote of"
    /// prefix, which is why the pattern alone cannot see it.</summary>
    [Fact]
    public void VoidTouchedCountsButCarriesNoExperience()
    {
        Assert.True(Motes.IsMote("Void-Touched Potential"));
        Assert.Equal(0, Motes.PotencyOf("Void-Touched Potential"));

        var s = Motes.Summarize(
            [L("Void-Touched Potential", 1), L("Mote of Minor Potential", 2)],
            TimeSpan.FromHours(1));
        Assert.Equal(3, s.Total);
        Assert.Equal(2, s.Potency);
        // Strongest thing you can loot: it sorts above the whole ladder.
        Assert.Equal("Void-Touched Potential", s.Tiers[^1].Item);
    }

    [Fact]
    public void ShortSessionsDoNotExplodeThePerHourRate()
    {
        // 2 motes in 30 seconds is not "240/hr" on the card — the rate floors at a
        // one-minute basis until the session has any length to speak of.
        var s = Motes.Summarize([L("Mote of Minor Potential", 2)], TimeSpan.FromSeconds(30));
        Assert.Equal(120, s.PerHour, 0);
        Assert.Equal(MotesSummary.Empty, Motes.Summarize([], TimeSpan.FromHours(1)));
    }
}
