using System.IO;
using System.Text.Json;

namespace EQBuddy.Core;

public sealed class AppSettings
{
    public string? LogFolder { get; set; }
    /// <summary>Folder holding EQBuddySetup.exe for updates; null = auto-detect OneDrive.</summary>
    public string? UpdateFolder { get; set; }
    public bool Minimized { get; set; }
    public List<string> MiniStats { get; set; } = ["kills", "dps"];
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double Opacity { get; set; } = 0.96;
    public double UiScale { get; set; } = 1.0;
    /// <summary>Scale for the small floating windows — spawn/mez chips and the alert
    /// banner — independent of UiScale so 4K players can grow just those (discussion #47).</summary>
    public double ChipScale { get; set; } = 1.0;
    public double QuestsLeft { get; set; } = double.NaN;
    public double QuestsTop { get; set; } = double.NaN;
    /// <summary>Quest Tracker era ceiling ("" = any): quests after this era are hidden
    /// (discussion #62). Persisted app-wide — the world's era isn't per character.</summary>
    public string QuestEraFilter { get; set; } = "";
    /// <summary>Per-window Ctrl+wheel zoom factors, keyed by window kind ("drops",
    /// "breakout:Damage", …) — the universal text-scaling answer (discussion #59;
    /// David: "a more permanent scaling solution").</summary>
    public Dictionary<string, double> WindowZooms { get; set; } = new();
    /// <summary>Opacity of the widget's background panel only — text stays fully opaque.</summary>
    public double BackgroundOpacity { get; set; } = 0.95;
    /// <summary>Re-lift EQBuddy's windows above later-created topmost overlays every few
    /// seconds (#91: Lossless Scaling's upscale surface buried the widget). Off = the old
    /// behavior, for screen-capture setups where the re-lift makes a visible double.</summary>
    public bool KeepAboveOverlays { get; set; } = true;
    /// <summary>Global hotkeys, opt-in only (#100): action key → gesture text
    /// ("Ctrl+Alt+M"). EMPTY BY DEFAULT and stays that way unless the player binds
    /// keys in Options — the 1.12–1.34 era's default binds ate other apps' shortcuts
    /// and the feature was removed; it returns only in this bind-it-yourself form.</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new();

    /// <summary>The mez chip stack, off-switchable (Reddit ask, 2026-08-11): a class
    /// that never mezzes never wants the window popping mid-fight.</summary>
    public bool MezChipsEnabled { get; set; } = true;

    /// <summary>The slow alert (#94): a chip + optional voice when an attack-speed
    /// debuff lands on you — a silent 40% slow quietly doubles a fight.</summary>
    public bool SlowAlertEnabled { get; set; } = true;
    /// <summary>Speak the slow when it lands ("Slowed 40 percent") — the chip alone
    /// is easy to miss in exactly the busy fights slows matter most in.</summary>
    public bool SlowAlertSpoken { get; set; } = true;
    /// <summary>Alert only while raiding (#94's toggle) — detected from raid-channel
    /// chat, the log's only raid signal. Off = alert everywhere.</summary>
    public bool SlowAlertRaidOnly { get; set; }

    /// <summary>How the Tracked card orders its rules (#105, wizen): "manual" (the
    /// Options list order, rearrangeable there), "alpha", "total", or "recent".</summary>
    public string WatchSortMode { get; set; } = "manual";

    /// <summary>The recent-lines rule picker's chat filter (David's field note: General
    /// chat drowns the combat lines). Off by default — a "WTS" watch is a real rule.</summary>
    public bool RecentLinesHideChat { get; set; }

    /// <summary>Buff card display (David): false = every running buff with its full
    /// countdown; true = quiet until a buff is within <see cref="BuffWarnSeconds"/> of
    /// fading — the "tell me when it matters" mode.</summary>
    public bool BuffTimersExpiringOnly { get; set; }
    public double BuffWarnSeconds { get; set; } = 60;

