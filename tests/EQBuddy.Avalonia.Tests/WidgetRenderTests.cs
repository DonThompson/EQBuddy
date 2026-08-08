using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Avalonia.Tests;

/// <summary>
/// Renders the Linux widget without a display server.
///
/// These exist because of a specific failure of process: for a week, changes shipped to the
/// Avalonia UI — themes, a delay box, a guide panel, per-fight sections — verified only by
/// "it compiles and the unit tests pass". Neither of those would have caught a window that
/// draws nothing, a theme that leaves everything the same colour, or a card that throws when
/// it populates. A frame captured here is the cheapest thing that would.
///
/// The widget is deliberately built as the app builds it, so a break in construction shows
/// up here rather than on a user's desktop.
/// </summary>
[Collection("avalonia")]
public class WidgetRenderTests : IDisposable
{
    private readonly string _profile =
        Directory.CreateTempSubdirectory("eqbuddy-render-").FullName;

    public WidgetRenderTests()
    {
        // Isolate settings/history: constructing the widget opens a SQLite history db and
        // reads settings, and a test must not touch the real profile.
        Environment.SetEnvironmentVariable("EQBUDDY_APPDATA", _profile);
        Environment.SetEnvironmentVariable("EQBUDDY_EXPAND", "1");
        Directory.CreateDirectory(Path.Combine(_profile, "logs"));
        File.WriteAllText(Path.Combine(_profile, "settings.json"),
            $$"""
              { "LogFolder": {{System.Text.Json.JsonSerializer.Serialize(Path.Combine(_profile, "logs"))}},
                "TruncateLogs": false, "ShowTutorial": false, "TrackSpawns": false,
                "LastSeenVersion": {{System.Text.Json.JsonSerializer.Serialize(UpdateChecker.CurrentVersion.ToString())}},
                "Theme": "ParchmentBrass" }
              """);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("EQBUDDY_APPDATA", null);
        Environment.SetEnvironmentVariable("EQBUDDY_EXPAND", null);
        try { Directory.Delete(_profile, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>The widget builds, shows, and paints something. A window that constructs but
    /// draws nothing is the failure mode "it compiles" can't see.</summary>
    [AvaloniaFact]
    public void TheWidgetRendersAFrame()
    {
        var window = new MainWindow();
        window.Show();

        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.True(frame!.Size.Width > 100, $"window rendered only {frame.Size.Width}px wide");
        Assert.True(frame.Size.Height > 100, $"window rendered only {frame.Size.Height}px tall");
        window.Close();
    }

    /// <summary>The cards are actually in the visual tree, not just fields on the class.</summary>
    [AvaloniaFact]
    public void TheCardsArePresent()
    {
        var window = new MainWindow();
        window.Show();

        var headings = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "").ToList();

        Assert.Contains(headings, h => h.Contains("Combat"));
        Assert.Contains(headings, h => h.Contains("Healing"));
        Assert.Contains(headings, h => h.Contains("Kills"));
        window.Close();
    }

    [AvaloniaFact]
    public void WhatsNewPopupRendersSkippedReleasesAndHighlights()
    {
        var entries = WhatsNewCatalog.EntriesBetween("1.23.1", "1.25.0");
        var window = new WhatsNewWindow(entries);
        window.Show();

        var text = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "").ToList();
        Assert.Contains("What's new since your last version", text);
        Assert.Contains("EQBuddy 1.25.0", text);
        Assert.Contains("EQBuddy 1.24.0", text);
        Assert.Contains(text, t => t.StartsWith("This popup!"));

        window.Close();
    }

    [Fact]
    public void PreFeatureBaselineSelectsOnlyTheCurrentMinorRelease()
    {
        Assert.Equal("1.24.0", MainWindow.PreviousVersionBaseline("1.25.0.0"));
    }

    [AvaloniaFact]
    public void SpawnTrackerRendersTheCatalogAndControls()
    {
        var main = new MainWindow();
        main.Show();
        var catalog = SpawnCatalog.LoadEmbedded();
        var overrides = SpawnOverrides.Load(Path.Combine(_profile, "spawn-test-overrides.json"));
        var timers = new SpawnTimers(catalog, overrides, Path.Combine(_profile, "spawn-test-timers.json"));
        var tracker = new SpawnsWindow(main, new SpawnsViewModel(catalog, overrides, timers));
        tracker.Show(main);

        Assert.NotNull(tracker.CaptureRenderedFrame());
        var text = tracker.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "").ToList();
        Assert.Contains(text, value => value.Contains("Spawns"));
        Assert.Contains(text, value => value.Contains("countdown starts from the log"));
        Assert.Contains(tracker.GetVisualDescendants().OfType<CheckBox>(),
            check => Equals(check.Content, "Follow"));
        Assert.Contains(tracker.GetVisualDescendants().OfType<ComboBox>(),
            combo => combo.Items.Contains("Custom…") && combo.Items.Contains("Chimes"));

        tracker.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void SpawnTrackerCanHideWithoutDisarmingTrackingAndOpensOnRequestedZone()
    {
        var main = new MainWindow();
        main.Settings.TrackSpawns = true;
        main.Show();
        var catalog = SpawnCatalog.LoadEmbedded();
        var overrides = SpawnOverrides.Load(Path.Combine(_profile, "spawn-lifecycle-overrides.json"));
        var timers = new SpawnTimers(catalog, overrides, Path.Combine(_profile, "spawn-lifecycle-timers.json"));
        var tracker = new SpawnsWindow(main,
            new SpawnsViewModel(catalog, overrides, timers), "Befallen");
        tracker.Show(main);

        Assert.Contains(tracker.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == "🕒 Spawns - Befallen");
        tracker.Close();
        Assert.True(main.Settings.TrackSpawns);

        main.Close();
    }

    [AvaloniaFact]
    public void SpawnCountdownsRenderAsCompactChips()
    {
        var main = new MainWindow();
        main.Show();
        var catalog = SpawnCatalog.LoadEmbedded();
        var overrides = SpawnOverrides.Load(Path.Combine(_profile, "spawn-chip-overrides.json"));
        var timers = new SpawnTimers(catalog, overrides, Path.Combine(_profile, "spawn-chip-timers.json"));
        timers.StartManual("Befallen", "Asaka L`Rei", 210);
        var chips = new SpawnChipsWindow(main, new SpawnsViewModel(catalog, overrides, timers));
        chips.RefreshChips(DateTime.Now);
        chips.Show(main);

        Assert.NotNull(chips.CaptureRenderedFrame());
        var text = chips.GetVisualDescendants().OfType<TextBlock>()
            .Select(block => block.Text ?? "").ToList();
        Assert.Contains("⏳ Asaka L`Rei", text);
        Assert.Contains(text, value => value.StartsWith("3:"));

        chips.Position = new global::Avalonia.PixelPoint(321, 222);
        chips.Close();
        Assert.Equal(321, main.Settings.SpawnChipsLeft);
        Assert.Equal(222, main.Settings.SpawnChipsTop);
        main.Close();
    }

    [AvaloniaFact]
    public void MezTargetsRenderInTheirOwnMovableChipStack()
    {
        var settings = AppSettings.Load();
        var now = new DateTime(2026, 8, 8, 15, 0, 0);
        var mezzes = new[]
        {
            new MezState("an orc centurion", "Mesmerize", "You", now.AddSeconds(-10), now.AddSeconds(20)),
            new MezState("an orc oracle", "Entrance", "Aenari", now.AddSeconds(-5), null),
        };
        var chips = new MezChipsWindow(settings);
        chips.RefreshChips(mezzes, now);
        chips.Show();

        Assert.NotNull(chips.CaptureRenderedFrame());
        var text = chips.GetVisualDescendants().OfType<TextBlock>()
            .Select(block => block.Text ?? "").ToList();
        Assert.Contains("💤 an orc centurion", text);
        Assert.Contains("0:20", text);
        Assert.Contains("💤 an orc oracle", text);
        Assert.Contains("?", text);

        chips.Position = new global::Avalonia.PixelPoint(432, 234);
        chips.Close();
        Assert.Equal(432, settings.MezChipsLeft);
        Assert.Equal(234, settings.MezChipsTop);
    }

    /// <summary>Applying a snapshot is where a card that mis-formats or dereferences null
    /// blows up — and it's the path every refresh takes.</summary>
    [AvaloniaFact]
    public void ApplyingStatsDoesNotThrow()
    {
        var window = new MainWindow();
        window.Show();

        var stats = new SessionStats { CharacterName = "Testchar" };
        foreach (var line in (string[])
                 [
                     "[Sat Jul 18 15:00:00 2026] You slash orc pawn for 12 points of damage.",
                     "[Sat Jul 18 15:00:02 2026] Orc pawn hits YOU for 4 points of damage.",
                     "[Sat Jul 18 15:00:03 2026] You healed Testchar for 20 hit points by Light Healing.",
                     "[Sat Jul 18 15:00:04 2026] You have slain orc pawn!",
                     "[Sat Jul 18 15:00:05 2026] --You have looted a Mote of Minor Potential from orc pawn's corpse.--",
                 ])
            if (LogParser.Parse(line) is { } evt) stats.Apply(evt);

        var snapshot = stats.Snapshot(null, null);

        // The exception, if any, is the point — this is the call every refresh makes.
        window.RenderSnapshotForTest(snapshot);

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        window.Close();
    }

    [AvaloniaFact]
    public void CombatCardShowsSpellDamageAndCastCompletion()
    {
        var window = new MainWindow();
        window.Show();

        window.RenderSnapshotForTest(new StatsSnapshot
        {
            DotDamage = 1_250,
            DirectSpellDamage = 875,
            CastsStarted = 10,
            CastsInterrupted = 1,
            Fizzles = 2,
            Resists = 3,
        });

        var summary = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text?.StartsWith("Dealt ") == true).Text!;
        Assert.Contains("Your spells: 1,250 over time / 875 direct", summary);
        Assert.Contains("Casts 10 · 70% completed (1 interrupted · 2 fizzled · 3 resisted)", summary);

        window.Close();
    }

