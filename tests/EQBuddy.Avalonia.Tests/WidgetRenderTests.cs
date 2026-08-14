using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
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

    /// <summary>The Epics card exists at all — until it did, the "epic" key sat in the
    /// shared OverlaySections catalog with nothing here to build it, which is what
    /// crashed startup, and then (once guarded) left a dead row in Options.</summary>
    [AvaloniaFact]
    public void TheEpicsCardRendersItsClassTabsAndClassicLens()
    {
        var window = new MainWindow();
        window.Show();

        window.RenderSnapshotForTest(new StatsSnapshot());
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var text = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "").ToList();
        Assert.Contains("⚔ Epics", text);
        Assert.Contains(text, t => t.StartsWith("BRD "));   // a class tab with its score
        Assert.Contains(window.GetLogicalDescendants().OfType<CheckBox>(),
            c => (c.Content as string) == "Classic-doable only");
        window.Close();
    }

    /// <summary>The classic lens hides non-classic steps from the LIST and the COUNTS
    /// alike (#71d21ea) — a score that counted steps it wasn't showing would be the
    /// dishonest half of the feature.</summary>
    [AvaloniaFact]
    public void TheClassicLensMovesTheEpicsScoreNotJustTheList()
    {
        var window = new MainWindow();
        window.Show();
        window.RenderSnapshotForTest(new StatsSnapshot());
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var lens = window.GetLogicalDescendants().OfType<CheckBox>()
            .First(c => (c.Content as string) == "Classic-doable only");
        var header = window.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text is { } s && s.Contains('/') && s.EndsWith(EpicTotal(window).ToString()));

        var before = header.Text;
        lens.IsChecked = true;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Classic-only is a strict subset, so the denominator can only shrink or hold.
        Assert.NotNull(header.Text);
        Assert.True(Denominator(header.Text!) <= Denominator(before!),
            $"classic lens grew the total: {before} → {header.Text}");
        window.Close();
    }

    private static int Denominator(string headerText) =>
        int.Parse(headerText.Split('/')[1]);

    private static int EpicTotal(MainWindow window) =>
        window.Settings.EpicQuestChecklist.Count;

    /// <summary>The Gear card's WHERE-TO-GO pivot (#122abd6) reached this UI: the
    /// toggle has to exist in the tree, or the by-zone view is unreachable here even
    /// though the rollup it draws is shared and tested.</summary>
    [AvaloniaFact]
    public void TheGearCardOffersTheByZonePivot()
    {
        var window = new MainWindow();
        window.Show();

        var checks = window.GetLogicalDescendants().OfType<CheckBox>()
            .Select(c => c.Content as string ?? "").ToList();

        Assert.Contains(checks, c => c.Contains("Group by farm zone"));
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

        var active = Assert.Single(new SpawnsViewModel(catalog, overrides, timers).Chips(DateTime.Now));
        chips.DismissChip(active);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(chips.GetVisualDescendants().OfType<TextBlock>(),
            block => block.Text == "⏳ Asaka L`Rei");

        // The drag flag, not a coordinate delta, is the persistence signal (#117).
        chips.MarkUserMovedForTests();
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
            new MezState("an orc centurion", "Mesmerize", "You", now.AddSeconds(-8), now.AddSeconds(22)),
            new MezState("an orc oracle", "Entrance", "Aenari", now.AddSeconds(-5), null),
        };
        // The clock-source ctor is the only shape left (WPF parity): the stack asks
        // its source at refresh time; BuildChips remains the shared mez builder.
        var chips = new MezChipsWindow(settings, at => MezChipsWindow.BuildChips(mezzes, at));
        chips.RefreshChips(now);
        chips.Show();

        Assert.NotNull(chips.CaptureRenderedFrame());
        var text = chips.GetVisualDescendants().OfType<TextBlock>()
            .Select(block => block.Text ?? "").ToList();
        Assert.Contains("💤 an orc centurion (1)", text);
        Assert.Contains("💤 an orc centurion (2)", text);
        Assert.Contains("0:20", text);
        Assert.Contains("💤 an orc oracle", text);
        Assert.Contains("?", text);

        // A USER drag persists; a merely programmatic Position write must not
        // (#117 round two: the grow-up anchor moves the window itself, so the drag
        // flag — not a coordinate delta — is the persistence signal).
        chips.MarkUserMovedForTests();
        chips.Position = new global::Avalonia.PixelPoint(432, 234);
        chips.Close();
        Assert.Equal(432, settings.MezChipsLeft);
        Assert.Equal(234, settings.MezChipsTop);
    }

    [AvaloniaFact]
    public void ItemInfoPopupRendersWikiSectionsAndSourceState()
    {
        var service = new EqlWikiItemService(Path.Combine(_profile, "item-cache"),
            _ => Task.FromResult<string?>(null));
        var window = new ItemInfoWindow(service, new AppSettings());
        window.Render(new ItemLookupResult(new ItemInfo
        {
            Name = "Cloak of Flames",
            StatsLines = ["MAGIC ITEM", "Slot: BACK", "AC: 10"],
            MerchantValue = "5g",
            DropsFrom = [("Nagafen's Lair", ["Lord Nagafen"])],
            Quests = ["A Fiery Favor"],
            WikiUrl = "https://eqlwiki.com/Cloak_of_Flames",
        }, ItemLookupState.Cached, new DateTime(2026, 8, 5)));
        window.Show();

        Assert.NotNull(window.CaptureRenderedFrame());
        var text = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(block => block.Text ?? "").ToList();
        Assert.Contains("Cloak of Flames", text);
        Assert.Contains("CACHED 8/5", text);
        Assert.Contains("MAGIC ITEM", text);
        Assert.Contains("Lord Nagafen — Nagafen's Lair", text);
        Assert.Contains("A Fiery Favor", text);
        Assert.Contains("Open wiki page ↗", text);

        window.Close();
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

    /// <summary>AA display since the 2026-08-11 rethink: session-new AAs lead, the full
    /// character ledger folds behind the ▸ label (Pet-abilities idiom, WPF parity).</summary>
    [AvaloniaFact]
    public void ProgressCardFoldsTheAaLedgerBehindAToggle()
    {
        var window = new MainWindow();
        window.Show();
        var snapshot = new StatsSnapshot
        {
            SessionStart = new DateTime(2026, 8, 8),
            AaAbilities =
            [
                new AaAbilityInfo("Spell Casting Mastery", 3, new DateTime(2026, 8, 8, 1, 0, 0)),
                new AaAbilityInfo("Natural Durability", 1, new DateTime(2026, 8, 7)),
            ],
        };

        window.Settings.ShowAllAAs = false;
        window.RenderSnapshotForTest(snapshot);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var text = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "").ToList();
        // Learned this session: leads unfolded; the pre-session AA stays folded away.
        Assert.Contains("AA learned this session", text);
        Assert.Contains("Spell Casting Mastery", text);
        Assert.Contains("rank 3", text);
        Assert.Contains("▸ All AA abilities (2)", text);
        Assert.DoesNotContain("Natural Durability", text);

        window.Settings.ShowAllAAs = true;
        window.RenderSnapshotForTest(snapshot);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        text = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "").ToList();
        Assert.Contains("▾ All AA abilities", text);
        Assert.Contains("Natural Durability", text);
        window.Close();
    }

    [AvaloniaFact]
    public void DamageBreakoutRendersFightAbilityBars()
    {
        var settings = AppSettings.Load();
        var window = new BreakoutWindow(settings, BreakoutKind.Damage);
        window.Update(new StatsSnapshot
        {
            LastFight = new LastFightInfo("a froglok", 10, 150, 8, 0, 15, 0,
                "slain", false,
                [new SourceDamage("Backstab", 2, 100), new SourceDamage("Slash", 5, 50)],
                [], []),
        });
        window.Show();

        Assert.NotNull(window.CaptureRenderedFrame());
        var text = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "").ToList();
        Assert.Contains("⚔ Your damage", text);
        Assert.Contains("Backstab", text);
        // BreakdownRows layout: "100" is the semibold headline, the columns read dim beside it.
        Assert.Contains("100", text);
        Assert.Contains("×2 · avg 50 · 10 dps", text);
        window.Close();
    }

    [AvaloniaFact]
    public void FeedbackWindowExplainsThatGitHubReviewsTheDraft()
    {
        var window = new FeedbackWindow();
        window.Show();

        Assert.NotNull(window.CaptureRenderedFrame());
        var text = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "").ToList();
        Assert.Contains("💡 Feature request", text);
        Assert.Contains("🐛 Bug report", text);
        Assert.Contains(text, t => t.Contains("nothing is sent from the app"));
        window.Close();
    }

    /// <summary>The KPI strip (2026-08-11 modernization): the headline numbers are
    /// always painted, before any card opens.</summary>
    [AvaloniaFact]
    public void KpiStripShowsTheHeadlineNumbers()
    {
        var window = new MainWindow();
        window.Show();

        window.RenderSnapshotForTest(new StatsSnapshot
        {
            CurrentDps = 42, YourKillCount = 7, LootTotal = 3, XpPerHour = 12.5,
        });

        var text = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "").ToList();
        Assert.Contains("XP/HR", text);   // the strip's captions (SectionLabel uppercases)
        Assert.Contains("42", text);      // current DPS leads while fighting
        Assert.Contains("7", text);
        Assert.Contains("12.5%", text);
        window.Close();
    }

    /// <summary>The Sky Quest card: class tabs from the embedded checklist, the state
    /// lens vocabulary, and live checkboxes on the selected tab.</summary>
    [AvaloniaFact]
    public void SkyQuestCardRendersClassTabsWithChecklists()
    {
        var window = new MainWindow();
        window.Show();

        window.RenderSnapshotForTest(new StatsSnapshot());
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var text = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "").ToList();
        Assert.Contains("☁ Sky Quest", text);
        // The tab header is now the D/R/P state count (#3d7911d), not "collected/total":
        // the class abbreviation leads, then the three metric labels beside it.
        Assert.Contains(text, t => t.StartsWith("BRD"));
        Assert.Contains("D", text);
        Assert.Contains("R", text);
        Assert.Contains("P", text);
        Assert.Contains(window.GetVisualDescendants().OfType<ComboBox>(),
            combo => combo.Items.Contains("ready") && combo.Items.Contains("open"));
        Assert.Contains(window.GetVisualDescendants().OfType<CheckBox>(),
            check => check.IsEnabled);   // the selected tab's item boxes are live
        window.Close();
    }

    /// <summary>Buffs and Raids cards stay where Options put them, with honest empty
    /// states instead of vanishing (David's 1.66.2 verdict).</summary>
    [AvaloniaFact]
    public void BuffsAndRaidsCardsShowHonestEmptyStates()
    {
        var window = new MainWindow();
        window.Show();

        window.RenderSnapshotForTest(new StatsSnapshot());

        var text = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "").ToList();
        Assert.Contains("⏳ Buffs", text);
        Assert.Contains(text, t => t.StartsWith("Nothing running"));
        Assert.Contains("🐉 Raids", text);
        Assert.Contains(text, t => t.StartsWith("Nothing defeated yet"));
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

    /// <summary>The toggleAll hotkey's restore loop must skip a window that closed while
    /// hidden (a chip stack whose timers ran out, a tracker torn down by its owner) —
    /// Avalonia throws "Cannot re-show a closed window" where WPF just checked IsLoaded.</summary>
    [AvaloniaFact]
    public void HotkeyRestoreSurvivesAWindowClosedWhileHidden()
    {
        var main = new MainWindow();
        main.Show();
        var satellite = new Window { Width = 120, Height = 60 };
        satellite.Show();
        // Headless has no desktop lifetime, so the capture list comes from the seam.
        main.WindowEnumeratorForTests = () => [main, satellite];

        main.HandleHotkeyAction("toggleAll");
        Assert.False(satellite.IsVisible);   // proves the hide pass actually captured it
        satellite.Close();                   // closes while hidden

        main.HandleHotkeyAction("toggleAll");   // restore must not throw on the corpse
        Assert.True(main.IsVisible);
        main.Close();
    }
}