    /// <summary>Buff sets (#120, Frankthetankk): the buffs a character never wants to
    /// camp without, keyed per character by the same "name_server" key the AA ledger
    /// uses. Player-built only — never auto-populated — and evaluated by
    /// BuffSetEvaluator into the Buffs card's missing line. Names stored as picked;
    /// rank suffixes fold at match time, so "Temperance" covers "Temperance II".</summary>
    public Dictionary<string, List<string>> BuffSets { get; set; } = new();

    /// <summary>The Options tab last used — iterating on watch rules shouldn't cost a
    /// click per visit. "look" / "alerts" / "watch" / "cards" / "behavior".</summary>
    public string OptionsTab { get; set; } = "look";

    /// <summary>#112 (Frankthetankk): show EQBuddy's own CPU/memory in the title bar.
    /// Off by default — diagnostic info, not furniture.</summary>
    public bool ShowPerfStats { get; set; }

    /// <summary>Fight-timeline window placement; 0 width = never opened, defaults apply.</summary>
    public double TimelineLeft { get; set; }
    public double TimelineTop { get; set; }
    public double TimelineWidth { get; set; }
    public double TimelineHeight { get; set; }
    /// <summary>The Progress card's full AA ledger, folded by default (same Reddit
    /// report): session-new AAs show always; the complete list is a click away.</summary>
    public bool ShowAllAAs { get; set; }

    /// <summary>The Progress card's next-milestone AA preview, folded by default: the
    /// label always names the level and count; the rows are a click away.</summary>
    public bool ShowNextUnlocks { get; set; }

