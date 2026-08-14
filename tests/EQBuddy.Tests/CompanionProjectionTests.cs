using EQBuddy.Companion;
using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>The projection from desktop state to the phone's wire DTO: field mapping,
/// sorting, the due/imminent flags, the desktop gate, per-client filtering, and the
/// push fingerprint's "what counts as a change" contract.</summary>
public class CompanionProjectionTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 20, 0, 0);

    private static SpawnTimerState Timer(string name, double? durationSeconds, TimeSpan elapsed,
        string zone = "Lower Guk") =>
        new("legends", zone, name, Now - elapsed, durationSeconds);

    private static StatsSnapshot Stats(long version = 7) => new()
    {
        Version = version,
        CurrentZone = "Lower Guk",
        YourKillCount = 42,
        XpPerHour = 3.5,
        Elapsed = TimeSpan.FromMinutes(90),
        SessionDps = 55.5,
    };

    private static CompanionSnapshot Build(
        IReadOnlyList<SpawnTimerState>? timers = null,
        IReadOnlyList<string>? offered = null,
        StatsSnapshot? stats = null,
        DateTime? now = null) =>
        CompanionProjection.Build(stats ?? Stats(), timers ?? [], "Dranak", "1.79.0",
            now ?? Now, offered);

    [Fact]
    public void ProjectsIdentityAndSession()
    {
        var snap = Build();
        Assert.Equal("Dranak", snap.Identity.Character);
        Assert.Equal("Lower Guk", snap.Identity.Zone);
        Assert.Equal("1.79.0", snap.Identity.AppVersion);
        Assert.NotNull(snap.Session);
        Assert.Equal(42, snap.Session!.Kills);
        Assert.Equal(3.5, snap.Session.XpPerHour);
        Assert.Equal(90 * 60, snap.Session.SessionSeconds);
        Assert.Equal(55.5, snap.Session.SessionDps);
        Assert.Equal(7, snap.Version);
    }

    [Fact]
    public void TimersSortedByRemaining_DueFirst_UnknownLast()
    {
        var snap = Build(timers:
        [
            Timer("Slow One", 3600, TimeSpan.FromMinutes(5)),      // ~55m left
            Timer("No Clock", null, TimeSpan.FromMinutes(1)),      // unknown duration
            Timer("The Due One", 600, TimeSpan.FromMinutes(10.5)), // past due
            Timer("Soon", 600, TimeSpan.FromMinutes(9)),           // 60s left
        ]);
        var names = snap.Spawns!.Timers.Select(t => t.Name).ToList();
        Assert.Equal(new[] { "The Due One", "Soon", "Slow One", "No Clock" }, names);
    }

    [Fact]
    public void DueAndImminentFlags()
    {
        var snap = Build(timers:
        [
            Timer("Due", 600, TimeSpan.FromMinutes(11)),
            Timer("Imminent", 600, TimeSpan.FromMinutes(9)),   // 60s left ≤ 120
            Timer("Far", 3600, TimeSpan.FromMinutes(5)),
            Timer("Unknown", null, TimeSpan.FromMinutes(5)),
        ]);
        var byName = snap.Spawns!.Timers.ToDictionary(t => t.Name);
        Assert.True(byName["Due"].Due);
        Assert.Equal(0, byName["Due"].RemainingSeconds);
        Assert.False(byName["Due"].Imminent);          // due outranks imminent
        Assert.True(byName["Imminent"].Imminent);
        Assert.False(byName["Imminent"].Due);
        Assert.Equal(60, byName["Imminent"].RemainingSeconds!.Value, 1);
        Assert.False(byName["Far"].Due);
        Assert.False(byName["Far"].Imminent);
        Assert.Null(byName["Unknown"].RemainingSeconds);
        Assert.False(byName["Unknown"].Due);
    }

    [Fact]
    public void JsonIsCamelCase_AndOmitsNullSections()
    {
        var json = Build(timers: [Timer("Frenzy", 600, TimeSpan.FromMinutes(1))],
            offered: [CompanionSurfaces.Spawns]).ToJson();
        Assert.Contains("\"kind\":\"snapshot\"", json);
        Assert.Contains("\"spawns\":{\"timers\":[", json);
        Assert.Contains("\"remainingSeconds\":", json);
        Assert.Contains("\"appVersion\":\"1.79.0\"", json);
        Assert.DoesNotContain("\"session\"", json);    // gated off → absent, not null
        Assert.DoesNotContain("\"notOffered\"", json); // nothing was refused
    }

    [Fact]
    public void DesktopGate_NeverProjectsWithheldSections()
    {
        var snap = Build(offered: [CompanionSurfaces.Session]);
        Assert.Null(snap.Spawns);       // not even built, let alone sent
        Assert.NotNull(snap.Session);
        Assert.Equal(new[] { CompanionSurfaces.Session }, snap.Offered);
    }

    [Fact]
    public void ForSubscription_FiltersToTheClientsPicks()
    {
        var snap = Build(timers: [Timer("Frenzy", 600, TimeSpan.FromMinutes(1))]);
        var filtered = snap.ForSubscription([CompanionSurfaces.Session]);
        Assert.Null(filtered.Spawns);
        Assert.NotNull(filtered.Session);
        Assert.Null(filtered.NotOffered);
        // Null subscription = everything offered.
        var everything = snap.ForSubscription(null);
        Assert.NotNull(everything.Spawns);
        Assert.NotNull(everything.Session);
    }

    [Fact]
    public void ForSubscription_GatedSurfaceIsAnExplicitNo()
    {
        var snap = Build(offered: [CompanionSurfaces.Spawns]);
        var filtered = snap.ForSubscription([CompanionSurfaces.Spawns, CompanionSurfaces.Session]);
        Assert.NotNull(filtered.Spawns);
        Assert.Null(filtered.Session);
        Assert.Equal(new[] { CompanionSurfaces.Session }, filtered.NotOffered);
        Assert.Contains("\"notOffered\":[\"session\"]", filtered.ToJson());
    }

    [Fact]
    public void Fingerprint_IgnoresTheEverTickingNumbers()
    {
        var timers = new[] { Timer("Frenzy", 3600, TimeSpan.FromMinutes(5)) };
        var a = CompanionProjection.Fingerprint(Build(timers: timers, now: Now));
        var b = CompanionProjection.Fingerprint(Build(timers: timers, now: Now.AddSeconds(10)));
        Assert.Equal(a, b); // ten seconds of countdown is the phone's job, not a push
    }

    [Fact]
    public void Fingerprint_MovesOnTheTransitionsThatMatter()
    {
        var timers = new[] { Timer("Frenzy", 600, TimeSpan.FromMinutes(9)) };
        var baseline = CompanionProjection.Fingerprint(Build(timers: timers));

        // The due flip (2 minutes later this timer is past due).
        Assert.NotEqual(baseline, CompanionProjection.Fingerprint(
            Build(timers: timers, now: Now.AddMinutes(2))));
        // A new kill (stats version bump).
        Assert.NotEqual(baseline, CompanionProjection.Fingerprint(
            Build(timers: timers, stats: Stats(version: 8))));
        // The owner closes a gate.
        Assert.NotEqual(baseline, CompanionProjection.Fingerprint(
            Build(timers: timers, offered: [CompanionSurfaces.Spawns])));
        // A timer disappearing.
        Assert.NotEqual(baseline, CompanionProjection.Fingerprint(Build(timers: [])));
    }
}
