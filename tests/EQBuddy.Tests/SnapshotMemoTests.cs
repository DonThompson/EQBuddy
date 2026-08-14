using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>
/// Perf audit #12: SessionStats serves the identical snapshot instance while
/// (version, recent window, rules fingerprint) are unchanged — zeroing idle
/// rebuilds — and rebuilds the moment any of them moves. Snapshots are immutable,
/// which is what makes instance sharing safe.
/// </summary>
public class SnapshotMemoTests
{
    private static readonly DateTime T0 = new(2026, 8, 8, 20, 0, 0);

    private static string At(DateTime t, string msg) =>
        $"[{t.ToString("ddd MMM d HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture)}] {msg}";

    private static void Feed(SessionStats stats, DateTime t, string msg) =>
        stats.Apply(LogParser.Parse(At(t, msg))!);

    [Fact]
    public void UnchangedInputsReturnTheSameInstanceChangedInputsRebuild()
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };
        var rules = new List<TrackedRule>
        {
            new() { Name = "Bone Chips", Pattern = "bone chips", Kind = WatchKind.Loot },
        };
        // Past timestamps: CurrentDps is 0 at build time, so the memo may serve
        // freely (a cached 0 stays 0 without new events).
        Feed(stats, T0, "You slash a gnoll pup for 10 points of damage.");
        Feed(stats, T0.AddSeconds(4), "--You have looted a Bone Chips from a decaying skeleton's corpse.--");

        var w = TimeSpan.FromMinutes(10);
        var s1 = stats.Snapshot(w, rules);
        Assert.Same(s1, stats.Snapshot(w, rules));          // idle tick: no rebuild
        Assert.Equal(0, s1.CurrentDps, 3);

        // A different window is a different snapshot (its Recent block differs).
        var other = stats.Snapshot(TimeSpan.FromMinutes(5), rules);
        Assert.NotSame(s1, other);

        // A new event rebuilds — and the rebuilt snapshot sees it.
        Feed(stats, T0.AddSeconds(8), "--You have looted a Bone Chips from a decaying skeleton's corpse.--");
        var s2 = stats.Snapshot(w, rules);
        Assert.NotSame(s1, s2);
        Assert.Equal(2, s2.Tracked.Single().TotalQuantity);
        Assert.Equal(1, s1.Tracked.Single().TotalQuantity);   // the old instance is frozen

        // A rule edit changes the fingerprint: rebuild, even at the same version.
        rules[0].Pattern = "rusty";
        Assert.NotSame(s2, stats.Snapshot(w, rules));
    }

    [Fact]
    public void ResetAndIdentityChangesInvalidateTheCache()
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };
        Feed(stats, T0, "You have slain a gnoll pup!");
        var s1 = stats.Snapshot();
        Assert.Same(s1, stats.Snapshot());

        // The character key feeds the AA ledger the snapshot shows — identity
        // changes must never serve a cached snapshot.
        stats.CharacterName = "Douglas";
        var s2 = stats.Snapshot();
        Assert.NotSame(s1, s2);

        stats.Reset();
        var s3 = stats.Snapshot();
        Assert.NotSame(s2, s3);
        Assert.Equal(0, s3.YourKillCount);
    }

    [Fact]
    public void ParameterlessAndParameterizedSnapshotsDoNotServeEachOther()
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };
        var rules = new List<TrackedRule>
        {
            new() { Name = "Gnolls", Pattern = "gnoll", Kind = WatchKind.Kill },
        };
        Feed(stats, T0, "You have slain a gnoll pup!");

        var plain = stats.Snapshot();
        var withRules = stats.Snapshot(TimeSpan.FromMinutes(10), rules);
        Assert.NotSame(plain, withRules);
        Assert.Empty(plain.Tracked);
        Assert.Single(withRules.Tracked);
        Assert.NotNull(withRules.Recent);
        // And re-asking for either shape still answers correctly.
        Assert.Empty(stats.Snapshot().Tracked);
        Assert.Single(stats.Snapshot(TimeSpan.FromMinutes(10), rules).Tracked);
    }
}