    /// <summary>Chip-stack growth direction (#95): anchored at the bottom edge, new
    /// chips push the stack upward — so boss timers can sit above mez timers with
    /// each growing away from the other.</summary>
    public bool SpawnChipsGrowUp { get; set; }
    public bool MezChipsGrowUp { get; set; }
    /// <summary>Section-list height chosen by dragging the widget's bottom edge, in
    /// pre-scale units so it survives UiScale changes (Reddit ask, 2026-08-09: grow the
    /// window without growing the text). NaN = automatic, fit the monitor.</summary>
    public double ContentHeight { get; set; } = double.NaN;
    /// <summary>Empty finished-session logs automatically. Off = logs grow forever
    /// (for players who upload their logs elsewhere).</summary>
    public bool TruncateLogs { get; set; } = true;
    /// <summary>Copy a log's content to Logs\archive\eqlog_name_server_STAMP.txt before
    /// the janitor empties it — for players who want the raw history kept (discussion
    /// #52, joeymavity). Off by default: most players run EQBuddy precisely so logs
    /// stop accumulating.</summary>
    public bool ArchiveLogs { get; set; }
    /// <summary>User-defined tracked-loot rules (TRACK-018: persisted).</summary>
    public List<TrackedRule> TrackedRules { get; set; } = [];
    /// <summary>Highest version of the built-in default watch rules already applied.
    /// Bumping <see cref="CurrentDefaultRulesVersion"/> hands new defaults to existing
    /// installs exactly once, and never re-adds a rule the user deleted on purpose.</summary>
    public int DefaultRulesVersion { get; set; }
    /// <summary>Options window width, dragged by its right edge. Wide enough by default
    /// that the watch-rule row (kind + name + spell class + match text + toggles) fits
    /// without clipping.</summary>
    public double OptionsWidth { get; set; } = 420;
    /// <summary>Default rolling window for "recent" rates, in minutes (5/15/30).</summary>
    public int RecentWindowMinutes { get; set; } = 15;
    /// <summary>Alert sound: a built-in name (Ding, Notify, Chimes, Chord, Tada,
    /// Exclamation, Alarm) or the full path of a custom .wav/.mp3 file.</summary>
    public string AlertSound { get; set; } = "Ding";
    /// <summary>Alert playback volume, 0..1. Defaults to FULL — WPF's MediaPlayer
    /// default is 0.5 and nothing ever set it, so alerts played at half loudness
    /// for everyone (Reddit report: "very quiet, needs a booster").</summary>
    public double AlertVolume { get; set; } = 1.0;
    /// <summary>Spoken-alert voice: an installed SAPI voice's description ("Microsoft Zira
    /// Desktop"), or "" for the system default — the only behavior before the picker
    /// existed. A voice that's gone missing (settings copied between machines) falls back
    /// to the default at speak time rather than silencing alerts. Windows-only effect;
    /// macOS `say` and the Linux no-op ignore it.</summary>
    public string SpeechVoice { get; set; } = "";
    /// <summary>Spoken-alert rate in SAPI units. SAPI accepts -10..10 but the app clamps
    /// to ±5 (UI.Shared SpokenAlerts.MinRate/MaxRate — past that speech stops being
    /// speech); 0 = the voice's normal pace, the pre-slider behavior.</summary>
    public int SpeechRate { get; set; }
    /// <summary>Spoken-alert volume 0..100, SAPI's own scale. Separate from
    /// <see cref="AlertVolume"/> on purpose: that slider drives only the MediaPlayer that
    /// plays sound files — SAPI never saw it, so one slider claiming both would be a lie
    /// in whichever direction it didn't reach.</summary>
    public int SpeechVolume { get; set; } = 100;
    /// <summary>Position of the floating alert tile; NaN = above the widget.</summary>
    public double AlertLeft { get; set; } = double.NaN;
    public double AlertTop { get; set; } = double.NaN;
    /// <summary>Master switch for watch chips in the mini dashboard. Which rules appear is
    /// then per-rule (<see cref="TrackedRule.Pinned"/>): showing every enabled rule was
    /// all-or-nothing, and a mini bar with eight chips on it isn't a mini bar.</summary>
    public bool PinWatchChips { get; set; }
    /// <summary>Whether the one-time "pin everything you were already seeing" pass has run.
    /// A flag rather than inferring it from "nothing is pinned", so deliberately unpinning
    /// every rule isn't undone at the next launch.</summary>
    public bool WatchPinsMigrated { get; set; }
    /// <summary>Whether the watch-rule examples panel in Options is expanded. Remembered so
    /// someone still learning the feature doesn't have to reopen it every time, and someone
    /// who doesn't need it never sees it again.</summary>
    public bool ShowWatchGuide { get; set; }
    /// <summary>Which of the Combat/Healing subsections are expanded. Separate per card and
    /// per section, because the reason to collapse one isn't the reason to collapse another:
    /// a melee player may want the fight breakdown open and the session one shut, and a
    /// healer the reverse. Default open — a new subsection nobody can see is a wasted one.</summary>
    public bool ShowCombatFight { get; set; } = true;
    public bool ShowCombatSession { get; set; } = true;
    /// <summary>Pet abilities breakdown expanded on the Combat card. Default collapsed
    /// (discussion #28): the pet's overall damage is already a row in the main list,
    /// and a pet class fighting all session got a wall of ability rows for free.</summary>
    public bool ShowPetAbilities { get; set; }
    public bool ShowHealFight { get; set; } = true;
    public bool ShowHealSession { get; set; } = true;
    /// <summary>Show the quick tour at every launch. Turned off by the tutorial's
    /// "Never show again" button or the Options checkbox. While on, the startup
    /// janitor defers log truncation — the tour's first page is its consent question.</summary>
    public bool ShowTutorial { get; set; } = true;
    /// <summary>Overlay card order (section keys); missing keys append in default order.</summary>
    public List<string> SectionOrder { get; set; } = [];
    /// <summary>Hidden overlay cards (still collect data — OVERLAY acceptance).</summary>
    public List<string> HiddenSections { get; set; } = [];
    // Global hotkeys were REMOVED 2026-08-06 (Reddit report: RegisterHotKey is
    // system-wide, so EQBuddy ate Ctrl+Shift+T — reopen browser tab — from every app on
    // the machine). Old settings.json files still carrying Hotkey* keys deserialize fine;
    // unknown properties are ignored and dropped on the next save.
    /// <summary>Persistent Plane of Sky quest turn-in checklist shown in the overlay.</summary>
    public List<SkyQuestChecklistItem> SkyQuestChecklist { get; set; } = [];
    /// <summary>The class tab last selected in the Sky Quest card. Quest item names
    /// repeat across classes (five classes each need a Wind Rune Azia), so loot
    /// auto-check only ticks boxes for this class; empty = no tab picked yet, first
    /// unacquired match wins.</summary>
    public string SkyQuestClass { get; set; } = "";
    /// <summary>Sky quest rewards marked turned-in, as "ClassName|Reward" keys
    /// (discussion #73, chrstahl). Manual only: the log shows nothing reliable when
    /// items change hands at an NPC, so the player is the source of truth — including
    /// for quests finished before this feature existed. Marking one complete also
    /// checks its items (they were acquired and then handed over).</summary>
    public List<string> SkyQuestCompleted { get; set; } = [];
    /// <summary>Imported equipment shopping list from EQ Legends Tools, shown as a
    /// lightweight in-game checklist. Manual checkboxes: imports replace the list,
    /// toggles persist until the next import or clear.</summary>
    public List<GearChecklistItem> GearChecklist { get; set; } = [];
    public string GearChecklistName { get; set; } = "";
    /// <summary>Gear card grouped by farm zone (the "where to go" pivot) instead of
    /// by slot. Persisted like the Epics classic-only lens — a view choice survives
    /// a restart.</summary>
    public bool GearGroupByZone { get; set; }
    /// <summary>Path|timestamp of the last inventory dump the gear auto-done pass
    /// consumed. Persisted so a box the player deliberately unchecked is not
    /// re-fought on restart by the SAME dump; a new dump re-opens the question.</summary>
    public string GearInventoryAppliedStamp { get; set; } = "";
    /// <summary>Persistent Epic 1.0 checklist shown in the overlay. Seeded from the
    /// shipped quest catalog; manual checkboxes for now, with room for log/inventory
    /// auto-checking later.</summary>
    public List<EpicQuestChecklistItem> EpicQuestChecklist { get; set; } = [];
    public string EpicQuestClass { get; set; } = "";
    public List<string> EpicQuestCompleted { get; set; } = [];
    /// <summary>Per-class snapshot of which epic rows were already acquired when the
    /// "Epic complete" master check bulk-flipped the rest (#138, aodgizmo): unchecking
    /// the master restores this instead of leaving every row checked. Persisted so the
    /// undo survives a restart; a class completed before the snapshot existed has no
    /// key here and unchecking falls back to clearing just the completed flag.</summary>
    public Dictionary<string, List<string>> EpicQuestPreCompleteAcquired { get; set; } = [];
    public bool EpicQuestClassicOnly { get; set; }
    /// <summary>Color theme key (see EQBuddy.UI.Shared.ThemeCatalog); defaults to the
    /// original parchment-and-brass look so existing installs don't change on upgrade.</summary>
    public string Theme { get; set; } = "ParchmentBrass";

