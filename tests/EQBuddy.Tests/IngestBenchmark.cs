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

    /// <summary>
    /// Repeated snapshots at the SAME version — i.e. the perf-audit-#12 cache doing its
    /// job. This is what an idle tick pays, and it is the number that stays flat however
    /// often you tick.
    /// </summary>
    [Fact]
    public void SnapshotCostWhenNothingChanged()
    {
        var stats = Loaded(30_000);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 1_000; i++) _ = stats.Snapshot();
        sw.Stop();
        output.WriteLine($"snapshot (cached): 1,000 reads in {sw.ElapsedMilliseconds:N0} ms " +
            $"= {sw.Elapsed.TotalMilliseconds / 1_000:N4} ms each");
        Assert.True(sw.ElapsedMilliseconds < 60_000);
    }

    /// <summary>
    /// The number that actually governs how fast EQBuddy Mobile may tick: a REBUILD,
    /// forced by advancing the version between every snapshot. The old version of this
    /// benchmark looped at one version and so measured 999 cache hits and one build,
    /// then described the result as what a 1 Hz tick pays — which understated a rebuild
    /// by the cache's whole benefit. Corrected 2026-08-15 while sizing the mobile
    /// coalescing window; a wrong number here would have picked that interval blind.
    /// </summary>
    [Fact]
    public void SnapshotRebuildCost()
    {
        var stats = Loaded(30_000);
        var hit = LogParser.Parse(
            "[Wed Aug 12 20:30:00 2026] You slash a gnoll pup for 100 points of damage.")!;

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 1_000; i++)
        {
            stats.Apply(hit);        // bumps the version, so the next call must rebuild
            _ = stats.Snapshot();
        }
        sw.Stop();
        var each = sw.Elapsed.TotalMilliseconds / 1_000;
        output.WriteLine($"snapshot (rebuild): 1,000 builds in {sw.ElapsedMilliseconds:N0} ms " +
            $"= {each:N3} ms each");
        output.WriteLine($"  -> a 20 Hz mobile pump costs at most {each * 20:N1} ms/s of one core " +
            "when state changes continuously; 0 while nothing changes or nobody is paired.");
        Assert.True(sw.ElapsedMilliseconds < 60_000);
    }

    private static SessionStats Loaded(int lines)
    {
        var stats = new SessionStats();
        foreach (var line in FightLog(lines))
            if (LogParser.Parse(line) is { } evt) stats.Apply(evt);
        return stats;
    }
}
