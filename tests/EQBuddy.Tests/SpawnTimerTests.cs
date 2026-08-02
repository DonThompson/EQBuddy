using EQBuddy.Core;
using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// Spawn timers: the shipped catalog, kill matching (named + placeholder, zone-gated,
/// per-server), countdown lifecycle, player overrides, and the window's view model.
/// </summary>
public class SpawnTimerTests
{
    private static readonly DateTime T0 = new(2026, 7, 18, 15, 0, 0);

    private static SpawnCatalog TestCatalog() => new()
    {
        Zones =
        [
            new SpawnZone
            {
                Zone = "Lower Guk",
                LogZoneName = "The Ruins of Old Guk",
                NamedDefaultSeconds = 1680,
                Named =
                [
                    new SpawnEntry { Name = "a froglok ghoul lord", RespawnSeconds = 1620 },
                    new SpawnEntry { Name = "the ghoul arch magi", Placeholder = "kor ghoul wizard" },
                ],
            },
            new SpawnZone
            {
                Zone = "Permafrost Keep",
                Named = [new SpawnEntry { Name = "Lady Vox", RespawnSeconds = 604800, Variance = "±8h" }],
            },
        ],
    };

    private static SpawnTimers Tracker(SpawnOverrides? overrides = null, string? path = null) =>
        new(TestCatalog(), overrides ?? new SpawnOverrides(), path) { Server = "freeport" };

    // ---- the shipped catalog ----

    [Fact]
    public void EmbeddedCatalogLoadsAndIsComprehensive()
    {
        var cat = SpawnCatalog.LoadEmbedded();
        Assert.True(cat.Zones.Count >= 100, $"only {cat.Zones.Count} zones");
        Assert.True(cat.Zones.Sum(z => z.Named.Count) >= 800, "named entries went missing");
        // Every zone parses; no entry has a negative or absurd timer (8 days is the
        // ceiling anything documented reaches).
        foreach (var z in cat.Zones)
        foreach (var n in z.Named)
            if (n.RespawnSeconds is { } s)
                Assert.InRange(s, 30, 8 * 86400);
    }

    [Fact]
    public void FindZoneShrugsOffArticlesAndLogNames()
    {
        var cat = SpawnCatalog.LoadEmbedded();
        Assert.NotNull(cat.FindZone("Estate of Unrest"));
        Assert.NotNull(cat.FindZone("The Estate of Unrest"));
        Assert.NotNull(cat.FindZone("Lower Guk"));
    }

    /// <summary>EQ Legends runs difficulty-tier instances of a zone — the log says
    /// "Befallen 1 (Awakened)" or "Befallen 4 (Refined)" (both observed in
    /// eqlog_Hugzee). They resolve to the base zone so Follow and kill matching keep
    /// working there.</summary>
    [Theory]
    [InlineData("Befallen 1 (Awakened)", "Befallen")]
    [InlineData("Befallen 4 (Refined)", "Befallen")]
    [InlineData("Befallen 2", "Befallen")]
    public void DifficultyTierZonesResolveToTheirBase(string logZone, string expected)
    {
        var cat = SpawnCatalog.LoadEmbedded();
        Assert.Equal(expected, cat.FindZone(logZone)?.Zone);
    }

    [Theory]
    [InlineData("Befallen 1 (Awakened)", "Befallen")]
    [InlineData("Clan Crushbone 2 (Adaptive)", "Clan Crushbone")]
    [InlineData("Befallen", "Befallen")]
    [InlineData("Solusek's Eye", "Solusek's Eye")]   // no tier suffix — unchanged
    public void TierVariantStrippingIsConservative(string input, string expected) =>
        Assert.Equal(expected, SpawnCatalog.StripTierVariant(input));

    [Theory]
    [InlineData("a froglok ghoul lord", "froglok ghoul lord", true)]   // article
    [InlineData("orc centurions", "orc centurion", true)]              // plural note
    [InlineData("Lady Vox", "lady vox", true)]                         // case
    [InlineData("a froglok ghoul lord", "froglok ghoul", false)]       // prefix is not a match
    [InlineData("", "anything", false)]
    public void NameMatchingIsForgivingButNotFuzzy(string catalogName, string killed, bool expected) =>
        Assert.Equal(expected, SpawnCatalog.NameMatches(catalogName, killed));