    /// <summary>The click-through alignment grid (discussion #34). Persisted so a grid
    /// left on comes back after a restart — turning it off is the same one menu click
    /// that turned it on.</summary>
    public bool ShowGridOverlay { get; set; }
    /// <summary>Minor grid line spacing in pixels; strong lines land every fourth.</summary>
    public double GridSpacing { get; set; } = 32;

    /// <summary>The cursor-finder ring (issue #81 — "I often lose my tiny cursor").
    /// Same persistence contract as the grid: left on, it comes back at launch.</summary>
    public bool ShowCursorRing { get; set; }
    /// <summary>Folder of classic-format zone map files (Brewall packs and kin).
    /// Null = auto-detect the game's own maps folder beside Logs.</summary>
    public string? MapFolder { get; set; }
    /// <summary>Ring diameter in DIPs.</summary>
    public double CursorRingSize { get; set; } = 46;

    /// <summary>The three colors behind the "Custom" theme (#RRGGBB); the rest of its
    /// palette is derived in EQBuddy.UI.Shared.CustomTheme. Null until first edited —
    /// the seed colors apply.</summary>
    public string? CustomThemeBg { get; set; }
    public string? CustomThemeText { get; set; }
    public string? CustomThemeAccent { get; set; }

    /// <summary>The newest version whose "What's new" notes this install has shown.
    /// Empty on installs from before the feature: those get just the current version's
    /// notes once (if the tutorial was already done — a fresh install skips notes
    /// entirely; onboarding belongs to the tutorial).</summary>
    public string LastSeenVersion { get; set; } = "";

