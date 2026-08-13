using EQBuddy.Core;
using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The fight-timeline builder: journal events in, lanes and DPS series out. Fed
/// through the real parser — the lane a mark lands in is a contract with the log,
/// not with hand-built events.
/// </summary>
public class FightTimelineTests
{
    private static readonly DateTime T0 = DateTime.Parse("2026-08-12T20:00:00");

    private static GameEvent Ev(int seconds, string message) =>
        LogParser.Parse($"[{T0.AddSeconds(seconds):ddd MMM d HH:mm:ss yyyy}] {message}")!;

    private static FightTimeline Build(double duration, string pet = "", params GameEvent[] events) =>
        TimelineBuilder.Build(events, T0, duration, pet);

    [Fact]
    public void HitsMissesAndResistsLandInTheRightLanes()
    {
        var t = Build(30, "",
            Ev(1, "You slash a gnoll pup for 42 points of damage."),
            Ev(3, "You try to slash a gnoll pup, but a gnoll pup dodges!"),
            Ev(5, "You kick a gnoll pup for 18 points of damage. (Critical)"),
            Ev(7, "Your target resisted the Flame Lick spell."));

        var slash = t.Lanes.Single(l => l.Name == "Slash");
        Assert.Equal(2, slash.Marks.Count);
        Assert.False(slash.Marks[0].Hollow);
        Assert.True(slash.Marks[1].Hollow);                      // the dodge
        Assert.Equal("a gnoll pup dodges", slash.Marks[1].Label); // log's own words

        Assert.True(t.Lanes.Single(l => l.Name == "Kick").Marks[0].Crit);
        var resist = t.Lanes.Single(l => l.Name == "Flame Lick");
        Assert.True(resist.Marks[0].Hollow);
        Assert.Equal("resisted", resist.Marks[0].Label);
    }

    [Fact]
    public void StanceAndInvocationChangesBecomePhaseMarks()
    {
        var t = Build(60, "",
            Ev(2, "You slash a gnoll pup for 10 points of damage."),
            Ev(10, "You assume a defensive stance."),
            Ev(30, "You begin reciting the empowering invocation."),
            Ev(40, "You slash a gnoll pup for 12 points of damage."));

        Assert.Equal(2, t.Phases.Count);
        Assert.Equal(10, t.Phases[0].Sec, 1);
        Assert.Contains("defensive", t.Phases[0].Label, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stance", t.Phases[0].Label);
        Assert.Contains("invocation", t.Phases[1].Label);
        Assert.Equal(2, t.EventCount);   // boundaries aren't events
    }

    [Fact]
    public void PetDamageGetsItsOwnLanesAndSeries()
    {
        var t = Build(20, pet: "Vexthar",
            Ev(1, "You slash a gnoll pup for 100 points of damage."),
            Ev(2, "Vexthar bites a gnoll pup for 50 points of damage."),
            Ev(3, "Gnarn claws a gnoll pup for 999 points of damage."));   // a stranger, not the pet

        var petLane = t.Lanes.Single(l => l.Kind == LaneKind.Pet);
        Assert.Equal("Bite", petLane.Name);
        Assert.Equal(50, petLane.Total);
        // The stranger's damage appears nowhere: not your lane, not the pet's.
        Assert.DoesNotContain(t.Lanes, l => l.Total == 999);
        Assert.True(t.PetDpsSeries.Max() > 0);
    }

    [Fact]
    public void IncomingIsOneLaneWithAttackerAndReasonOnTheTooltip()
    {
        var t = Build(20, "",
            Ev(1, "A gnoll pup bites YOU for 12 points of damage."),
            Ev(2, "A gnoll pup tries to bite YOU, but misses!"),
            Ev(3, "A gnoll pup tries to bite YOU, but YOUR magical skin absorbs the blow!"));

        var incoming = t.Lanes.Single(l => l.Kind == LaneKind.Incoming);
        Assert.Equal(3, incoming.Marks.Count);
        // Normalize strips the article, same as every other surface.
        Assert.Equal("Gnoll pup · Bite", incoming.Marks[0].Label);
        Assert.True(incoming.Marks[1].Hollow);
        Assert.Contains("absorbed by rune", incoming.Marks[2].Label);
        Assert.Equal(12, (int)t.Lanes.Single(l => l.Kind == LaneKind.Incoming).Total);
    }

    [Fact]
    public void DotTicksShareTheSpellsLaneButSaySo()
    {
        var t = Build(20, "",
            Ev(1, "A gnoll pup has taken 30 damage from your Flame Lick."));

        Assert.Contains(t.Lanes, l => l.Name == "Flame Lick (DoT)");
    }

    [Fact]
    public void TheLongTailFoldsIntoOther()
    {
        var events = new List<GameEvent>();
        // 15 distinct one-hit "spells" via DoT lines — more lanes than the cap.
        for (var i = 0; i < 15; i++)
            events.Add(Ev(i + 1, $"A gnoll pup has taken {10 + i} damage from your Spell{i}."));
        var t = Build(30, "", [.. events]);

        Assert.True(t.Lanes.Count <= TimelineBuilder.MaxLanes);
        var other = t.Lanes.Single(l => l.Name.StartsWith("Other ("));
        Assert.Contains("Spell", other.Marks[0].Label);   // folded marks keep their identity
    }

    [Fact]
    public void DpsSeriesIsRolledAndPeakIsFound()
    {
        // 600 damage in one second, then silence: the rolling window spreads it,
        // so the peak reads 100 (600/6s window), not a 600-DPS lie.
        var t = Build(20, "",
            Ev(5, "You slash a gnoll pup for 600 points of damage."));

        Assert.Equal(100, t.PeakDps, 0);
        Assert.Equal(5, t.PeakSec, 0);
        Assert.Equal(100, t.DpsSeries[10], 0);   // still inside the 6 s window
        Assert.Equal(0, t.DpsSeries[12], 0);     // outside it
    }

    [Fact]
    public void EventsOutsideTheWindowAreIgnored()
    {
        var t = Build(10, "",
            Ev(-5, "You slash a gnoll pup for 42 points of damage."),
            Ev(15, "You slash a gnoll pup for 42 points of damage."));

        Assert.Empty(t.Lanes);
        Assert.Equal(0, t.EventCount);
    }

    [Fact]
    public void MissLinesCarryTheirSkillThroughTheParser()
    {
        var miss = (MissEvent)Ev(0, "You try to crush an orc pawn, but miss!");
        Assert.True(miss.Outgoing);
        Assert.Equal("Crush", miss.Ability);
        Assert.Equal("miss", miss.Reason);
    }
}