    [AvaloniaFact]
    public void AreaSpellsAppearOnlyWhenTheSnapshotContainsThem()
    {
        var window = new MainWindow();
        window.Show();

        window.RenderSnapshotForTest(new StatsSnapshot());
        var heading = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == "Area spells (per cast)");
        Assert.False(heading.IsVisible);

        window.RenderSnapshotForTest(new StatsSnapshot
        {
            AreaSpells =
            [
                new AreaSpellInfo("Rain of Fire", 3, 2.5, 4, 3600, 1200),
                new AreaSpellInfo("Circle of Flame", 2, 3, 3, 1000, 500),
            ],
        });

        Assert.True(heading.IsVisible);
        var text = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "").ToList();
        Assert.Contains("Rain of Fire", text);
        Assert.Contains("1,200/cast - x3 - 2.5 targets (best 4)", text);
        Assert.Contains("500/cast - x2 - 3 targets", text);

        window.Close();
    }

    [AvaloniaFact]
    public void PendingCueCountsDownInTheTrackedCardHeading()
    {
        var window = new MainWindow();
        window.Show();
        var rule = new TrackedRule { Name = "Respawn", Pattern = "placeholder" };
        window.Settings.TrackedRules.Add(rule);
        var dueAt = DateTime.Now.AddMinutes(8);

        window.RenderSnapshotForTest(new StatsSnapshot
        {
            Tracked =
            [
                new TrackedRuleResult(rule.Name, 1, [], 1, 1,
                    DateTime.Now, DateTime.Now, "placeholder", rule.Id),
            ],
        }, new Dictionary<string, DateTime> { [rule.Id] = dueAt });

        var heading = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text?.StartsWith("RESPAWN ⏳") == true);
        Assert.Matches(@"RESPAWN ⏳ (7:5\d|8:00)", heading.Text!);
        Assert.Same(AppTheme.WarnBrush, heading.Foreground);

        window.Close();
    }

    [AvaloniaFact]
    public void WatchCardLeadsWithLastMatchAndCollapsesMultipleKinds()
    {
        var window = new MainWindow();
        window.Show();
        var rule = new TrackedRule { Name = "Buff fades", Pattern = "placeholder" };
        window.Settings.TrackedRules.Add(rule);

        window.RenderSnapshotForTest(new StatsSnapshot
        {
            Tracked =
            [
                new TrackedRuleResult(rule.Name, 3,
                    [new NameCount("Haste", 2), new NameCount("Echoing Light", 1)],
                    3, 3, DateTime.Now, DateTime.Now.AddSeconds(-5), "Haste", rule.Id),
            ],
        });

        var text = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "").ToList();
        Assert.Contains(text, t => t.StartsWith("last: Haste · ") && t.EndsWith(" ago"));
        Assert.Contains("▸ all 2 kinds", text);
        Assert.DoesNotContain("Haste   x2", text);

        window.Close();
    }

    [AvaloniaFact]
    public void PetAbilitiesDefaultCollapsedAndExpandFromTheSavedSetting()
    {
        var window = new MainWindow();
        window.Show();
        var snapshot = new StatsSnapshot
        {
            PetAbilities = [new SourceDamage("Slash", 2, 30)],
        };

        window.Settings.ShowPetAbilities = false;
        window.RenderSnapshotForTest(snapshot);
        var text = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "").ToList();
        Assert.Contains("▸ Pet abilities (1)", text);
        Assert.DoesNotContain("Slash", text);

        window.Settings.ShowPetAbilities = true;
        window.RenderSnapshotForTest(snapshot);
        text = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "").ToList();
        Assert.Contains("▾ Pet abilities", text);
        Assert.Contains("Slash", text);

        window.Close();
    }

    /// <summary>A theme swap has to change what's on screen. Mutating brushes in place is
    /// clever but invisible to a compiler: this is the check that it actually repaints.</summary>
    [AvaloniaFact]
    public void SwitchingThemeChangesTheColours()
    {
        // Set the starting theme rather than reading whatever the last test left: AppTheme's
        // brushes are process-wide singletons, so an ambient starting point makes this test
        // depend on execution order.
        AppTheme.Apply("ParchmentBrass");
        var parchment = AppTheme.BgBrush.Color;
        AppTheme.Apply("Solarized");
        var light = AppTheme.BgBrush.Color;
        AppTheme.Apply("SolarizedDark");
        var dark = AppTheme.BgBrush.Color;

        Assert.NotEqual(parchment, light);
        Assert.NotEqual(light, dark);

        AppTheme.Apply("ParchmentBrass");
        Assert.Equal(parchment, AppTheme.BgBrush.Color);   // and back again
    }

    /// <summary>Every catalogued theme has to produce a paintable palette on this platform
    /// too — a hex the shared table accepts but Avalonia's parser rejects would only show up
    /// at runtime, on Linux, for whoever picked that theme.</summary>
    [AvaloniaFact]
    public void EveryThemeApplies()
    {
        foreach (var (key, _) in ThemeCatalog.Themes)
        {
            AppTheme.Apply(key);
            Assert.NotEqual(default, AppTheme.BgBrush.Color);
            Assert.NotEqual(default, AppTheme.TextBrush.Color);
        }
        AppTheme.Apply("ParchmentBrass");
    }
}