    // ---- spawn timers (the Spawns window) ----
    /// <summary>Track named-mob spawn timers; the Spawns window opens whenever this is on.
    /// Default ON (David's call): the window is the feature's front door, and a default-off
    /// window behind a right-click menu is a feature nobody's family finds. Closing the
    /// window opts out, and that sticks.</summary>
    public bool TrackSpawns { get; set; } = true;
    public double SpawnLeft { get; set; } = double.NaN;
    public double SpawnTop { get; set; } = double.NaN;
    /// <summary>Follow the zone the log says the player is in; off = stay on the zone
    /// picked in the window's dropdown.</summary>
    public bool SpawnFollowZone { get; set; } = true;
    /// <summary>One-time repair (1.20.1): 1.20.0 could untick SpawnFollowZone on a
    /// selection event the user never made, so following silently died. The auto-untick
    /// is gone; this restores the default once for anyone the bug touched.</summary>
    public bool SpawnFollowRepaired { get; set; }
    /// <summary>Last manually-picked zone, for when SpawnFollowZone is off.</summary>
    public string SpawnZone { get; set; } = "";
    /// <summary>UNUSED since 1.23.0 (kept so older settings.json round-trips): spawn
    /// "Default" now follows <see cref="AlertSound"/>, the same default watch rules use —
    /// a second spawn-specific default made "Default" mean silence, which read as broken.</summary>
    public string SpawnSound { get; set; } = "Off";
    /// <summary>Position of the spawn-chicklet stack; NaN = a default spot near the
    /// top-left, clear of the widget's home edge.</summary>
    public double SpawnChipsLeft { get; set; } = double.NaN;
    public double SpawnChipsTop { get; set; } = double.NaN;
    /// <summary>Bottom edge of the spawn-chip stack at last close. Grow-up stacks
    /// anchor their BOTTOM, and the top edge depends on chip count at close — so the
    /// bottom is what restores when growing upward (#122). NaN = never saved.</summary>
    public double SpawnChipsBottom { get; set; } = double.NaN;

    /// <summary>Position of the mez-chip stack — its own window, deliberately separate
    /// from the spawn chips (mez chips are combat-urgent, spawn chips are ambient).</summary>
    public double MezChipsLeft { get; set; } = double.NaN;
    public double MezChipsTop { get; set; } = double.NaN;
    /// <summary>See <see cref="SpawnChipsBottom"/> — same rule, mez stack.</summary>
    public double MezChipsBottom { get; set; } = double.NaN;

    /// <summary>Target-drops block in the Loot card (wiki drops for the creature being
    /// fought). Default on; the toggle exists for lean-card people.</summary>
    public bool ShowTargetDrops { get; set; } = true;

    /// <summary>Loot list order: "count" (biggest stacks first, the original behavior) or
    /// "name" (alphabetical — Klona11's ask, discussion #43).</summary>
    public string LootSort { get; set; } = "count";

    /// <summary>Player-supplied hp-per-tick for the regen healing estimate (0 = use the
    /// wiki base value). The log can't see instrument resonance or spell ranks; the
    /// player's own health bar can — their number wins (David, 2026-08-06).</summary>
    public int RegenPerTickOverride { get; set; }

    /// <summary>Hide the widget (and its satellite windows) while the game is running but
    /// NOT the foreground app — alt-tabbing to a browser shouldn't leave the widget over
    /// its buttons (sicliffe-cloud, discussion #41). Off by default; when the game isn't
    /// running at all the widget always shows (people configure it outside the game).</summary>
    public bool HideWhenGameUnfocused { get; set; }
    /// <summary>Hide the widget (and every satellite) while EverQuest Legends isn't
    /// RUNNING at all (#114) — the complement of <see cref="HideWhenGameUnfocused"/>,
    /// which deliberately keeps the widget visible in that case. Both off by default;
    /// they compose. EQBuddy's own windows having focus always overrides the hide.</summary>
    public bool HideWhenGameNotRunning { get; set; }

    // Breakout stat windows (BREAKOUT-*): one position + Fight/Session scope per kind.
    // They open while the widget is minimized with the matching star set.

