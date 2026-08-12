using System.Diagnostics;
using EQBuddy.Core;
using Xunit;
using Xunit.Abstractions;

namespace EQBuddy.Tests;

/// <summary>
/// Throughput benchmarks for the ingest pipeline — not assertions of speed (CI boxes
/// vary), but a printed number so an optimization pass can measure instead of guess.
/// Run explicitly: dotnet test --filter IngestBenchmark -- (see output).
/// </summary>
public class IngestBenchmark(ITestOutputHelper output)
{
    private static List<string> FightLog(int lines)
    {
        var rng = new Random(11);
        var t0 = DateTime.Parse("2026-08-12T20:00:00");
        var list = new List<string>(lines);
        for (var i = 0; i < lines; i++)
        {
            var t = t0.AddSeconds(i * 0.4);
            var stamp = $"[{t:ddd MMM d HH:mm:ss yyyy}] ";
            list.Add(stamp + (rng.Next(10) switch
            {
                0 => "You slash a gnoll pup for 142 points of damage.",
                1 => "You backstab a gnoll pup for 903 points of damage. (Critical)",
                2 => "You try to slash a gnoll pup, but a gnoll pup dodges!",
                3 => "A gnoll pup hits YOU for 33 points of damage.",
                4 => "A gnoll pup has taken 120 damage from your Flame Lick.",
                5 => "Soandso tells the guild, 'anyone camping AC?'",       // chat spam
                6 => "Cleric1 tells the raid, 'CH --> Tankname'",
                7 => "You gain experience! (1.2%)",
                8 => "--You have looted 2 Bone Chips from a gnoll pup's corpse.--",
                _ => "You have slain a gnoll pup!",
            }));
        }
        return list;
    }

    [Fact]
    public void ParseAndApplyThroughput()
    {
        var lines = FightLog(120_000);
        var stats = new SessionStats();
        var mez = new MezTracker();
        var slow = new SlowTracker();
        var buffs = new BuffTracker();
        var raids = new RaidKillLedger(path: null) { CharacterKey = () => "Bench|legends" };

        var sw = Stopwatch.StartNew();
        foreach (var line in lines)
        {
            var evt = LogParser.Parse(line);
            if (evt is not null)
            {
                stats.Apply(evt);
                mez.Apply(evt);
                slow.Apply(evt);
                buffs.Apply(evt);
                raids.Apply(evt);
            }
            stats.ObserveRawLine(line);
        }
        sw.Stop();
        output.WriteLine($"ingest: {lines.Count:N0} lines in {sw.ElapsedMilliseconds:N0} ms " +
            $"= {lines.Count * 1000.0 / Math.Max(1, sw.ElapsedMilliseconds):N0} lines/s");
        Assert.True(sw.ElapsedMilliseconds < 60_000);   // sanity ceiling only
    }

    [Fact]
    public void SnapshotCost()
    {
        var lines = FightLog(30_000);
        var stats = new SessionStats();
        foreach (var line in lines)
            if (LogParser.Parse(line) is { } evt) stats.Apply(evt);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) _ = stats.Snapshot();
        sw.Stop();
        output.WriteLine($"snapshot: 100 builds in {sw.ElapsedMilliseconds:N0} ms " +
            $"= {sw.ElapsedMilliseconds / 100.0:N2} ms each (a 1 Hz tick pays this every second)");
        Assert.True(sw.ElapsedMilliseconds < 60_000);
    }
}
