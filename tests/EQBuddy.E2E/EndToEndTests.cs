using EQBuddy.Core;

namespace EQBuddy.E2E;

// One app instance per test, never two at once: each launch shows a real always-on-top
// widget, and parallel widgets would fight for the desktop (and the CPU the replay uses).
[CollectionDefinition("e2e", DisableParallelization = true)]
public sealed class E2ECollection;

/// <summary>
/// The four v1 scenarios, end to end: the REAL EQBuddy.exe, a real log file growing
/// under it, assertions on the rendered result via the EQBUDDY_EXPAND state dump and
/// on the persisted history.db. Line shapes are copied verbatim from the fixture —
/// they are what LogParser actually matches.
/// </summary>
[Collection("e2e")]
public sealed class EndToEndTests
{
    // "a training dummy" appears nowhere in the fixture, so a fresh kill/loot of it
    // moves both the distinct-row counts and the totals by exactly one.
    private const string MeleeHit = "You crush a training dummy for 25 points of damage.";
    private const string Kill = "You have slain a training dummy!";

    [Fact]
    public void SessionGoesLive_AndFreshKillUpdatesLiveStats()
    {
        using var app = new AppHarness();
        app.Launch();   // waits for the replayed session to be live

        var kills = app.DumpValue("killsTotal");
        var killRows = app.DumpValue("kills");
        Assert.True(kills > 0, "fixture replay should land kills");

        app.AppendLogLines(MeleeHit, Kill);

        app.WaitForDump("killsTotal", kills + 1, "the fresh kill to reach the widget");
        app.WaitForDump("kills", killRows + 1, "the new creature to get its own kill row");
    }

    [Fact]
    public void KillThenLoot_ShowsUpOnTheLootSurface()
    {
        using var app = new AppHarness();
        app.Launch();

        var lootTotal = app.DumpValue("lootTotal");
        var lootRows = app.DumpValue("loot");
        Assert.True(lootTotal > 0, "fixture replay should land loot");

        app.AppendLogLines(MeleeHit, Kill,
            "--You have looted a Harness Test Trinket from a training dummy's corpse.--");

        app.WaitForDump("lootTotal", lootTotal + 1, "the looted item to reach the widget");
        app.WaitForDump("loot", lootRows + 1, "the new item to get its own loot row");
    }

    [Fact]
    public void SeededWatchRule_CountsItsMatchingLoot()
    {
        using var app = new AppHarness(settings =>
            settings.TrackedRules.Add(new TrackedRule
            {
                Name = "Harness Test Widget",   // pattern falls back to the name
                Kind = WatchKind.Loot,
                AlertBanner = false,            // counting is under test, not alerting
            }));
        app.Launch();

        app.WaitForDump("tracked", 0, "the seeded rule to be live with no matches yet");

        app.AppendLogLines(
            "--You have looted a Harness Test Widget from a training dummy's corpse.--");

        app.WaitForDump("tracked", 1, "the watch rule's total to increment on the match");
    }

    [Fact]
    public void Session_SurvivesGracefulCloseAndRelaunch()
    {
        using var app = new AppHarness();
        app.Launch();

        var kills = app.DumpValue("killsTotal");
        app.AppendLogLines(MeleeHit, Kill);
        app.WaitForDump("killsTotal", kills + 1, "the fresh kill to land before closing");

        app.CloseGracefully();   // finalizes the session into history.db

        // Same profile, same log: the replay must ADOPT the finalized session
        // (same server/character/start), not mint a duplicate row.
        app.Launch();
        app.CloseGracefully();

        using var repo = new SessionRepository(app.HistoryDbPath);
        var row = Assert.Single(repo.Query(character: AppHarness.Character));
        Assert.Equal(AppHarness.Server, row.Server);
        Assert.Equal(kills + 1, row.Kills);
        Assert.Equal("ApplicationExit", row.EndReason);
    }
}