    /// <summary>Breakout kinds the player ✕-closed for good ("Damage", "Loot", …): the
    /// star keeps its mini-pill chip, the window stays away until re-enabled in Options
    /// (Frankthetankk, discussion #45 — ✕-until-next-minimize made the window a
    /// whack-a-mole).</summary>
    public List<string> DisabledBreakouts { get; set; } = [];
    public double BreakoutDamageLeft { get; set; } = double.NaN;
    public double BreakoutDamageTop { get; set; } = double.NaN;
    public string BreakoutDamageScope { get; set; } = "fight";
    public double BreakoutHealingLeft { get; set; } = double.NaN;
    public double BreakoutHealingTop { get; set; } = double.NaN;
    public string BreakoutHealingScope { get; set; } = "fight";
    public double BreakoutPetLeft { get; set; } = double.NaN;
    public double BreakoutPetTop { get; set; } = double.NaN;
    public string BreakoutPetScope { get; set; } = "fight";
    /// <summary>The Watch breakout (CrispyPigeon131, discussion #44): pinned watch rules
    /// as a floating window while minimized. No scope — rules are session counters.</summary>
    public double BreakoutWatchLeft { get; set; } = double.NaN;
    public double BreakoutWatchTop { get; set; } = double.NaN;
    /// <summary>The Loot breakout (David's live report 2026-08-06): target drops while
    /// fighting, session loot between fights, opened by the 🎒 star while minimized.</summary>
    public double BreakoutLootLeft { get; set; } = double.NaN;
    public double BreakoutLootTop { get; set; } = double.NaN;
    /// <summary>"target" (drops for the creature you're fighting or last /considered) or
    /// "session" (what you've looted).</summary>
    public string BreakoutLootScope { get; set; } = "target";
    // Per-breakout manual size (NaN = auto-size to content). Set the moment the resize
    // grip is dragged; cleared by double-clicking it (David: let me resize the loot
    // window and scroll, 2026-08-06).
    public double BreakoutDamageWidth { get; set; } = double.NaN;
    public double BreakoutDamageHeight { get; set; } = double.NaN;
    public double BreakoutHealingWidth { get; set; } = double.NaN;
    public double BreakoutHealingHeight { get; set; } = double.NaN;
    public double BreakoutPetWidth { get; set; } = double.NaN;
    public double BreakoutPetHeight { get; set; } = double.NaN;
    public double BreakoutWatchWidth { get; set; } = double.NaN;
    public double BreakoutWatchHeight { get; set; } = double.NaN;
    public double BreakoutLootWidth { get; set; } = double.NaN;
    public double BreakoutLootHeight { get; set; } = double.NaN;
    // Per-breakout row sort for the stat kinds: "total" | "hits" | "avg" | "rate".
    public string BreakoutDamageSort { get; set; } = "total";
    public string BreakoutHealingSort { get; set; } = "total";
    public string BreakoutPetSort { get; set; } = "total";

    private static string FilePath => AppPaths.File("settings.json");

    // NaN is a legitimate value here ("not placed yet" window positions), and the
    // default serializer refuses it — which made Save() throw and silently drop
    // every settings change on profiles with an unplaced window.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>Bump when adding a built-in rule; see <see cref="DefaultRulesVersion"/>.</summary>
    private const int CurrentDefaultRulesVersion = 1;