    // ---- kill-driven timers ----

    [Fact]
    public void AKillInTheCurrentZoneStartsTheCountdown()
    {
        var t = Tracker();
        t.Apply(new ZoneEvent(T0, "The Ruins of Old Guk"));
        t.Apply(new KillEvent(T0.AddMinutes(1), "froglok ghoul lord", "You"));

        var timer = Assert.Single(t.Snapshot(T0.AddMinutes(2)));
        Assert.Equal("a froglok ghoul lord", timer.Name);
        Assert.Equal(T0.AddMinutes(1).AddSeconds(1620), timer.DueAt);
    }

    [Fact]
    public void KillingThePlaceholderRunsTheSameClock()
    {
        var t = Tracker();
        t.Apply(new ZoneEvent(T0, "Lower Guk"));
        t.Apply(new KillEvent(T0, "kor ghoul wizard", "Lizzid"));

        var timer = Assert.Single(t.Snapshot(T0.AddMinutes(1)));
        Assert.Equal("the ghoul arch magi", timer.Name);
        // No per-mob timer documented — the zone's named default carries it.
        Assert.Equal(T0.AddSeconds(1680), timer.DueAt);
    }

    [Fact]
    public void KillsMatchNothingWithoutAZoneAndNothingAcrossZones()
    {
        var t = Tracker();
        t.Apply(new KillEvent(T0, "froglok ghoul lord", "You"));      // no zone yet
        Assert.Empty(t.Snapshot(T0));

        t.Apply(new ZoneEvent(T0, "Permafrost Keep"));
        t.Apply(new KillEvent(T0.AddMinutes(1), "froglok ghoul lord", "You")); // wrong zone
        Assert.Empty(t.Snapshot(T0.AddMinutes(1)));
    }

    [Fact]
    public void ReplayingTheLogNeverRewindsATimer()
    {
        var t = Tracker();
        t.Apply(new ZoneEvent(T0, "Lower Guk"));
        t.Apply(new KillEvent(T0.AddMinutes(5), "froglok ghoul lord", "You"));
        // Startup ingest replays the same kill, then an older one from earlier in the log.
        t.Apply(new KillEvent(T0.AddMinutes(5), "froglok ghoul lord", "You"));
        t.Apply(new KillEvent(T0.AddMinutes(2), "froglok ghoul lord", "You"));

        var timer = Assert.Single(t.Snapshot(T0.AddMinutes(6)));
        Assert.Equal(T0.AddMinutes(5), timer.KilledAt);

        // A genuinely newer kill restarts the clock.
        t.Apply(new KillEvent(T0.AddMinutes(30), "froglok ghoul lord", "You"));
        Assert.Equal(T0.AddMinutes(30), Assert.Single(t.Snapshot(T0.AddMinutes(31))).KilledAt);
    }

    [Fact]
    public void TimersArePerServer()
    {
        var t = Tracker();
        t.Apply(new ZoneEvent(T0, "Lower Guk"));
        t.Apply(new KillEvent(T0, "froglok ghoul lord", "You"));

        t.Server = "qeynos";   // character switch to another server
        Assert.Empty(t.Snapshot(T0.AddMinutes(1)));
        t.Server = "freeport";
        Assert.Single(t.Snapshot(T0.AddMinutes(1)));
    }

    [Fact]
    public void AnOverriddenDurationBeatsTheCatalog()
    {
        var overrides = new SpawnOverrides();
        overrides.GetOrAdd("Lower Guk", "a froglok ghoul lord").RespawnSeconds = 2000;
        var t = Tracker(overrides);
        t.Apply(new ZoneEvent(T0, "Lower Guk"));
        t.Apply(new KillEvent(T0, "froglok ghoul lord", "You"));

        Assert.Equal(T0.AddSeconds(2000), Assert.Single(t.Snapshot(T0)).DueAt);
    }

