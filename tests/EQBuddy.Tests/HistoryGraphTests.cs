using System.Text.Json;
using System.Text.Json.Serialization;
using EQBuddy.Core;
using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>The History window's DPS-over-time graph: per-minute damage buckets
/// accumulated in SessionStats, persisted through the snapshot JSON, and laid out by
/// HistoryPresentation.BuildDpsGraph.</summary>
public class HistoryGraphTests
{
    private static TimelinePoint At(string hhmm, long damage) =>
        new(DateTime.Parse($"2026-07-18T{hhmm}:00"), damage);

    [Fact]
    public void DamageAccumulatesIntoMinuteBuckets()
    {
        var stats = new SessionStats();
        stats.Apply(LogParser.Parse("[Sat Jul 18 15:00:05 2026] You slash an orc pawn for 10 points of damage.")!);
        stats.Apply(LogParser.Parse("[Sat Jul 18 15:00:40 2026] You slash an orc pawn for 15 points of damage.")!);
        stats.Apply(LogParser.Parse("[Sat Jul 18 15:02:10 2026] You slash an orc pawn for 7 points of damage.")!);

        var timeline = stats.Snapshot().DamageTimeline;
        Assert.Equal(2, timeline.Count);                       // 15:01 had no damage → absent
        Assert.Equal(25, timeline[0].Damage);
        Assert.Equal(7, timeline[1].Damage);
        Assert.Equal(0, timeline[0].Time.Second);              // bucket-aligned, not event time
    }

    [Fact]
    public void TimelineSurvivesTheSnapshotJsonRoundTrip()
    {
        // Same options the session repository serializes with.
        var opts = new JsonSerializerOptions
        { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var snapshot = new StatsSnapshot { DamageTimeline = [At("15:00", 600), At("15:01", 1200)] };

        var restored = JsonSerializer.Deserialize<StatsSnapshot>(
            JsonSerializer.Serialize(snapshot, opts), opts)!;
        Assert.Equal(snapshot.DamageTimeline, restored.DamageTimeline);
    }

    [Fact]
    public void PreTimelineSnapshotsDeserializeToAnEmptyTimeline()
    {
        // A snapshot archived before the field existed has no such JSON property.
        var restored = JsonSerializer.Deserialize<StatsSnapshot>("""{"DamageDealt":123}""")!;
        Assert.Empty(restored.DamageTimeline);
        Assert.Null(HistoryPresentation.BuildDpsGraph(restored.DamageTimeline, 300, 64));
    }

    [Fact]
    public void GraphNormalizesToPeakAndFillsIdleMinutesWithZero()
    {
        var graph = HistoryPresentation.BuildDpsGraph(
            [At("15:00", 600), At("15:02", 300)], width: 300, height: 60)!;

        Assert.Equal(3, graph.Points.Count);                   // 15:01 drawn, at zero
        Assert.Equal(10.0, graph.PeakDps);                     // 600 dmg / 60 s
        Assert.Equal(0, graph.Points[0].Y);                    // peak minute touches the top
        Assert.Equal(60, graph.Points[1].Y);                   // idle minute sits on the floor
        Assert.Equal(30, graph.Points[2].Y, 3);                // half the peak → halfway up
        Assert.Equal(0, graph.Points[0].X);
        Assert.Equal(150, graph.Points[1].X, 3);
        Assert.Equal(300, graph.Points[2].X, 3);
    }

    [Fact]
    public void DegenerateTimelinesProduceNoGraph()
    {
        Assert.Null(HistoryPresentation.BuildDpsGraph([], 300, 60));
        Assert.Null(HistoryPresentation.BuildDpsGraph([At("15:00", 500)], 300, 60));
        Assert.Null(HistoryPresentation.BuildDpsGraph(
            [At("15:00", 500), At("15:01", 100)], 0, 60));
    }
}