    public static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new()
                : new AppSettings();
        }
        catch (Exception ex)
        {
            CoreLog.Error(ex); // corrupted settings — start fresh, but say so
            settings = new AppSettings();
        }
        // Non-short-circuiting on purpose: rules saved before ids existed get theirs
        // assigned at construction, and persisting them NOW is what makes the id stable
        // across restarts rather than re-rolled every launch until some unrelated edit
        // happens to save settings.
        var changed = settings.ApplyDefaultRules();
        changed |= settings.ApplyDefaultSkyQuestSection();
        changed |= settings.ApplyDefaultGearSection();
        changed |= settings.ApplyDefaultEpicQuestSection();
        changed |= settings.ApplyDefaultSkyQuestChecklist();
        changed |= settings.ApplyDefaultEpicQuestChecklist();
        if (changed | settings.TrackedRules.Any(r => r.IdWasGenerated))
            settings.Save();
        return settings;
    }

    /// <summary>
    /// Adds built-in watch rules that ship enabled. A charm or mez breaking is the one
    /// event where finding out late is expensive — and you are looking at the game, not
    /// the widget — so both the banner and the sound are on out of the box rather than
    /// waiting for the player to discover watch rules and configure one.
    ///
    /// Everything about it stays editable: 🔔 and 🔊 toggle per rule, the class filter and
    /// name are editable, the whole rule can be deleted (and stays deleted), and the sound
    /// itself is the shared <see cref="AlertSound"/> choice.
    ///
    /// Runs once per version — deleting the rule makes it stay deleted.
    /// Returns true when something changed and the settings need saving.
    /// </summary>
    public bool ApplyDefaultRules()
    {
        if (DefaultRulesVersion >= CurrentDefaultRulesVersion) return false;
        if (DefaultRulesVersion < 1 &&
            !TrackedRules.Any(r => r.Kind == WatchKind.SpellFade &&
                                   r.SpellFilter == SpellFilter.AnyCrowdControl))
        {
            TrackedRules.Add(new TrackedRule
            {
                Name = "CC broke",
                Kind = WatchKind.SpellFade,
                SpellFilter = SpellFilter.AnyCrowdControl,
                AlertBanner = true,
                AlertSound = true,
            });
        }
        DefaultRulesVersion = CurrentDefaultRulesVersion;
        return true;
    }

    /// <summary>
    /// One-time migration for settings saved before the Sky Quest card existed: slot
    /// "sky" in at its catalog position (after motes) instead of letting the UI append
    /// it last. Deliberately insert-only — ordering, dedup, and unknown-key cleanup
    /// stay the UI layer's job (ApplySectionLayout / the cards editor), so Core never
    /// carries its own copy of the section catalog that could drift out of sync.
    /// A fresh install's empty order is left empty: the UI appends the catalog itself.
    /// </summary>
    public bool ApplyDefaultSkyQuestSection()
    {
        if (SectionOrder.Count == 0 || SectionOrder.Contains("sky")) return false;
        var motes = SectionOrder.IndexOf("motes");
        SectionOrder.Insert(motes < 0 ? SectionOrder.Count : motes + 1, "sky");
        return true;
    }

    public bool ApplyDefaultGearSection()
    {
        if (SectionOrder.Count == 0 || SectionOrder.Contains("gear")) return false;
        var sky = SectionOrder.IndexOf("sky");
        var motes = SectionOrder.IndexOf("motes");
        var anchor = sky >= 0 ? sky : motes;
        SectionOrder.Insert(anchor < 0 ? SectionOrder.Count : anchor + 1, "gear");
        return true;
    }

    public bool ApplyDefaultEpicQuestSection()
    {
        if (SectionOrder.Count == 0 || SectionOrder.Contains("epic")) return false;
        var gear = SectionOrder.IndexOf("gear");
        var sky = SectionOrder.IndexOf("sky");
        var motes = SectionOrder.IndexOf("motes");
        var anchor = gear >= 0 ? gear : sky >= 0 ? sky : motes;
        SectionOrder.Insert(anchor < 0 ? SectionOrder.Count : anchor + 1, "epic");
        return true;
    }

    public bool ApplyDefaultSkyQuestChecklist()
    {
        SkyQuestChecklist ??= [];
        var changed = false;
        foreach (var item in SkyQuestDefaults.Items)
        {
            if (SkyQuestChecklist.Any(i => string.Equals(i.Id, item.Id, StringComparison.Ordinal)))
                continue;

            SkyQuestChecklist.Add(item.Clone());
            changed = true;
        }

        return changed;
    }

    public bool ApplyDefaultEpicQuestChecklist()
    {
        EpicQuestChecklist ??= [];
        var changed = false;
        foreach (var item in EpicQuestDefaults.Items())
        {
            var existing = EpicQuestChecklist.FirstOrDefault(i => string.Equals(i.Id, item.Id, StringComparison.Ordinal));
            if (existing is not null)
            {
                if (existing.QuestName == item.QuestName &&
                    existing.Reward == item.Reward &&
                    existing.Section == item.Section &&
                    existing.QuestItem == item.QuestItem &&
                    existing.Qty == item.Qty &&
                    existing.Order == item.Order &&
                    existing.Source == item.Source &&
                    existing.AvailableInClassic == item.AvailableInClassic &&
                    existing.ItemNames.SequenceEqual(item.ItemNames, StringComparer.Ordinal))
                    continue;

                existing.QuestName = item.QuestName;
                existing.Reward = item.Reward;
                existing.Section = item.Section;
                existing.QuestItem = item.QuestItem;
                existing.Qty = item.Qty;
                existing.Order = item.Order;
                existing.Source = item.Source;
                existing.AvailableInClassic = item.AvailableInClassic;
                existing.ItemNames = [.. item.ItemNames];
                changed = true;
                continue;
            }

            EpicQuestChecklist.Add(item.Clone());
            changed = true;
        }

        return changed;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch (Exception ex)
        {
            CoreLog.Error(ex); // non-fatal, but visible
        }
    }
}