    [Fact]
    public void ManualStartAndDurationEditsRederiveTheCountdown()
    {
        var t = Tracker();
        t.StartManual("Permafrost Keep", "Lady Vox", 604800, elapsed: TimeSpan.FromHours(2));

        var timer = Assert.Single(t.Snapshot(DateTime.Now));
        Assert.True(timer.DueAt < DateTime.Now.AddDays(7));

        t.SetDuration("Permafrost Keep", "Lady Vox", 3 * 86400);
        Assert.Equal(timer.KilledAt.AddDays(3), Assert.Single(t.Snapshot(DateTime.Now)).DueAt);
    }

    [Fact]
    public void DueTimersLingerThenDrop()
    {
        var t = Tracker();
        t.Apply(new ZoneEvent(T0, "Lower Guk"));
        t.Apply(new KillEvent(T0, "froglok ghoul lord", "You"));    // 27 min timer

        // Due but within the linger window (clamped to at least an hour): still shown.
        Assert.Single(t.Snapshot(T0.AddMinutes(80)));   // 53 min past the 27-min due point
        // An hour past due (linger = max(duration, 1h)): gone.
        Assert.Empty(t.Snapshot(T0.AddSeconds(1620).AddHours(1).AddMinutes(1)));
    }

    [Fact]
    public void TimersSurviveARestartThroughThePersistFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"spawn-timers-{Guid.NewGuid():N}.json");
        try
        {
            var t = Tracker(path: path);
            t.Apply(new ZoneEvent(T0, "Lower Guk"));
            t.Apply(new KillEvent(T0, "froglok ghoul lord", "You"));

            var reborn = Tracker(path: path);
            var timer = Assert.Single(reborn.Snapshot(T0.AddMinutes(1)));
            Assert.Equal(T0, timer.KilledAt);
        }
        finally { File.Delete(path); }
    }

    // ---- duration text ----

    [Theory]
    [InlineData("22", 1320)]        // bare number = minutes, the wiki convention
    [InlineData("90s", 90)]
    [InlineData("8m", 480)]
    [InlineData("12h", 43200)]
    [InlineData("3d", 259200)]
    [InlineData("3d 12h", 302400)]
    [InlineData("1h30m", 5400)]
    [InlineData("6:40", 400)]       // m:ss, how eqlwiki writes zone timers
    [InlineData("1:00:00", 3600)]
    public void DurationTextParses(string text, double seconds) =>
        Assert.Equal(seconds, SpawnDurationText.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("h")]
    [InlineData("1:2:3:4")]
    public void DurationTextRejectsNoise(string text) =>
        Assert.Null(SpawnDurationText.Parse(text));

    [Theory]
    [InlineData(1320, "22m")]
    [InlineData(302400, "3d 12h")]
    [InlineData(400, "6m 40s")]
    [InlineData(90, "1m 30s")]
    public void DurationTextFormats(double seconds, string expected) =>
        Assert.Equal(expected, SpawnDurationText.Format(seconds));

    // ---- the view model ----

    private static (SpawnsViewModel Vm, SpawnTimers Timers, SpawnOverrides Overrides) Vm()
    {
        var overrides = new SpawnOverrides();
        var timers = new SpawnTimers(TestCatalog(), overrides) { Server = "freeport" };
        return (new SpawnsViewModel(TestCatalog(), overrides, timers), timers, overrides);
    }

    [Fact]
    public void RowsPutRunningTimersFirstAndNamePlaceholders()
    {
        var (vm, timers, _) = Vm();
        timers.Apply(new ZoneEvent(T0, "Lower Guk"));
        timers.Apply(new KillEvent(T0, "kor ghoul wizard", "You"));

        var rows = vm.RowsFor("Lower Guk", T0.AddMinutes(1));
        Assert.Equal(2, rows.Count);
        Assert.Equal("the ghoul arch magi", rows[0].Name);   // running timer sorts first
        Assert.True(rows[0].HasActiveTimer);
        Assert.Equal("the ghoul arch magi — Placeholder (kor ghoul wizard)", rows[0].DisplayName);
        Assert.Equal("27m", rows[1].DurationText);           // catalog 1620 s
    }

    [Fact]
    public void EditingADurationSticksAsAnOverrideAndRetimesTheClock()
    {
        var (vm, timers, overrides) = Vm();
        timers.Apply(new ZoneEvent(T0, "Lower Guk"));
        timers.Apply(new KillEvent(T0, "froglok ghoul lord", "You"));

        vm.SetDuration("Lower Guk", "a froglok ghoul lord", "30m");

        Assert.Equal(1800, overrides.Find("Lower Guk", "a froglok ghoul lord")!.RespawnSeconds);
        Assert.Equal(T0.AddMinutes(30), Assert.Single(timers.Snapshot(T0)).DueAt);
    }

    [Fact]
    public void CustomNamedJoinTheirZoneAndDuplicatesAreRefused()
    {
        var (vm, _, _) = Vm();
        Assert.True(vm.AddCustom("Lower Guk", "the Fabled Froglok", "45m"));
        Assert.False(vm.AddCustom("Lower Guk", "a froglok ghoul lord", "45m")); // already catalogued

        var rows = vm.RowsFor("Lower Guk", T0);
        Assert.Contains(rows, r => r.Name == "the Fabled Froglok" && r.IsCustom && r.DurationText == "45m");
    }

    [Fact]
    public void DueAlertsFireOnceOnTheLiveTransitionAndNeverOnStartup()
    {
        var (vm, timers, _) = Vm();
        timers.Apply(new ZoneEvent(T0, "Lower Guk"));
        timers.Apply(new KillEvent(T0, "froglok ghoul lord", "You"));

        // First look happens after the timer already expired — startup priming, no alert.
        Assert.Empty(vm.ConsumeDueAlerts(T0.AddMinutes(60)));

        // A fresh kill counts down live: nothing while running, one alert at zero, silent after.
        timers.Apply(new KillEvent(T0.AddMinutes(70), "froglok ghoul lord", "You"));
        Assert.Empty(vm.ConsumeDueAlerts(T0.AddMinutes(71)));
        var due = vm.ConsumeDueAlerts(T0.AddMinutes(70 + 28));
        Assert.Equal("a froglok ghoul lord", Assert.Single(due).Name);
        Assert.Empty(vm.ConsumeDueAlerts(T0.AddMinutes(70 + 29)));
    }

    /// <summary>ConsumeNewTimers drives the pop-on-kill window: recovered timers pop at
    /// startup (unlike due ALERTS, which prime silently), each kill pops once, and a
    /// re-kill pops again because it carries a new kill time.</summary>
    [Fact]
    public void NewTimersReportOnceIncludingThoseRecoveredAtStartup()
    {
        var (vm, timers, _) = Vm();
        timers.Apply(new ZoneEvent(T0, "Lower Guk"));
        timers.Apply(new KillEvent(T0, "froglok ghoul lord", "You"));   // "recovered" during ingest

        var first = vm.ConsumeNewTimers(T0.AddMinutes(1));
        Assert.Equal("a froglok ghoul lord", Assert.Single(first).Name);
        Assert.Empty(vm.ConsumeNewTimers(T0.AddMinutes(2)));            // unchanged — no re-pop

        timers.Apply(new KillEvent(T0.AddMinutes(5), "froglok ghoul lord", "You"));
        Assert.Single(vm.ConsumeNewTimers(T0.AddMinutes(6)));           // re-kill = new information

        Assert.True(vm.HasActiveTimers(T0.AddMinutes(7)));
    }

    [Fact]
    public void RowsWithAlertToggledOffStayQuiet()
    {
        var (vm, timers, _) = Vm();
        vm.ToggleAlert("Lower Guk", "a froglok ghoul lord");   // default on → off
        vm.ConsumeDueAlerts(T0);                               // prime

        timers.Apply(new ZoneEvent(T0, "Lower Guk"));
        timers.Apply(new KillEvent(T0.AddMinutes(1), "froglok ghoul lord", "You"));
        Assert.Empty(vm.ConsumeDueAlerts(T0.AddMinutes(1 + 28)));
    }
}
