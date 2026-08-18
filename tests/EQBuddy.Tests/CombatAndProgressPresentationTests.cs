using EQBuddy.Core;
using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The Combat, Healing and Progress summaries (Gate 5b) — the densest text in the app, and
/// until now the least assertable: roughly a dozen conditional fragments each, composed
/// inline in <c>RefreshUi</c> on the card a player reads most.
///
/// Several of these conditions encode decisions that were argued over rather than defaults
/// that fell out. Those are the ones worth pinning.
/// </summary>
public class CombatAndProgressPresentationTests
{
    // ---- combat ----

    /// <summary>Both DPS models appear, each labelled. Neither is "the" DPS: in-combat is
    /// the honest camp number because medding doesn't dilute it, and wall-clock is what a
    /// raid night actually produced. A bare number would let the reader assume.</summary>
    [Fact]
    public void BothDpsModelsAreNamedRatherThanOneBeingCalledTheDps()
    {
        var start = new DateTime(2026, 7, 18, 15, 0, 0);
        var line = Assert.Single(CombatPresentation.SummaryLines(new StatsSnapshot
        {
            DamageDealt = 36000, SessionDps = 40, SessionStart = start,
            LastEventTime = start.AddHours(1),
        }), l => l.StartsWith("Session dps:", StringComparison.Ordinal));

        Assert.Contains("in combat", line);
        Assert.Contains("wall-clock", line);
    }

    /// <summary>A window that has not filled yet says so — a 3-minute average presented as
    /// a 15-minute one is a lie in the direction of whatever just happened.</summary>
    [Fact]
    public void APartialRecentWindowLabelsItself()
    {
        var partial = CombatPresentation.SummaryLines(new StatsSnapshot
        {
            Recent = new RecentRates(TimeSpan.FromMinutes(15), false, 0, 0, 0, 0, 120, 0),
        });
        Assert.Contains(partial, l => l.Contains("(partial window)"));

        var full = CombatPresentation.SummaryLines(new StatsSnapshot
        {
            Recent = new RecentRates(TimeSpan.FromMinutes(15), true, 0, 0, 0, 0, 120, 0),
        });
        Assert.DoesNotContain(full, l => l.Contains("(partial window)"));
    }

    /// <summary>Cast completion SUBSUMES the fizzle count, so a log carrying cast lines
    /// must not also print the older fizzle/resist line — that is the same fact twice.</summary>
    [Fact]
    public void CastCompletionReplacesTheOlderFizzleLineRatherThanJoiningIt()
    {
        var line = CombatPresentation.CastingLine(new StatsSnapshot
        {
            CastsStarted = 50, CastsInterrupted = 5, Fizzles = 5, Resists = 2,
        });
        Assert.StartsWith("Casts 50", line, StringComparison.Ordinal);
        Assert.Contains("80% completed", line);
        Assert.DoesNotContain("Fizzles", line);
    }

    /// <summary>A log with NO cast lines still reports what it can.</summary>
    [Fact]
    public void ALogWithoutCastLinesFallsBackToFizzlesAndResists()
    {
        var line = CombatPresentation.CastingLine(new StatsSnapshot { Fizzles = 3, Resists = 2 });
        Assert.StartsWith("Fizzles 3", line, StringComparison.Ordinal);
    }

    /// <summary>Nothing went wrong, so nothing is said. "0 fizzled" is not news.</summary>
    [Fact]
    public void NothingIsSaidAboutCastingWhenNothingFailed() =>
        Assert.Null(CombatPresentation.CastingLine(new StatsSnapshot()));

    /// <summary>Blocked is a STACKING fact — a completed cast a standing buff refused —
    /// not a casting failure, so it appears only when it happened rather than sitting at
    /// zero beside the real failures.</summary>
    [Fact]
    public void BlockedJoinsTheLineOnlyWhenItHappened()
    {
        Assert.DoesNotContain("blocked",
            CombatPresentation.CastingLine(new StatsSnapshot { Fizzles = 1 })!);
        Assert.Contains("blocked",
            CombatPresentation.CastingLine(new StatsSnapshot { Fizzles = 1, Blocked = 2 })!);
    }

    // ---- healing ----

    /// <summary>The regen estimate depends on a SETTING, so the card composes it and hands
    /// it in. With no ticks there is nothing to say, whatever the caller passes.</summary>
    [Fact]
    public void TheRegenLineAppearsOnlyWhenThereWereTicks()
    {
        Assert.DoesNotContain(
            CombatPresentation.HealingLines(new StatsSnapshot(), "est. ~500 healed"),
            l => l.Contains("healed"));

        Assert.Contains(
            CombatPresentation.HealingLines(new StatsSnapshot { RegenTicks = 12 }, "est. ~500 healed"),
            l => l.Contains("est. ~500 healed"));
    }

    /// <summary>One absorbed hit is a hit, not "1 hits".</summary>
    [Fact]
    public void TheRuneLineAgreesWithItselfAboutPlurals()
    {
        var one = CombatPresentation.HealingLines(
            new StatsSnapshot { RuneBlockCount = 1, RuneBlockStreakMax = 1 }, null);
        Assert.Contains(one, l => l.Contains("1 hit (") && !l.Contains("1 hits"));
    }

    /// <summary>Healer rows are totals and counts — never ranked against each other or
    /// against yours. Measuring other players is the line this project does not cross.</summary>
    [Fact]
    public void HealerRowsCarryNoComparison()
    {
        var rows = CombatPresentation.HealerRows(new StatsSnapshot
        {
            HealsByHealer = [new SourceDamage("Someone", Hits: 12, Total: 5000)],
        });
        var row = Assert.Single(rows);
        Assert.Contains("12 heals", row.Value);
        Assert.DoesNotContain("%", row.Value);
        Assert.False(row.Item);
    }

    // ---- progress ----

    /// <summary>The FIRST level of a session is measured from the session start; every
    /// later one from the previous ding. Getting that wrong reports the first as 0m.</summary>
    [Fact]
    public void TheFirstLevelOfASessionIsMeasuredFromTheSessionStart()
    {
        var start = new DateTime(2026, 7, 18, 15, 0, 0);
        var text = ProgressPresentation.Levels(new StatsSnapshot
        {
            SessionStart = start,
            Levels =
            [
                new TimedDetail(start.AddMinutes(40), "Level 12"),
                new TimedDetail(start.AddMinutes(95), "Level 13"),
            ],
        });

        Assert.Contains("Level 12", text);
        Assert.Contains("(40m)", text);   // from the session start
        Assert.Contains("(55m)", text);   // from the previous ding, not the start
    }

    [Fact]
    public void NoLevelsMeansNoLine() =>
        Assert.Equal("", ProgressPresentation.Levels(new StatsSnapshot()));

    /// <summary>Hours appear only once there is one, and a level minutes away says a
    /// minute rather than "~0m".</summary>
    [Theory]
    [InlineData(2.25, "~2h 15m")]
    [InlineData(0.75, "~45m")]
    [InlineData(0.001, "~1m")]
    public void EtaReadsAsTimeRatherThanADecimal(double hours, string expected) =>
        Assert.Equal(expected, ProgressPresentation.FormatEta(hours));

    [Fact]
    public void AaProgressIsReportedOnlyWhenSomeWasEarned()
    {
        Assert.DoesNotContain(ProgressPresentation.SummaryLines(new StatsSnapshot()),
            l => l.Contains("AA"));
        Assert.Contains(
            ProgressPresentation.SummaryLines(new StatsSnapshot { AaGained = 1, AaTotal = 4 }),
            l => l.Contains("1 AA point ") && !l.Contains("1 AA points"));
    }
}