public sealed class SkyQuestChecklistItem
{
    public string Id { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string Npc { get; set; } = "";
    public string Reward { get; set; } = "";
    public string QuestItem { get; set; } = "";
    public string Source { get; set; } = "";
    public bool Acquired { get; set; }
    /// <summary>True when the loot auto-tick PLACED this check itself because the
    /// item is wanted by several classes and none of them passed the class lens
    /// (#106, bjstrange's two-quest staff: "check one of them off, doesn't matter
    /// which, and let me decide"). Shown as a * so the player can move the tick;
    /// any manual toggle clears it — the player deciding IS the resolution.</summary>
    public bool AcquiredUnassigned { get; set; }

    public SkyQuestChecklistItem Clone() => new()
    {
        Id = Id,
        ClassName = ClassName,
        Npc = Npc,
        Reward = Reward,
        QuestItem = QuestItem,
        Source = Source,
        Acquired = Acquired,
        AcquiredUnassigned = AcquiredUnassigned,
    };
}

public sealed class GearChecklistItem
{
    public string Slot { get; set; } = "";
    /// <summary>True when this is a socketed exaltation rather than equipped gear.</summary>
    public bool IsExaltation { get; set; }
    public string Item { get; set; } = "";
    /// <summary>The effect granted by a socketed exaltation, when supplied by the export.</summary>
    public string ExaltationEffect { get; set; } = "";
    public string Source { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Acquired { get; set; }
}

public sealed class EpicQuestChecklistItem
{
    public string Id { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string QuestName { get; set; } = "";
    public string Reward { get; set; } = "";
    public string Section { get; set; } = "";
    public string QuestItem { get; set; } = "";
    public int Qty { get; set; } = 1;
    public int Order { get; set; }
    public string Source { get; set; } = "";
    public bool AvailableInClassic { get; set; } = true;
    public bool Acquired { get; set; }
    /// <summary>The catalog turn-in items this prose step mentions — the loot auto-tick's
    /// match key (#121), resolved in EpicQuestDefaults from the class's epic quest items.
    /// Empty when no loot line can prove the step (hails, dialogue, kill-only steps) —
    /// those rows simply never auto-tick.</summary>
    public List<string> ItemNames { get; set; } = [];
    /// <summary>True when the loot auto-tick PLACED this check itself because the
    /// item is wanted by several classes' epics and none of them passed the class
    /// lens — same contract as SkyQuestChecklistItem.AcquiredUnassigned (#106).
    /// Shown as a * so the player can move the tick; any manual toggle clears it —
    /// the player deciding IS the resolution.</summary>
    public bool AcquiredUnassigned { get; set; }

    public EpicQuestChecklistItem Clone() => new()
    {
        Id = Id,
        ClassName = ClassName,
        QuestName = QuestName,
        Reward = Reward,
        Section = Section,
        QuestItem = QuestItem,
        Qty = Qty,
        Order = Order,
        Source = Source,
        AvailableInClassic = AvailableInClassic,
        Acquired = Acquired,
        ItemNames = [.. ItemNames],
        AcquiredUnassigned = AcquiredUnassigned,
    };
}
