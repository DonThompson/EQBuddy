using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Templates;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Avalonia;

public sealed class MainWindow : Window, IZoneHost, IQuestsHost, IDropsHost
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly SessionStats _stats = new();
    // Attached at construction (not in SessionStats itself) so tests never touch disk.
    private void AttachSpellStore() =>
        _stats.Spells.AttachStore(System.IO.Path.Combine(Core.AppPaths.Dir, "spell-categories.json"));
    private readonly LogWatcher _watcher;
    private readonly SpawnTimers _spawnTimers;
    public SpawnTimers SpawnTimers => _spawnTimers;
    private readonly EQBuddy.UI.Shared.SpawnsViewModel _spawnsVm;
    private readonly SpawnPointLedger _spawnPoints;
    private readonly SpawnOverrides _spawnOverrides;
    private readonly SpawnCatalog _spawnCatalog;
    public SpawnPointLedger SpawnPoints => _spawnPoints;
    public SpawnOverrides SpawnOverridesStore => _spawnOverrides;
    public SpawnCatalog SpawnCatalogData => _spawnCatalog;
    public QuestCatalog QuestCatalog { get; private set; } = new();
    public ZoneGraph ZoneGraph { get; private set; } = new();
    public QuestLedgerStore? QuestLedger { get; private set; }
    public string QuestCharacterKey => _stats.LedgerCharacterKey;

    /// <summary>The zone the log last put us in — the Quest Tracker measures distances
    /// from here.</summary>
    public string CurrentZoneName { get; private set; } = "";

    /// <summary>Followed character identity for window titles and exports.</summary>
    public (string Character, string Server) Identity =>
        (_stats.CharacterName ?? "", _stats.ServerName ?? "");
    private SpawnsWindow? _spawnsWindow;
    private SpawnChipsWindow? _spawnChipsWindow;
    private MezChipsWindow? _mezChipsWindow;
    private readonly MezTracker _mezTracker = new();
    private readonly SlowTracker _slowTracker = new();
    private readonly BuffTracker _buffTracker = new();
    private readonly RaidKillLedger _raidLedger;
    private readonly EqlWikiItemService _wikiItems =
        new(System.IO.Path.Combine(AppPaths.Dir, "wiki-cache", "items"));
    private ItemInfoWindow? _itemInfoWindow;
    private readonly EqlWikiMobService _wikiMobs =
        new(System.IO.Path.Combine(AppPaths.Dir, "wiki-cache", "mobs"));
    private readonly Dictionary<string, MobLookupResult?> _targetResults =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<BreakoutKind, BreakoutWindow> _breakouts = new();
    private readonly HashSet<BreakoutKind> _dismissedBreakouts = [];
    private readonly SessionRepository _repo = new(SessionRepository.DefaultDbPath);
    private readonly SessionArchiver _archiver;
    private DateTime _lastCheckpoint = DateTime.MinValue;
    private readonly Dictionary<string, int> _skyQuestLootSeen = new(StringComparer.OrdinalIgnoreCase);
    // Rebuilding 200+ checkboxes every UI tick is the one thing this overlay never
    // does elsewhere — the checklist re-renders only when a box actually changed.
    private bool _skyQuestDirty = true;
    private bool _gearChecklistDirty = true;
    private readonly DispatcherTimer _uiTimer;
    private readonly LayoutTransformControl _scaleRoot = new();
    private readonly Border _root = new();
    private readonly Grid _miniRoot = new();
    private readonly StackPanel _miniChips = new() { Orientation = Orientation.Horizontal };
    private readonly Ellipse _miniDot = Dot();
    private readonly StackPanel _normalRoot = new() { Width = 320 };
    private readonly Ellipse _statusDot = Dot();
    private readonly TextBlock _charLabel = AppTheme.DimText("looking for a character...");
    private readonly ScrollViewer _sectionScroll = new();
    private readonly Border _logBanner = Banner(AppTheme.WarnWashBrush);
    private readonly Border _updateBanner = Banner(AppTheme.GoodWashBrush);
    private readonly TextBlock _updateText = new() { FontSize = 12, Foreground = AppTheme.GoodBrush, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _zoneText = AppTheme.DimText("-");
    private readonly TextBlock _sessionText = AppTheme.DimText("session 0:00");
    private readonly TextBlock _combatHeader = AppTheme.StatValue("0 dps");
    private readonly TextBlock _healingHeader = AppTheme.StatValue("0 hps");
    private readonly TextBlock _killsHeader = AppTheme.StatValue("0");
    private readonly TextBlock _lootHeader = AppTheme.StatValue("0 items");
    private readonly TextBlock _trackedHeader = AppTheme.StatValue("0");
    private readonly TextBlock _motesHeader = AppTheme.StatValue("0");
    private readonly TextBlock _motesSummary = AppTheme.DimText("");
    private readonly ItemsControl _motesList = new();
    private readonly TextBlock _gearHeader = AppTheme.StatValue("0/0");
    private readonly TextBlock _gearListName = AppTheme.DimText("");
    private readonly StackPanel _gearChecklistPanel = new();
    private readonly TextBlock _raidsHeader = AppTheme.StatValue("0");
    private readonly StackPanel _raidsPanel = new();
    private readonly TextBlock _buffsHeader = AppTheme.StatValue("0");
    private readonly StackPanel _buffsPanel = new();
    /// <summary>Per-tick clock TextBlocks + their buff labels, so a tick with an
    /// unchanged buff SET updates text in place instead of rebuilding rows (the mez
    /// window's signature idiom).</summary>
    private readonly List<(TextBlock Clock, string Label)> _buffClocks = [];
    private string _buffsSignature = "";
    private readonly ComboBox _skyStateCombo = new() { Width = 96, FontSize = 11 };
    private readonly TextBox _skySearchBox = new()
    {
        FontSize = 11, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(5, 3),
        Background = AppTheme.ComboBoxBrush, Foreground = AppTheme.TextBrush,
        CaretBrush = AppTheme.TextBrush, BorderBrush = AppTheme.BorderBrush,
        BorderThickness = new Thickness(1),
    };
    private readonly ScrollViewer _skySearchScroll = new()
    {
        IsVisible = false,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(0, 0, 4, 0),
    };
    private readonly StackPanel _skySearchResults = new();
    private readonly TabControl _skyQuestTabs = new() { Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(0) };
    private readonly TextBlock _kpiDps = Kpi(accent: true);
    private readonly TextBlock _kpiKills = Kpi();
    private readonly TextBlock _kpiLoot = Kpi();
    private readonly TextBlock _kpiXp = Kpi();
    // #112 (Frankthetankk): EQBuddy's own footprint, on the record — off by default,
    // refreshed every few seconds when on.
    private readonly TextBlock _perfLabel = new()
    {
        FontSize = 10, Foreground = AppTheme.DimBrush, IsVisible = false,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
    };
    private readonly Grid _combatSparkHost = new() { Height = 34, Margin = new Thickness(0, 2, 0, 4), IsVisible = false };
    private readonly Polyline _combatSpark = new()
    {
        StrokeThickness = 1.8, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round,
        Stroke = AppTheme.ChartYouBrush,
    };
    private readonly Polygon _combatSparkFill = new();
    private Color _combatSparkFillColor;
    private readonly Ellipse _combatSparkPeak = new()
    {
        Width = 6, Height = 6, IsVisible = false, Fill = AppTheme.ChartYouBrush,
        Stroke = AppTheme.BgBrush, StrokeThickness = 1.5,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
    };
    private readonly TextBlock _procLabel = AppTheme.Heading("⚡ Procs");
    private readonly ItemsControl _procList = new();
    private readonly TextBlock _aaNewLabel = AppTheme.Heading("AA learned this session");
    private readonly ItemsControl _aaNewList = new();
    private readonly Button _combatFightCopy = AppTheme.IconButton("⧉",
        "Copy this fight as Discord-ready text (a monospace block — the official Discord blocks images, "
        + "so the parse travels as text). Your numbers only, from your log.");
    private readonly Button _combatFightTimeline = AppTheme.IconButton("⧗",
        "Fight timeline: the whole pull on one canvas — a lane per skill, every hit, miss and resist, "
        + "with a DPS-over-time graph. Scroll to zoom, drag to pan.");
    // Perf audit #1: the version last painted into the expanded sections, and the
    // last time a full paint happened (10 s heartbeat keeps time-derived rates live).
    private long _lastRenderedVersion = -1;
    private DateTime _lastFullRender = DateTime.MinValue;

    private static TextBlock Kpi(bool accent = false) => new()
    {
        Text = "0", FontSize = 17, FontWeight = FontWeight.SemiBold,
        Foreground = accent ? AppTheme.AccentBrush : AppTheme.TextBrush,
    };
    private readonly TextBlock _moneyHeader = AppTheme.StatValue("0c");
    private readonly TextBlock _progressHeader = AppTheme.StatValue("0% xp");
    private readonly TextBlock _factionHeader = AppTheme.StatValue("-");
    private readonly TextBlock _miscHeader = AppTheme.StatValue("0 deaths");
    private readonly TextBlock _combatSummary = AppTheme.DimText("");
    // The fight in front of you, above the session aggregate — see ShowLastFight. The
    // headings are buttons: each subsection collapses on its own and remembers it.
    private readonly Button _combatFightLabel = AppTheme.IconButton("v Last fight", "Show or hide this fight's breakdown");
    private readonly StackPanel _combatFightBody = new();
    private readonly TextBlock _combatFightText = AppTheme.DimText("");
    private readonly StackPanel _combatFightList = new();
    private readonly TextBlock _combatFightSplit = AppTheme.DimText("");
    private readonly TextBlock _combatFightOutLabel = AppTheme.Heading("Your damage");
    private readonly TextBlock _combatFightInLabel = AppTheme.Heading("Damage you took");
    private readonly ItemsControl _combatFightInList = new();
    private readonly Button _combatSessionLabel = AppTheme.IconButton("v Session so far", "Show or hide the session totals");
    private readonly StackPanel _combatSessionBody = new();
    private readonly Button _healFightLabel = AppTheme.IconButton("v Last fight", "Show or hide this fight's healing");
    private readonly StackPanel _healFightBody = new();
    private readonly TextBlock _healFightText = AppTheme.DimText("");
    private readonly StackPanel _healFightList = new();
    private readonly Button _healSessionLabel = AppTheme.IconButton("v Session so far", "Show or hide the session totals");
    private readonly StackPanel _healSessionBody = new();
    private readonly TextBlock _healingSummary = AppTheme.DimText("");
    private readonly TextBlock _killsSummary = AppTheme.DimText("");
    private readonly TextBlock _moneySummary = AppTheme.DimText("");
    private readonly TextBlock _progressSummary = AppTheme.DimText("");
    private readonly StackPanel _damageSourceList = new();
    private readonly TextBlock _petAbilityLabel = AppTheme.Heading("Pet abilities");
    private readonly StackPanel _petAbilityList = new();
    private readonly ItemsControl _damageTakenList = new();
    private readonly StackPanel _healSpellList = new();
    private readonly ItemsControl _healerList = new();
    private readonly ItemsControl _killList = new();
    private readonly ItemsControl _partyKillList = new();
    private readonly ItemsControl _lootList = new();
    private readonly StackPanel _targetDropsBlock = new() { IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _targetDropsHeader = AppTheme.Heading("", AppTheme.WarnBrush);
    private readonly ItemsControl _targetDropsList = new();
    private readonly StackPanel _trackedPanel = new();
    private readonly ItemsControl _craftedList = new();
    private readonly ItemsControl _soldList = new();
    private readonly ItemsControl _skillList = new();
    private readonly TextBlock _aaAbilitiesLabel = AppTheme.Heading("AA abilities");
    private readonly ItemsControl _aaAbilityList = new();
    private readonly ItemsControl _factionList = new();
    private readonly ItemsControl _deathList = new();
    private readonly ItemsControl _zoneList = new();
    private readonly TextBlock _healSpellsLabel = AppTheme.Heading("Heals cast", AppTheme.GoodBrush);
    private readonly StackPanel _healSortBar = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _healersLabel = AppTheme.Heading("Healed by", AppTheme.GoodBrush);
    private readonly TextBlock _partyKillsLabel = AppTheme.Heading("Group kills");
    private readonly TextBlock _craftedLabel = AppTheme.Heading("Created by merging");
    private readonly TextBlock _soldLabel = AppTheme.Heading("Sold to merchants");
    private readonly TextBlock _recentFightsLabel = AppTheme.Heading("Recent fights");
    private readonly ItemsControl _recentFightsList = new();
    private readonly TextBlock _areaSpellLabel = AppTheme.Heading("Area spells (per cast)");
    private readonly ItemsControl _areaSpellList = new();
    private readonly TextBlock _stanceLabel = AppTheme.Heading("By stance");
    private readonly ItemsControl _stanceList = new();
    private readonly TextBlock _invocationLabel = AppTheme.Heading("By invocation");
    private readonly ItemsControl _invocationList = new();
    private readonly TextBlock _farmingLabel = AppTheme.Heading("Farming (per creature)");
    private readonly ItemsControl _farmingList = new();
    private readonly TextBlock _markersLabel = AppTheme.Heading("Camp markers");
    private readonly ItemsControl _markerList = new();
    private readonly Button _gearBtn = AppTheme.IconButton(AppIcon.Settings, "Settings");
    private readonly Dictionary<string, Button> _stars = new();
    private readonly Dictionary<string, SectionPanel> _sections = new(StringComparer.OrdinalIgnoreCase);
    private readonly StackPanel _sectionsPanel = new();
    private TextBlock _dmgOutSortTotal = null!;
    private TextBlock? _dmgOutSortDps;
    private TextBlock _dmgOutSortHits = null!;
    private TextBlock _dmgOutSortAvg = null!;
    private TextBlock _dmgInSortTotal = null!;
    private TextBlock _dmgInSortHits = null!;
    private TextBlock _dmgInSortAvg = null!;
    private TextBlock _healSortTotal = null!;
    private TextBlock? _healSortHps;
    private TextBlock _healSortHits = null!;
    private TextBlock _healSortAvg = null!;
    private DateTime _lastCharScan = DateTime.MinValue;
    private DateTime _lastJanitorRun = DateTime.MinValue;
    private DateTime _lastUpdateCheck = DateTime.MinValue;
    private UpdateInfo? _pendingUpdate;
    private DateTime _upToDateNoticeUntil = DateTime.MinValue;
    private bool _installingUpdate;
    private bool _clickThrough;
    private HistoryWindow? _historyWindow;
    private OptionsWindow? _optionsWindow;
    private readonly MenuItem _reviewLogItem = new()
    {
        Header = "Review an archived log…",
    };
    private readonly MenuItem _chooseLogFolderItem = new()
    {
        Header = "Choose log folder…",
    };
    private readonly MenuItem _clickThroughItem = new()
    {
        Header = "Click-through (game clicks pass through)",
    };
    private ClickThroughChip? _unlockChip;
    private AlertWindow? _alertWindow;
    private IReadOnlyList<WhatsNewEntry> _whatsNewNotes = [];
    private StatSort _dmgOutSort = StatSort.Total;
    private StatSort _dmgInSort = StatSort.Total;
    private StatSort _healSort = StatSort.Total;
    private readonly bool _expandForTesting = Environment.GetEnvironmentVariable("EQBUDDY_EXPAND") == "1";

    private static readonly string[] MiniStatOrder = ["kills", "dps", "hps", "pet", "procs", "loot", "motes", "money", "xp", "deaths"];

    // StatSort moved to BreakdownRows.cs (internal) when the breakout windows grew
    // their own sort bars — one enum, every surface.

    public MainWindow()
    {
        // Before the watcher's startup replay, so already-logged charms classify with
        // everything learned in earlier sessions (issue #29).
        AttachSpellStore();
        _stats.AaStore = new AaLedgerStore(AppPaths.File("aa-ledger.json"));
        _mezTracker.AttachStore(AppPaths.File("mez-durations.json"));
        // Quest ledger rides the same replay: the catalog decides what's worth keeping,
        // the store's time high-water mark keeps the replay from double-counting.
        QuestCatalog = QuestCatalog.LoadEmbedded();
        ZoneGraph = ZoneGraph.LoadEmbedded();
        QuestLedger = new QuestLedgerStore(AppPaths.File("quest-ledger.json"))
        { TrackFilter = QuestCatalog.IsTurnInItem, Normalize = QuestCatalog.BaseItemName };
        _stats.QuestStore = QuestLedger;
        _watcher = new LogWatcher(_stats);
        _watcher.Mez = _mezTracker;
        _watcher.Slow = _slowTracker;
        _slowTracker.Landed += OnSlowLanded;
        _buffTracker.AttachStore(AppPaths.File("buff-durations.json"));
        // Your Spell Casting Reinforcement rank stretches your own casts' estimates
        // (+5/15/30/50%); learned durations already carry it and are never re-scaled.
        _buffTracker.ReinforcementRank = () => _stats.AaRank("Spell Casting Reinforcement");
        _watcher.Buffs = _buffTracker;
        EQBuddy.UI.Shared.SpokenAlerts.Warmup();   // first alert must not pay TTS init
        _raidLedger = new RaidKillLedger(AppPaths.File("raid-kills.json"))
        { CharacterKey = () => _stats.LedgerCharacterKey };
        _watcher.Raids = _raidLedger;
        var spawnCatalog = SpawnCatalog.LoadEmbedded();
        var spawnOverrides = SpawnOverrides.Load(AppPaths.File("spawn-overrides.json"));
        _spawnTimers = new SpawnTimers(spawnCatalog, spawnOverrides, AppPaths.File("spawn-timers.json"));
        _watcher.Spawns = _spawnTimers;
        _spawnsVm = new EQBuddy.UI.Shared.SpawnsViewModel(spawnCatalog, spawnOverrides, _spawnTimers);
        // Voice settings are set app-wide even though the picker UI is WPF-only for now:
        // this build speaks through the same SpokenAlerts on Windows, and settings.json
        // may have been shaped by the WPF app. Stored-only on Linux (Speak no-ops there)
        // and ignored by macOS's `say`.
        EQBuddy.UI.Shared.SpokenAlerts.Configure(
            _settings.SpeechVoice, _settings.SpeechRate, _settings.SpeechVolume);
        // The map's spawn-point circles: kills near a fresh /loc accrete into
        // per-zone archives that only refine over time (David's map brief).
        _spawnPoints = new SpawnPointLedger(
            System.IO.Path.Combine(AppPaths.Dir, "zone-spawns"), spawnCatalog);
        _watcher.SpawnPoints = _spawnPoints;
        _spawnOverrides = spawnOverrides;
        _spawnCatalog = spawnCatalog;
        // Before any tailing: the initial full-log ingest has to know which text rules to
        // watch for, or a Text rule would miss everything already in today's log.
        _stats.RefreshTextPatterns(_settings.TrackedRules);
        _stats.TextMatched += OnTextMatched;
        // An idle gap ended the session: anything still cued belongs to a fight that is
        // long over.
        _stats.SessionRolledOver += () => Dispatcher.UIThread.Post(_delayedAlerts.CancelAll);
        _archiver = new SessionArchiver(_repo);
        // A 60-minute quiet gap ends a session — persist its final state to history.
        // Not while reviewing an archived log (#74): those sessions were archived when
        // they were live; replay must not mint duplicates.
        _stats.SessionEnding += snap =>
        {
            if (_reviewPath is null) _archiver.FinalizeActive(snap, "IdleTimeout");
        };
        Title = "EQBuddy";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = true;
        CanResize = false;
        Opacity = _settings.Opacity;
        Content = BuildRoot();

        // Migration: any old per-rule pin enables the replacement group pin.
        if (!_settings.PinWatchChips && _settings.TrackedRules.Any(r => r.Pinned))
            _settings.PinWatchChips = true;
        // Chips became per-rule again: someone who had them on was seeing every enabled rule,
        // so pin what they already had rather than silently emptying their mini bar. Once
        // only — gated on a flag so deliberately unpinning every rule isn't undone next launch.
        if (!_settings.WatchPinsMigrated)
        {
            // Not conditioned on "nothing is pinned": AppSettings.Load may already have
            // added the built-in CC-broke rule, which is pinned by default, and that made
            // this pass skip itself and leave the user's own rules invisible.
            if (_settings.PinWatchChips)
                foreach (var rule in _settings.TrackedRules.Where(r => r.Enabled))
                    rule.Pinned = true;
            _settings.WatchPinsMigrated = true;
            _settings.Save();
        }

        if (_settings.LogFolder is { } saved && !Directory.Exists(saved))
            _settings.LogFolder = null;
        _settings.LogFolder ??= LogWatcher.FindDefaultLogFolder();
        RestorePosition();
        ApplyUiScale(_settings.UiScale);
        // Ctrl+wheel over the widget drives UiScale (WPF parity): the setter clamps,
        // applies, and persists on its own.
        WindowZoom.Route(this, () => _settings.UiScale, SetUiScale);
        ApplyBackgroundOpacity(_settings.BackgroundOpacity);
        UpdateStarVisuals();
        ApplySectionLayout();
        SetMode(_settings.Minimized);
        if (_expandForTesting)
            foreach (var section in _sections.Values)
                section.IsExpanded = true;
        // Expanding a card renders it NOW (David's field report: sections only fill
        // inside the fullRender gate, so a click during a quiet moment stared at an
        // empty body until the next event or the 10 s heartbeat). Background priority
        // lets the panel's own layout land first, so the click still feels mechanical.
        foreach (var section in _sections.Values)
            section.ExpandedChanged += _ =>
            {
                _lastRenderedVersion = -1;
                _skyQuestDirty = true;
                Dispatcher.UIThread.Post(RefreshUi, DispatcherPriority.Background);
            };
        FollowActiveCharacter();

        PrepareWhatsNew();

        if (_settings.LogFolder is { } lf)
        {
            // Page one of the launch tour is the log-truncation consent question.
            // Leave existing logs untouched until the user has answered it.
            var prune = _settings.TruncateLogs && !_settings.ShowTutorial;
            var archive = _settings.ArchiveLogs;
            Task.Run(() =>
            {
                EqConfig.EnsureLoggingEnabled(lf);
                if (prune) EqConfig.TruncateStaleLogs(lf, SessionStats.SessionGap, archive: archive);
            });
        }

        if (Environment.GetEnvironmentVariable("EQBUDDY_CCLOG") == "1")
            StartCrowdControlCapture();

        // Warm the embedded item catalog off-thread: its one-time gunzip+parse
        // (~11k records) must never land on the UI thread mid-fight via the first
        // loot-row tooltip — after this, first UI touch is a dictionary probe.
        Task.Run(() => Core.ItemCatalog.Default);

        // Screenshot/debug hooks, one family (EQBUDDY_*): open a satellite window
        // after the startup replay has fed the ledger. Deferred a beat via Post so
        // the widget's own layout lands first, mirroring WPF's ApplicationIdle.
        if (Environment.GetEnvironmentVariable("EQBUDDY_DROPS") == "1")
            Loaded += (_, _) => Dispatcher.UIThread.Post(() => OnDropsWindow(this, EventArgs.Empty),
                DispatcherPriority.ApplicationIdle);

        // "1" opens the default view; "zone"/"all" open that mode directly.
        if (Environment.GetEnvironmentVariable("EQBUDDY_QUESTS") is { Length: > 0 } questsMode)
            Loaded += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                ShowQuestsWindow();
                if (questsMode is "zone" or "all") _questsWindow?.SetMode(questsMode);
            }, DispatcherPriority.ApplicationIdle);

        if (Environment.GetEnvironmentVariable("EQBUDDY_OPTIONS") == "1")
            Loaded += (_, _) => OnOptions(this, EventArgs.Empty);

        if (Environment.GetEnvironmentVariable("EQBUDDY_MAP") == "1")
            Loaded += (_, _) => Dispatcher.UIThread.Post(() => OnZoneMap(this, EventArgs.Empty),
                DispatcherPriority.ApplicationIdle);

        if (Environment.GetEnvironmentVariable("EQBUDDY_TRAVEL") == "1")
            Loaded += (_, _) => Dispatcher.UIThread.Post(() => OnTravelRoute(this, EventArgs.Empty),
                DispatcherPriority.ApplicationIdle);

        if (Environment.GetEnvironmentVariable("EQBUDDY_INVENTORY") == "1")
            Loaded += (_, _) => Dispatcher.UIThread.Post(() => OnInventoryWindow(this, EventArgs.Empty),
                DispatcherPriority.ApplicationIdle);

        if (Environment.GetEnvironmentVariable("EQBUDDY_GEARLOCKER") == "1")
            Loaded += (_, _) => Dispatcher.UIThread.Post(() => OnGearLocker(this, EventArgs.Empty),
                DispatcherPriority.ApplicationIdle);

        if (Environment.GetEnvironmentVariable("EQBUDDY_TIMELINE") == "1")
            Loaded += (_, _) => Dispatcher.UIThread.Post(OpenFightTimeline,
                DispatcherPriority.ApplicationIdle);

        if (Environment.GetEnvironmentVariable("EQBUDDY_FEEDBACK") == "1")
            Loaded += (_, _) => new FeedbackWindow().Show(this);

        // Screenshot/debug hook, same family as EQBUDDY_QUESTS: open straight into
        // archive review of the given file (#74), skipping the file dialog.
        if (Environment.GetEnvironmentVariable("EQBUDDY_REVIEW") is { Length: > 0 } reviewPath)
            Loaded += (_, _) => Dispatcher.UIThread.Post(() => _ = EnterReview(reviewPath),
                DispatcherPriority.ApplicationIdle);

        // 1.20.0 could turn Follow off on a selection event the user never made.
        // Repair affected profiles once; subsequent user choices are left alone.
        if (!_settings.SpawnFollowRepaired)
        {
            _settings.SpawnFollowZone = true;
            _settings.SpawnFollowRepaired = true;
            _settings.Save();
        }

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uiTimer.Tick += (_, _) => RefreshUi();
        _uiTimer.Start();
        Loaded += (_, _) =>
        {
            UpdateWindowHeightLimit();
            // A grid left on comes back — turning it off is the same Options click (#34).
            if (_settings.ShowGridOverlay) SetGridOverlay(true);
            if (_settings.ShowCursorRing) SetCursorRing(true);
            // Tray icon: EQBuddy's always-there presence (#114 follow-up) — when a hide
            // takes the widget AND its taskbar entry, this is how you know it's running
            // and how you get it back.
            _trayIcon = new TrayIcon(this, () => OnOptions(this, EventArgs.Empty));
            ApplyHotkeys();
            if (_settings.ShowTutorial)
                new TutorialWindow(this).Show(this);
            else if (_whatsNewNotes.Count > 0)
                new WhatsNewWindow(_whatsNewNotes).Show(this);
        };
        // A portrait secondary can be much taller than the primary. Recalculate after
        // every move so crossing a monitor boundary updates the available card height.
        PositionChanged += (_, _) => UpdateWindowHeightLimit();
    }

    /// <summary>Records the running version before displaying release notes, so an
    /// interrupted launch cannot show the same popup forever. Fresh installs use the
    /// tutorial instead; installs predating this feature see only the current release.</summary>
    private void PrepareWhatsNew()
    {
        var currentVersion = UpdateChecker.CurrentVersion.ToString();
        if (_settings.ShowTutorial || _settings.LastSeenVersion == currentVersion)
        {
            if (_settings.LastSeenVersion != currentVersion)
            {
                _settings.LastSeenVersion = currentVersion;
                _settings.Save();
            }
            return;
        }

        var lastSeen = _settings.LastSeenVersion.Length > 0
            ? _settings.LastSeenVersion
            : PreviousVersionBaseline(currentVersion);
        _whatsNewNotes = WhatsNewCatalog.EntriesBetween(lastSeen, currentVersion);
        _settings.LastSeenVersion = currentVersion;
        _settings.Save();
    }

    internal static string PreviousVersionBaseline(string current) =>
        Version.TryParse(current, out var version)
            ? new Version(version.Major, Math.Max(0, version.Minor - 1), 0).ToString()
            : current;

    public double UiScale => _settings.UiScale;
    public double WidgetOpacity => Opacity;
    public double BackgroundOpacityValue => _settings.BackgroundOpacity;
    public bool TruncateLogsValue => _settings.TruncateLogs;
    public AppSettings Settings => _settings;
    public void PersistSettings() => _settings.Save();

    /// <summary>
    /// Opt-in capture for CC-looking lines whose EQ Legends wording is not known yet.
    /// Keep only distinct lines and cap the file so diagnostics cannot grow without bound.
    /// </summary>
    private static void StartCrowdControlCapture()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = AppPaths.File("cc-candidates.txt");
        var gate = new object();
        LogParser.UnmatchedCandidateSink = message =>
        {
            lock (gate)
            {
                if (seen.Count >= 500 || !seen.Add(message)) return;
                try { File.AppendAllText(path, message + Environment.NewLine); }
                catch { /* diagnostics must never interrupt log tailing */ }
            }
        };
    }

    /// <summary>The card catalog now lives in UI.Shared (one list, both UIs and the
    /// Options card editor); this alias keeps existing call sites/tests readable.</summary>
    internal static readonly (string Key, string Title)[] SectionCatalog =
        EQBuddy.UI.Shared.OverlaySections.Catalog;

    public void ApplySectionLayout()
    {
        var order = _settings.SectionOrder.Where(_sections.ContainsKey).ToList();
        // Same ContainsKey guard as the line above: the catalog is shared with WPF, so a
        // card can land there before this UI builds it. Appending blind made that a
        // startup crash (KeyNotFoundException) instead of a merely missing card.
        foreach (var (key, _) in SectionCatalog)
            if (!order.Contains(key) && _sections.ContainsKey(key)) order.Add(key);

        // Options is the whole truth (David's 1.66.2 verdict): a card the user hasn't
        // hidden SHOWS, empty or not — self-hiding cards read as missing features. The
        // renders fill an honest one-line empty state instead of vanishing the card.
        _sectionsPanel.Children.Clear();
        foreach (var key in order)
        {
            var section = _sections[key];
            _sectionsPanel.Children.Add(section);
            section.IsVisible = !_settings.HiddenSections.Contains(key);
        }
    }

    public void SetTruncateLogs(bool enabled)
    {
        _settings.TruncateLogs = enabled;
        _settings.Save();
    }

    public void SetUiScale(double scale)
    {
        _settings.UiScale = Math.Clamp(scale, 0.5, 2.0);
        ApplyUiScale(_settings.UiScale);
        _settings.Save();
    }

    public void SetWindowOpacity(double opacity)
    {
        _settings.Opacity = Math.Clamp(opacity, 0.3, 1.0);
        Opacity = _settings.Opacity;
        _settings.Save();
    }

    public void SetBackgroundOpacity(double opacity)
    {
        _settings.BackgroundOpacity = Math.Clamp(opacity, 0.15, 1.0);
        ApplyBackgroundOpacity(_settings.BackgroundOpacity);
        _settings.Save();
    }

    private Control BuildRoot()
    {
        _scaleRoot.Child = _root;
        _root.CornerRadius = new CornerRadius(10);
        // Hairline, not the full border tone (the 2026-08-11 modernization):
        // the widget's edge should whisper.
        _root.BorderBrush = AppTheme.HairlineBrush;
        _root.BorderThickness = new Thickness(1);
        _root.ContextMenu = BuildContextMenu();
        _root.PointerPressed += OnDrag;
        _root.Child = new StackPanel
        {
            Margin = new Thickness(10),
            Children =
            {
                BuildMiniRoot(),
                BuildNormalRoot(),
            },
        };
        return _scaleRoot;
    }

    private Control BuildMiniRoot()
    {
        _miniRoot.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _miniRoot.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _miniRoot.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _miniRoot.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _miniDot.Margin = new Thickness(2, 0, 8, 0);
        _miniRoot.Children.Add(_miniDot);
        Grid.SetColumn(_miniChips, 1);
        _miniRoot.Children.Add(_miniChips);
        var restore = AppTheme.IconButton(AppIcon.Expand, "Expand");
        restore.Click += (_, _) => SetMode(false);
        restore.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(restore, 2);
        _miniRoot.Children.Add(restore);
        var close = AppTheme.IconButton(AppIcon.Close, "Close");
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 3);
        _miniRoot.Children.Add(close);
        return _miniRoot;
    }

    private Control BuildNormalRoot()
    {
        _normalRoot.Children.Add(BuildTitleBar());
        _logBanner.Child = new TextBlock
        {
            Text = "Logging looks off. Type /log in the game's chat window. EQBuddy enables it automatically for future game launches.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = AppTheme.WarnBrush,
        };
        _logBanner.Margin = new Thickness(0, 8, 0, 0);
        _normalRoot.Children.Add(_logBanner);
        _updateBanner.Child = _updateText;
        _updateBanner.Margin = new Thickness(0, 8, 0, 0);
        _updateBanner.Cursor = new Cursor(StandardCursorType.Hand);
        _updateBanner.PointerPressed += OnUpdateBannerClick;
        _normalRoot.Children.Add(_updateBanner);
        _normalRoot.Children.Add(BuildSessionLine());
        _normalRoot.Children.Add(BuildKpiRow());
        _sectionScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _sectionScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _sectionScroll.Content = BuildSections();
        _normalRoot.Children.Add(_sectionScroll);
        _normalRoot.Children.Add(BuildHeightGrip());
        return _normalRoot;
    }

    /// <summary>KPI strip (2026-08-11 modernization): the headline numbers before any
    /// card — always visible, one glance. Values are tabular semibold; DPS wears the
    /// accent. Hairline separators, not boxes.</summary>
    private Control BuildKpiRow()
    {
        var grid = new global::Avalonia.Controls.Primitives.UniformGrid { Columns = 4 };
        var first = true;
        foreach (var (label, value) in new[]
                 { ("dps", _kpiDps), ("kills", _kpiKills), ("loot", _kpiLoot), ("xp/hr", _kpiXp) })
        {
            var cell = new StackPanel { Margin = new Thickness(11, 6, 4, 7) };
            var caption = AppTheme.SectionLabel(label);
            caption.Margin = new Thickness(0);
            cell.Children.Add(caption);
            cell.Children.Add(value);
            if (first)
            {
                grid.Children.Add(cell);
                first = false;
            }
            else
                grid.Children.Add(new Border
                {
                    BorderBrush = AppTheme.HairlineBrush,
                    BorderThickness = new Thickness(1, 0, 0, 0),
                    Child = cell,
                });
        }
        return new Border
        {
            Margin = new Thickness(0, 0, 0, 7),
            CornerRadius = new CornerRadius(10),
            BorderBrush = AppTheme.HairlineBrush,
            BorderThickness = new Thickness(1),
            Background = AppTheme.PanelBrush,
            Child = grid,
        };
    }

    /// <summary>Bottom-edge height grip (Reddit ask, 2026-08-09): drag to show more or
    /// fewer cards before the list scrolls — TEXT size stays put, that's the Options
    /// slider's job. Invisible until hovered; double-tap returns to automatic.</summary>
    private Control BuildHeightGrip()
    {
        var hint = new Border
        {
            Height = 2.5, CornerRadius = new CornerRadius(1.25),
            Margin = new Thickness(18, 0, 18, 2),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = AppTheme.AccentBrush,
            Opacity = 0,
        };
        var grip = new global::Avalonia.Controls.Primitives.Thumb
        {
            Height = 12,
            Margin = new Thickness(12, 0, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
            Template = new FuncControlTemplate<global::Avalonia.Controls.Primitives.Thumb>((_, _) =>
                new Grid { Background = Brushes.Transparent, Children = { hint } }),
        };
        grip.PointerEntered += (_, _) =>
        {
            hint.Opacity = 0.7;
            var scrolling = _sectionsPanel.Bounds.Height > _sectionScroll.Bounds.Height + 1;
            ToolTip.SetTip(grip, scrolling
                ? "Drag down to show more cards (the list is scrolling); drag up to shorten. Double-tap: back to automatic."
                : "The widget is sizing itself automatically — everything you've selected in Options is shown. "
                  + "Drag up if you'd rather have it shorter (the list scrolls); double-tap returns to automatic.");
        };
        grip.PointerExited += (_, _) => hint.Opacity = 0;
        grip.DragStarted += (_, _) => _heightDragStart = _sectionScroll.Bounds.Height;
        grip.DragDelta += (_, e) =>
        {
            // The thumb reports deltas in the scaled widget's space; ContentHeight
            // lives in pre-scale units so it survives scale changes.
            _heightDragStart += e.Vector.Y;
            _settings.ContentHeight = Math.Max(120, _heightDragStart);
            ApplySectionMaxHeight();
        };
        grip.DragCompleted += (_, _) => _settings.Save();
        grip.DoubleTapped += (_, _) =>
        {
            _settings.ContentHeight = double.NaN;
            ApplySectionMaxHeight();
            _settings.Save();
        };
        _heightGrip = grip;
        return grip;
    }

    private global::Avalonia.Controls.Primitives.Thumb? _heightGrip;
    private double _heightDragStart;

    private Control BuildTitleBar()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < 5; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _statusDot.Margin = new Thickness(2, 0, 7, 0);
        title.Children.Add(_statusDot);
        title.Children.Add(new TextBlock { Text = "EQBuddy", FontWeight = FontWeight.Bold, FontSize = 14, Foreground = AppTheme.AccentBrush });
        grid.Children.Add(title);
        _charLabel.Margin = new Thickness(10, 0, 6, 0);
        _charLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(_charLabel, "Follows whoever is actively playing (log file growth)");
        _charLabel.PointerPressed += OnCharLabelClick;
        Grid.SetColumn(_charLabel, 1);
        grid.Children.Add(_charLabel);
        // #112: EQBuddy's own footprint in its own column, so a long character name
        // ellipsizes instead of colliding.
        ToolTip.SetTip(_perfLabel,
            "EQBuddy's own CPU (all cores) and memory — enable/disable under Options → Behavior");
        Grid.SetColumn(_perfLabel, 2);
        grid.Children.Add(_perfLabel);
        _gearBtn.Click += OnGear;
        Grid.SetColumn(_gearBtn, 3);
        grid.Children.Add(_gearBtn);
        var reset = AppTheme.IconButton(AppIcon.Refresh, "Reset session stats");
        reset.Click += (_, _) =>
        {
            // With archiving on, reset also splits the log (#52) — same as WPF.
            if (_settings.ArchiveLogs && _watcher.CurrentPath is { } path)
                Task.Run(() => EqConfig.SplitLog(path));
            _stats.Reset();
        };
        Grid.SetColumn(reset, 4);
        grid.Children.Add(reset);
        var mini = AppTheme.IconButton(AppIcon.Minimize, "Minimize to dashboard");
        mini.Click += (_, _) => SetMode(true);
        Grid.SetColumn(mini, 5);
        grid.Children.Add(mini);
        var close = AppTheme.IconButton(AppIcon.Close, "Close");
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 6);
        grid.Children.Add(close);
        return grid;
    }

    private Control BuildSessionLine()
    {
        var grid = new Grid { Margin = new Thickness(2, 8, 2, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.Children.Add(_zoneText);
        Grid.SetColumn(_sessionText, 1);
        grid.Children.Add(_sessionText);
        return grid;
    }

    private Control BuildSections()
    {
        AddSection("combat", "dps", "Combat", _combatHeader, BuildCombatSection(), "Show DPS in mini dashboard");
        AddSection("healing", "hps", "Healing", _healingHeader, BuildHealingSection(), "Show HPS in mini dashboard");
        AddSection("kills", "kills", "Kills", _killsHeader, BuildKillsSection(), "Show kills in mini dashboard");
        AddSection("loot", "loot", "Loot", _lootHeader, BuildLootSection(), "Show loot count in mini dashboard");
        AddSection("motes", "motes", "Motes", _motesHeader, BuildMotesSection(), "Show motes in mini dashboard");
        _sections["sky"] = AppTheme.Section(Header("☁ Sky Quest", _skyQuestHeader), BuildSkyQuestSection());
        _sections["gear"] = AppTheme.Section(Header("🛡 Gear", _gearHeader), BuildGearSection());
        _sections["tracked"] = AppTheme.Section(Header("Watch", _trackedHeader), _trackedPanel);
        _sections["buffs"] = AppTheme.Section(Header("⏳ Buffs", _buffsHeader), _buffsPanel);
        _sections["raids"] = AppTheme.Section(Header("🐉 Raids", _raidsHeader), _raidsPanel);
        AddSection("money", "money", "Money", _moneyHeader, BuildMoneySection(), "Show money in mini dashboard");
        AddSection("progress", "xp", "Progress", _progressHeader, BuildProgressSection(), "Show XP in mini dashboard");
        _sections["faction"] = AppTheme.Section(Header("Faction", _factionHeader), _factionList);
        AddSection("misc", "deaths", "Travels & Deaths", _miscHeader, BuildMiscSection(), "Show deaths in mini dashboard");
        return _sectionsPanel;
    }

    private Control BuildMotesSection()
    {
        var panel = new StackPanel();
        _motesSummary.Margin = new Thickness(0, 2, 0, 4);
        panel.Children.Add(_motesSummary);
        panel.Children.Add(_motesList);
        return panel;
    }

    private Control BuildSkyQuestSection()
    {
        var panel = new StackPanel();
        var bar = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        bar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        bar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        // Same state lens as the quest tracker (David, 2026-08-11): ready means every
        // piece collected and the turn-in still to make.
        ToolTip.SetTip(_skyStateCombo,
            "any · open (still missing items) · ready (every item collected — go turn it in) · done (turned in)");
        _skyStateCombo.SelectionChanged += OnSkyStateChanged;
        bar.Children.Add(_skyStateCombo);
        // #108 (bjstrange): find a dropped item without touring 14 class tabs. Search
        // crosses every class and ignores the state lens — filters shape tabs, never search.
        ToolTip.SetTip(_skySearchBox,
            "Type an item (or reward) name — every class's matching rows appear at once, "
            + "checkboxes included. Clear to go back to the tabs.");
        _skySearchBox.TextChanged += OnSkySearchChanged;
        Grid.SetColumn(_skySearchBox, 1);
        bar.Children.Add(_skySearchBox);
        panel.Children.Add(bar);
        _skySearchScroll.Content = _skySearchResults;
        panel.Children.Add(_skySearchScroll);
        _skyQuestTabs.SelectionChanged += OnSkyQuestTabChanged;
        panel.Children.Add(_skyQuestTabs);
        return panel;
    }

    private Control BuildGearSection()
    {
        var panel = new StackPanel();
        _gearListName.Margin = new Thickness(0, 2, 0, 4);
        _gearListName.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_gearListName);
        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 4, 0),
            Content = _gearChecklistPanel,
        });
        return panel;
    }

    private void AddSection(string sectionKey, string starKey, string title, TextBlock value, Control content, string tip)
    {
        var star = AppTheme.StarButton(starKey, tip);
        star.Click += OnStarChanged;
        _stars[starKey] = star;
        _sections[sectionKey] = AppTheme.Section(Header(title, value, star), content);
    }

    private static Grid Header(string title, TextBlock value, Button? star = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        if (star is not null) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.Children.Add(new TextBlock { Text = title, FontSize = 13, Foreground = AppTheme.TextBrush });
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        if (star is not null)
        {
            Grid.SetColumn(star, 2);
            grid.Children.Add(star);
        }
        return grid;
    }

    private Control BuildCombatSection()
    {
        var panel = new StackPanel();
        // Session-pace sparkline (2026-08-11): damage per minute over the last half
        // hour, area-filled, hottest minute marked. The data always existed
        // (_damageTimeline feeds History) — now it's drawn.
        ToolTip.SetTip(_combatSparkHost,
            "Damage per minute, last 30 minutes — the dot marks your hottest minute");
        _combatSparkHost.Children.Add(_combatSparkFill);
        _combatSparkHost.Children.Add(_combatSpark);
        _combatSparkHost.Children.Add(_combatSparkPeak);
        panel.Children.Add(_combatSparkHost);
        _combatFightText.Margin = new Thickness(0, 1, 0, 2);
        _combatFightBody.Children.Add(_combatFightText);
        _combatFightBody.Children.Add(_combatFightSplit);
        _combatFightBody.Children.Add(_combatFightOutLabel);
        _combatFightBody.Children.Add(_combatFightList);
        _combatFightInLabel.Margin = new Thickness(0, 2, 0, 0);
        _combatFightBody.Children.Add(_combatFightInLabel);
        _combatFightBody.Children.Add(_combatFightInList);
        _combatFightLabel.Click += (_, _) =>
            ToggleSubsection(v => _settings.ShowCombatFight = v, _settings.ShowCombatFight);
        _combatFightCopy.IsVisible = false;
        _combatFightCopy.Margin = new Thickness(6, 0, 0, 0);
        _combatFightCopy.Click += OnCopyFight;
        _combatFightTimeline.IsVisible = false;
        _combatFightTimeline.Margin = new Thickness(4, 0, 0, 0);
        _combatFightTimeline.Click += (_, _) => OpenFightTimeline();
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _combatFightLabel, _combatFightCopy, _combatFightTimeline },
        });
        panel.Children.Add(_combatFightBody);

        _combatSessionLabel.Click += (_, _) =>
            ToggleSubsection(v => _settings.ShowCombatSession = v, _settings.ShowCombatSession);
        panel.Children.Add(_combatSessionLabel);

        var body = _combatSessionBody;
        _combatSummary.Margin = new Thickness(0, 2, 0, 4);
        body.Children.Add(_combatSummary);
        body.Children.Add(SortHeader("Damage by attack", out _dmgOutSortTotal, out _dmgOutSortHits,
            out _dmgOutSortAvg, out _dmgOutSortDps, OnSortDmgOut, rateText: "dps"));
        body.Children.Add(_damageSourceList);
        var petHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _petAbilityLabel.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(_petAbilityLabel,
            "What your pet is using, split out of its Pet row above — click to expand");
        _petAbilityLabel.PointerPressed += OnPetAbilitiesToggled;
        petHeader.Children.Add(_petAbilityLabel);
        var petStar = AppTheme.StarButton("pet", "Show pet damage breakout when minimized");
        petStar.Click += OnStarChanged;
        _stars["pet"] = petStar;
        Grid.SetColumn(petStar, 1);
        petHeader.Children.Add(petStar);
        body.Children.Add(petHeader);
        body.Children.Add(_petAbilityList);
        body.Children.Add(SortHeader("Damage taken from", out _dmgInSortTotal, out _dmgInSortHits,
            out _dmgInSortAvg, out _, OnSortDmgIn));
        body.Children.Add(_damageTakenList);
        _recentFightsLabel.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(_recentFightsLabel);
        body.Children.Add(_recentFightsList);
        _areaSpellLabel.Margin = new Thickness(0, 6, 0, 0);
        _areaSpellLabel.IsVisible = false;
        body.Children.Add(_areaSpellLabel);
        body.Children.Add(_areaSpellList);
        // Procs per combat-minute (#85, Kerdude): same denominator as DPS, so
        // downtime doesn't flatter the weapon. The ⚡ star rides the label row —
        // one place to say what you watch when minimized (the pet-star rule).
        var procHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
        _procLabel.IsVisible = false;
        ToolTip.SetTip(_procLabel,
            "Spell damage that fired without a cast — weapon procs, poisons, item effects. "
            + "Rate is per minute of combat, the same denominator as DPS.");
        procHeader.Children.Add(_procLabel);
        var procStar = AppTheme.StarButton("procs", "Show proc rate (procs per combat-minute) in mini dashboard");
        procStar.Click += OnStarChanged;
        _stars["procs"] = procStar;
        Grid.SetColumn(procStar, 1);
        procHeader.Children.Add(procStar);
        body.Children.Add(procHeader);
        body.Children.Add(_procList);
        _stanceLabel.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(_stanceLabel);
        body.Children.Add(_stanceList);
        _invocationLabel.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(_invocationLabel);
        body.Children.Add(_invocationList);
        panel.Children.Add(body);
        return panel;
    }

    /// <summary>Each subsection remembers its own collapsed state — see AppSettings.</summary>
    private void ToggleSubsection(Action<bool> set, bool current)
    {
        set(!current);
        PersistSettings();
        RefreshUi();
    }

    private void OnPetAbilitiesToggled(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_petAbilityLabel).Properties.IsLeftButtonPressed) return;
        _settings.ShowPetAbilities = !_settings.ShowPetAbilities;
        PersistSettings();
        RefreshUi();
        e.Handled = true;
    }

    private void ApplySessionSubsections()
    {
        _combatSessionLabel.Content = (_settings.ShowCombatSession ? "v" : ">") + " Session so far";
        _combatSessionBody.IsVisible = _settings.ShowCombatSession;
        _healSessionLabel.Content = (_settings.ShowHealSession ? "v" : ">") + " Session so far";
        _healSessionBody.IsVisible = _settings.ShowHealSession;
    }

    private Control BuildHealingSection()
    {
        var panel = new StackPanel();
        _healFightText.Margin = new Thickness(0, 1, 0, 2);
        _healFightBody.Children.Add(_healFightText);
        _healFightBody.Children.Add(_healFightList);
        _healFightLabel.Click += (_, _) =>
            ToggleSubsection(v => _settings.ShowHealFight = v, _settings.ShowHealFight);
        panel.Children.Add(_healFightLabel);
        panel.Children.Add(_healFightBody);

        _healSessionLabel.Click += (_, _) =>
            ToggleSubsection(v => _settings.ShowHealSession = v, _settings.ShowHealSession);
        panel.Children.Add(_healSessionLabel);

        var body = _healSessionBody;
        _healingSummary.Margin = new Thickness(0, 2, 0, 4);
        body.Children.Add(_healingSummary);
        var sort = SortHeader("Heals cast", out _healSortTotal, out _healSortHits, out _healSortAvg,
            out _healSortHps, OnSortHeal, _healSpellsLabel, _healSortBar, "hps");
        body.Children.Add(sort);
        body.Children.Add(_healSpellList);
        body.Children.Add(_healersLabel);
        body.Children.Add(_healerList);
        panel.Children.Add(body);
        return panel;
    }

    private Control BuildKillsSection()
    {
        var panel = new StackPanel();
        _killsSummary.Margin = new Thickness(0, 2, 0, 4);
        panel.Children.Add(_killsSummary);
        panel.Children.Add(_killList);
        _farmingLabel.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(_farmingLabel);
        panel.Children.Add(_farmingList);
        _partyKillsLabel.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(_partyKillsLabel);
        panel.Children.Add(_partyKillList);
        return panel;
    }

    private Control BuildLootSection()
    {
        var panel = new StackPanel();
        panel.Children.Add(_lootList);
        _craftedLabel.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(_craftedLabel);
        panel.Children.Add(_craftedList);
        _targetDropsBlock.Children.Add(_targetDropsHeader);
        _targetDropsBlock.Children.Add(_targetDropsList);
        panel.Children.Add(_targetDropsBlock);
        return panel;
    }

    private Control BuildMoneySection()
    {
        var panel = new StackPanel();
        panel.Children.Add(_moneySummary);
        _soldLabel.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(_soldLabel);
        panel.Children.Add(_soldList);
        return panel;
    }

    private Control BuildProgressSection()
    {
        var panel = new StackPanel();
        _progressSummary.Margin = new Thickness(0, 2, 0, 4);
        panel.Children.Add(_progressSummary);
        panel.Children.Add(AppTheme.Heading("Skill-ups"));
        panel.Children.Add(_skillList);
        // Session-new AAs lead (Reddit, 2026-08-11); the full character ledger folds
        // behind the ▸ label, Pet-abilities style.
        _aaNewLabel.Margin = new Thickness(0, 4, 0, 0);
        _aaNewLabel.IsVisible = false;
        panel.Children.Add(_aaNewLabel);
        _aaNewList.IsVisible = false;
        panel.Children.Add(_aaNewList);
        _aaAbilitiesLabel.Margin = new Thickness(0, 4, 0, 0);
        _aaAbilitiesLabel.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(_aaAbilitiesLabel,
            "Everything the log's history (plus the durable ledger) says this character owns — "
            + "click to expand or fold");
        _aaAbilitiesLabel.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _settings.ShowAllAAs = !_settings.ShowAllAAs;
            _settings.Save();
            RefreshUi();
        };
        panel.Children.Add(_aaAbilitiesLabel);
        panel.Children.Add(_aaAbilityList);
        return panel;
    }

    private Control BuildMiscSection()
    {
        var panel = new StackPanel();
        panel.Children.Add(AppTheme.Heading("Deaths", AppTheme.BadBrush));
        panel.Children.Add(_deathList);
        panel.Children.Add(AppTheme.Heading("Zones visited"));
        panel.Children.Add(_zoneList);
        _markersLabel.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(_markersLabel);
        panel.Children.Add(_markerList);
        return panel;
    }

    private static Control SortHeader(string title, out TextBlock total, out TextBlock hits, out TextBlock avg,
        out TextBlock? rate, EventHandler<PointerPressedEventArgs> handler, TextBlock? titleBlock = null,
        StackPanel? sortBar = null, string? rateText = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.Children.Add(titleBlock ?? AppTheme.Heading(title));
        sortBar ??= new StackPanel { Orientation = Orientation.Horizontal };
        sortBar.HorizontalAlignment = HorizontalAlignment.Right;
        sortBar.Children.Add(AppTheme.DimText("sort:", new Thickness(0, 0, 4, 0)));
        total = SortLink("total", "total", handler, selected: true);
        var rateSubject = title.Contains("Heal", StringComparison.OrdinalIgnoreCase) ? "spell" : "ability";
        rate = rateText is null ? null : SortLink(rateText, "rate", handler,
            tip: $"Per-{rateSubject} {rateText}: that {rateSubject}'s total divided by total time in combat");
        hits = SortLink(title.Contains("Heal", StringComparison.OrdinalIgnoreCase) ? "casts" : "hits", "hits", handler);
        avg = SortLink("avg", "avg", handler);
        sortBar.Children.Add(total);
        if (rate is not null) sortBar.Children.Add(rate);
        sortBar.Children.Add(hits);
        sortBar.Children.Add(avg);
        Grid.SetColumn(sortBar, 1);
        grid.Children.Add(sortBar);
        return grid;
    }

    private static TextBlock SortLink(string text, string tag, EventHandler<PointerPressedEventArgs> handler,
        bool selected = false, string? tip = null)
    {
        var link = new TextBlock
        {
            Text = text,
            Tag = tag,
            FontSize = 10,
            Foreground = selected ? AppTheme.AccentBrush : AppTheme.DimBrush,
            Cursor = new Cursor(StandardCursorType.Hand),
            Margin = new Thickness(text == "total" ? 0 : 6, 0, 0, 0),
        };
        if (tip is not null) ToolTip.SetTip(link, tip);
        link.PointerPressed += handler;
        return link;
    }

    /// <summary>Reorganized 1.67.1 (David: the flat twenty-item list needed shelves like
    /// Options got). Windows lead — they're the daily drivers — then the two in-play
    /// actions, then submenus for data chores and help. Set-and-forget toggles (track
    /// spawns, grid overlay, cursor ring) moved to Options tabs; click-through stays,
    /// because flipping it mid-pull is what it's FOR.</summary>
    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        // A menu is a window of its own on macOS, and a raised main window outranks it —
        // the gear menu opens behind the widget. Opened fires with the popup already
        // realized, so correcting here beats waiting for the tick on something this brief.
        menu.Opened += (_, _) => EnsureOverlayLevel();

        static MenuItem Item(string header, EventHandler onClick, string? tip = null)
        {
            var item = new MenuItem { Header = header };
            if (tip is not null) ToolTip.SetTip(item, tip);
            item.Click += (s, _) => onClick(s, EventArgs.Empty);
            return item;
        }

        menu.Items.Add(Item("Options…", OnOptions,
            "Size, theme, alerts, watch rules, cards, hotkeys — now in tabs"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Zone map…", OnZoneMap,
            "Your zone's map with your last /log position — type /loc in game to update the marker"));
        menu.Items.Add(Item("Travel route…", OnTravelRoute,
            "Hop-by-hop directions from where you are to any zone"));
        menu.Items.Add(Item("Quest tracker…", OnQuestsWindow));
        menu.Items.Add(Item("Spawn timers…", (_, _) => ShowSpawnsWindow()));
        menu.Items.Add(Item("Inventory…", OnInventoryWindow,
            "Your worn gear by slot and what's in each bag — from the game's /outputfile inventory dump (type it in game, the file appears, EQBuddy reads it)"));
        menu.Items.Add(Item("Gear Locker…", OnGearLocker,
            "Everything wearable you own, grouped by slot and compared — items outclassed by something else in your bags get flagged as dump candidates. Your bags, not 'BiS'."));
        menu.Items.Add(Item("Session history…", OnHistory));
        menu.Items.Add(Item("Drops by creature…", OnDropsWindow));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Drop camp marker", (_, _) => DropCampMarker()));
        _clickThroughItem.Click += (_, _) => SetClickThrough(!_clickThrough);
        menu.Items.Add(_clickThroughItem);
        menu.Items.Add(new Separator());

        var data = new MenuItem { Header = "Data & imports" };
        data.Items.Add(Item("Import achievements…", OnImportAchievements,
            "Read the game's /outputfile achievements dump and pre-mark Sky quest rewards (and raid clears) you completed before EQBuddy — preview first, adds only, never unticks"));
        _reviewLogItem.Click += (s, _) => OnReviewLog(s, EventArgs.Empty);
        ToolTip.SetTip(_reviewLogItem,
            "Replay a saved log read-only — Drops by Creature and ✦ Copy for wiki work against that session");
        data.Items.Add(_reviewLogItem);
        _chooseLogFolderItem.Click += OnChooseLogFolder;
        data.Items.Add(_chooseLogFolderItem);
        data.Items.Add(Item("Auto-detect log folder", (_, _) =>
        {
            _settings.LogFolder = LogWatcher.FindDefaultLogFolder();
            _settings.Save();
            _lastCharScan = DateTime.MinValue;
            FollowActiveCharacter();
        }));
        menu.Items.Add(data);

        var help = new MenuItem { Header = "Help" };
        help.Items.Add(Item($"EQBuddy v{UpdateChecker.CurrentVersion}", OnOpenWebsite,
            "Open the EQBuddy page — downloads, guides, and a link to share"));
        help.Items.Add(Item("Quick tutorial…", OnTutorial));
        help.Items.Add(Item("Check for updates",
            (_, _) => { _lastUpdateCheck = DateTime.Now; CheckForUpdates(manual: true); }));
        help.Items.Add(Item("Send feedback…", (_, _) => new FeedbackWindow().Show(this)));
        menu.Items.Add(help);
        return menu;
    }

    // Where the window was placed at open and whether that was the SAVED spot —
    // OnClosed's PositionToPersist call needs both (#117: an unmoved fallback
    // placement must never overwrite a real saved position).
    private bool _restoredSavedPosition;
    private PixelPoint _placedPosition;

    private void RestorePosition()
    {
        // A spot saved on a monitor that's since gone would put the widget in the
        // void; keep the default position instead (parity with the WPF guard).
        _restoredSavedPosition = ScreenGuard.OnScreen(this, _settings.WindowLeft, _settings.WindowTop, Width, Height);
        if (_restoredSavedPosition)
            Position = new PixelPoint((int)_settings.WindowLeft, (int)_settings.WindowTop);
        // The fallback spot is whatever the WM hands us — record it once the window
        // is real, so a session that never moved it can be told apart from a drag.
        Opened += (_, _) => _placedPosition = Position;
        _placedPosition = Position;
    }

    private void ApplyUiScale(double scale)
    {
        _scaleRoot.LayoutTransform = Math.Abs(scale - 1.0) < 0.001 ? null : new ScaleTransform(scale, scale);
        UpdateWindowHeightLimit();
        _scaleRoot.InvalidateMeasure();
        InvalidateMeasure();
    }

    private void UpdateWindowHeightLimit()
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen is null) return;

        var workingHeight = screen.WorkingArea.Height / screen.Scaling;
        MaxHeight = Math.Max(240, workingHeight - 20);

        // The section list sits inside the scaled widget. Reserve room for the title,
        // status/session lines, borders, and a little work-area breathing room.
        var scale = Math.Max(0.5, _settings.UiScale);
        ApplySectionMaxHeight(Math.Max(160, (workingHeight - 160) / scale));
    }

    /// <summary>The section list's height: automatic (fit the monitor) unless the
    /// bottom-edge grip chose one (Reddit ask, 2026-08-09 — taller or shorter without
    /// rescaling text). The choice lives in pre-scale units so it survives scale
    /// changes; the monitor's cap always wins.</summary>
    private double _sectionAutoCap = double.MaxValue;

    private void ApplySectionMaxHeight(double? autoCap = null)
    {
        if (autoCap is { } cap) _sectionAutoCap = cap;
        _sectionScroll.MaxHeight = double.IsNaN(_settings.ContentHeight)
            ? _sectionAutoCap
            : Math.Clamp(_settings.ContentHeight, 120, _sectionAutoCap);
    }

    /// <summary>CPU% (share of ALL cores, so 100% = the whole machine) and working
    /// set, sampled every 3 s from the process's own counters — cheap enough that
    /// measuring the app doesn't meaningfully show up in the measurement. Off by
    /// default; the label collapses without leaving a gap (#112).</summary>
    private readonly Process _self = Process.GetCurrentProcess();
    private DateTime _perfSampledAt;
    private TimeSpan _perfCpuAt;

    private void UpdatePerfStats()
    {
        if (!_settings.ShowPerfStats)
        {
            if (_perfLabel.IsVisible) _perfLabel.IsVisible = false;
            return;
        }
        var now = DateTime.UtcNow;
        if ((now - _perfSampledAt).TotalSeconds < 3) return;
        try
        {
            _self.Refresh();
            var cpu = _self.TotalProcessorTime;
            if (_perfSampledAt != default)
            {
                var pct = (cpu - _perfCpuAt).TotalMilliseconds
                          / ((now - _perfSampledAt).TotalMilliseconds * Environment.ProcessorCount) * 100;
                _perfLabel.Text = $"{Math.Max(0, pct):0.0}% · {_self.WorkingSet64 / (1024.0 * 1024.0):0} MB";
                _perfLabel.IsVisible = true;
            }
            _perfSampledAt = now;
            _perfCpuAt = cpu;
        }
        catch (Exception ex) { App.LogError(ex); _settings.ShowPerfStats = false; }
    }

    /// <summary>The one-line body of a card that has nothing yet — the card stays
    /// where Options put it (David's verdict: "show what I've selected to see"),
    /// and the line says what will fill it.</summary>
    private static TextBlock EmptyCardLine(string text) => new()
    {
        Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 2),
        Foreground = AppTheme.DimBrush,
    };

    private void ApplyBackgroundOpacity(double opacity) => _root.Background = AppTheme.BgWithOpacity(opacity);

    /// <summary>Re-applies visual state that AppTheme.Apply's brush mutation can't reach
    /// on its own: BgWithOpacity returns a fresh, non-live brush each call, and stat rows
    /// built from AccentBarBrush() bake in a color snapshot rather than a live reference.
    /// Everything else (borders, banners, headings) repaints on its own because it holds
    /// a reference to the same AppTheme brush instance that just got mutated.</summary>
    public void RefreshTheme()
    {
        ApplyBackgroundOpacity(_settings.BackgroundOpacity);
        RefreshUi();
    }

    private async void OnChooseLogFolder(object? sender, EventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pick the EverQuest Legends Logs folder",
            AllowMultiple = false,
        });
        var picked = folders.FirstOrDefault()?.TryGetLocalPath();
        if (picked is null) return;
        var logsSub = System.IO.Path.Combine(picked, "Logs");
        if (!Directory.EnumerateFiles(picked, "eqlog_*.txt").Any() && Directory.Exists(logsSub))
            picked = logsSub;
        _settings.LogFolder = picked;
        _settings.Save();
        _lastCharScan = DateTime.MinValue;
        FollowActiveCharacter();
    }

    // ---- archived-log review (#74, Snagglefern: "see what I can contribute") ----

    /// <summary>Path of the archive being replayed; null = live. While set, character
    /// follow stands down and nothing writes to session history — the review is a
    /// window onto the past, not a new session.</summary>
    private string? _reviewPath;

    private async void OnReviewLog(object? sender, EventArgs e)
    {
        if (_reviewPath is not null) { ExitReview(); return; }
        var archive = _settings.LogFolder is { } lf ? System.IO.Path.Combine(lf, "archive") : null;
        var startIn = archive is not null && Directory.Exists(archive)
            ? archive : _settings.LogFolder;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Review an archived log",
            AllowMultiple = false,
            SuggestedStartLocation = startIn is null
                ? null
                : await StorageProvider.TryGetFolderFromPathAsync(startIn),
            FileTypeFilter =
            [
                new FilePickerFileType("EQ logs (eqlog_*.txt)") { Patterns = ["eqlog_*.txt"] },
                FilePickerFileTypes.All,
            ],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } picked)
            await EnterReview(picked);
    }

    private async Task EnterReview(string path)
    {
        // A pre-splitter log holds days of sessions; ask which one (#74 round two —
        // Snagglefern's 10 MB archive replayed as a 10-minute evening). Splitter
        // archives are one session each, so they skip the dialog entirely.
        List<LogSessionInfo> sessions;
        try { sessions = LogSessions.Scan(path); }
        catch (Exception ex) { App.LogError(ex); sessions = []; }
        LogSessionInfo? pick = null;
        if (sessions.Count > 1)
        {
            // Debug/screenshot hook: 1-based chronological index skips the dialog.
            pick = int.TryParse(Environment.GetEnvironmentVariable("EQBUDDY_REVIEW_SESSION"),
                    out var idx) && idx >= 1 && idx <= sessions.Count
                ? sessions[idx - 1]
                : await SessionPickerWindow.Choose(this, System.IO.Path.GetFileName(path), sessions);
            if (pick is null) return;   // cancelled
        }

        // The live session goes to history first, same as a character switch —
        // then the archiver stands down until we're back.
        _archiver.FinalizeActive(_stats.Snapshot(), "ReviewingArchive");
        _reviewPath = path;
        _targetResults.Clear();
        _skyQuestLootSeen.Clear();
        if (pick is not null) _watcher.Select(path, pick.StartOffset, pick.EndOffset);
        else _watcher.Select(path);
        _reviewLogItem.Header = "✓ Reviewing an archive — return to live log";
        var when = pick is not null ? $" ({pick.Start:MMM d HH:mm})" : "";
        _charLabel.Text = $"REVIEWING {System.IO.Path.GetFileName(path)}{when} — click here to go live";
        _charLabel.Foreground = AppTheme.WarnBrush;
        _charLabel.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(_charLabel, "Replaying a saved log. Drops by Creature and ✦ Copy for wiki " +
            "show the reviewed session. Click to return to the live log.");
    }

    private void ExitReview()
    {
        _reviewPath = null;
        _reviewLogItem.Header = "Review an archived log…";
        _charLabel.Foreground = AppTheme.DimBrush;
        _charLabel.Cursor = Cursor.Default;
        ToolTip.SetTip(_charLabel, "Follows whoever is actively playing (log file growth)");
        // No finalize here: the reviewed session is already history. Follow just
        // re-selects whoever is live; the switch path sees review's CurrentPath but
        // _reviewPath is null again, so guard by handing follow a clean slate.
        _lastCharScan = DateTime.MinValue;
        if (_settings.LogFolder is { } lf && LogWatcher.MostRecentlyActive(lf) is { } active)
        {
            _watcher.Select(active.FilePath);
            _archiver.SetIdentity(_stats.ServerName, _stats.CharacterName);
            _charLabel.Text = active.Display;
        }
        else
        {
            _charLabel.Text = "waiting for a character to log in...";
        }
    }

    // Pointer DOWN, and handled: the title bar's OnDrag starts a move-drag on the same
    // press, which captures the pointer and eats any up-event this label would get.
    private void OnCharLabelClick(object? sender, PointerPressedEventArgs e)
    {
        if (_reviewPath is null) return;
        if (!e.GetCurrentPoint(_charLabel).Properties.IsLeftButtonPressed) return;
        ExitReview();
        e.Handled = true;
    }

    private void FollowActiveCharacter()
    {
        if (_reviewPath is not null) return;   // reviewing an archive — stay put (#74)
        ToolTip.SetTip(_chooseLogFolderItem, _settings.LogFolder ?? "(no folder found)");
        if (_settings.LogFolder is null)
        {
            _charLabel.Text = "logs not found - right-click, Choose log folder";
            return;
        }
        var active = LogWatcher.MostRecentlyActive(_settings.LogFolder);
        if (active is null)
        {
            _charLabel.Text = "waiting for a character to log in...";
            return;
        }
        if (!string.Equals(active.FilePath, _watcher.CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            if (_watcher.CurrentPath is not null)
                _archiver.FinalizeActive(CurrentSnapshot(), "CharacterChanged");
            _watcher.Select(active.FilePath);
            _archiver.SetIdentity(_stats.ServerName, _stats.CharacterName);
            _charLabel.Text = active.Display;
            // Perf audit #9: these were session-lifetime by intent but PROCESS-lifetime
            // in fact — with review mode switching logs freely now, clear them with the
            // rest of the character state.
            _targetResults.Clear();
            _skyQuestLootSeen.Clear();
        }
    }

    /// <summary>A fresh stats snapshot, for the cards and for windows that refresh on
    /// their own cadence (the host-interface member the satellite windows call).</summary>
    public StatsSnapshot CurrentSnapshot() =>
        _stats.Snapshot(TimeSpan.FromMinutes(Math.Max(1, _settings.RecentWindowMinutes)),
            _settings.TrackedRules);

    /// <summary>The 🗺 badge signal: a known quest's turn-in OR a member of the wiki's
    /// Quest Items category. When known quests want the item and ALL are dismissed, the
    /// badge goes too. Third source, from #75: the item page's own "QUEST ITEM" stats
    /// flag — cache-only on purpose, so it costs nothing before a lookup.</summary>
    public bool IsActiveQuestItem(string name)
    {
        var wanting = QuestCatalog.QuestsWanting(name);
        if (wanting.Count == 0)
            return QuestCatalog.IsQuestItem(name)
                || _wikiItems.CachedInfo(name) is { QuestFlagged: true };
        var hidden = QuestLedger?.HiddenFor(QuestCharacterKey);
        return hidden is not { Count: > 0 } || wanting.Any(q => !hidden.Contains(q.Name));
    }

    /// <summary>Badge click, one behavior everywhere: quests we can name open in the
    /// Quest Tracker; a category-only item opens its own wiki page, where the quest
    /// that wants it is documented.</summary>
    public void OpenQuestInfoForItem(string itemName)
    {
        var baseName = QuestCatalog.BaseItemName(itemName);
        if (QuestCatalog.QuestsWanting(baseName).Count > 0) ShowQuestsWindow(baseName);
        else OpenWikiPage(baseName);
    }

    /// <summary>Prefix an item tooltip with the quest marker so the badge explains itself.</summary>
    internal string? QuestAwareTooltip(string name, string? baseTip)
    {
        if (!IsActiveQuestItem(name)) return baseTip;
        const string marker = "🗺 Part of a quest — click the 🗺 to see its quests in the Quest Tracker.";
        return baseTip is { Length: > 0 } ? marker + "\n" + baseTip : marker;
    }

    /// <summary>Hover stats for an item row: the cached wiki stat block when we have one
    /// (any age — a hover is a peek, not a lookup), else a hint that clicking fetches.</summary>
    internal string ItemHoverStats(string itemName) =>
        _wikiItems.CachedStatsText(itemName) ?? "Click for item info (eqlwiki)";

    /// <summary>Raw cached stats (null when the cache is empty) — the tooltip surfaces
    /// that want the real distinction so they know to fetch.</summary>
    public string? CachedItemStats(string itemName) => _wikiItems.CachedStatsText(itemName);

    /// <summary>Full tooltip text for an item, FETCHING from the wiki when the cache is
    /// empty. One bounded lookup, cached for a week.</summary>
    public async Task<string?> FetchItemTooltip(string name)
    {
        var r = await _wikiItems.LookupAsync(name);
        return r.Item is { StatsLines.Count: > 0 } info
            ? string.Join("\n", info.StatsLines)
            : null;
    }

    /// <summary>Open an item's eqlwiki page in the default browser — the search URL
    /// lands on the page itself on an exact title match (MediaWiki "Go"), and on
    /// search results otherwise, so a rename never strands the user on a 404.</summary>
    internal static void OpenWikiPage(string itemName)
    {
        var url = "https://eqlwiki.com/index.php?search="
            + Uri.EscapeDataString(EqlWikiItemService.NormalizeTitle(itemName));
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    /// <summary>DropsWindow's window into the target-drops memo (WIKI-NEW, #65):
    /// reuses the same lookups and cache the Loot card fires.</summary>
    public MobLookupResult? WikiMobResult(string name) =>
        _targetResults.GetValueOrDefault(name);

    public void EnsureMobLookup(string name)
    {
        if (_targetResults.ContainsKey(name)) return;
        _targetResults[name] = null;
        _ = LookupTargetAsync(name, CurrentZoneName);
    }

    // ---- inventory (/outputfile inventory) ----

    private InventoryFile.Snapshot? _inventory;

    /// <summary>The newest inventory dump for the followed character, adjusted by what
    /// the log has seen since it was written (loot in, sells out); the dump itself is
    /// memoized, the log overlay is always current. Pass refresh to re-scan the game
    /// folder (the ⟳ button, the held tab).</summary>
    public InventoryFile.Snapshot? LatestInventory(bool refresh = false)
    {
        if (refresh || _inventory is null)
            _inventory = InventoryFile.FindLatest(_settings.LogFolder, Identity.Character);
        return _inventory?.WithChanges(_stats.ItemsGainedSince(_inventory.WrittenAt));
    }

    /// <summary>Recent raw log messages for the Options "rule from a recent line" picker.</summary>
    internal List<(DateTime Time, string Message)> RecentLogLines() => _stats.RecentLines();

    private void RefreshUi()
    {
        EnsureOverlayLevel();
        UpdateFocusHide();
        ReassertTopmost();
        _stats.RegenPerTickOverride = _settings.RegenPerTickOverride;

        // Spawn timers crossing zero: banner always, sound only if one is chosen. Runs
        // off the shared tick so a hidden window can't silence a camp.
        if (_questsWindow is { IsVisible: true } qw) qw.MaybeRefresh();
        if (_dropsWindow is { IsVisible: true } dw) dw.MaybeRefresh();
        if (_mapWindow is { IsVisible: true } mapw) mapw.MaybeRefresh();

        if (_settings.TrackSpawns)
        {
            // Sound only: the chip changing to DUE is already the visual notification.
            foreach (var due in _spawnsVm.ConsumeDueAlerts(DateTime.Now))
                if (_spawnsVm.SoundFor(due.Zone, due.Name) is { } sound)
                    PlayAlertSound(sound);

            // Chips are the ambient face and stay visible alongside the full browser.
            if (!_hiddenForFocus && _spawnsVm.HasActiveTimers(DateTime.Now))
            {
                // A null field is the "window truly gone" signal (its Closed handler
                // nulls it) — the Avalonia stand-in for WPF's `is not { IsLoaded: true }`,
                // because Show() on a closed Avalonia window throws. A hidden-but-open
                // stack (the toggleAll hotkey) is reused, never re-Shown as a duplicate,
                // and nothing new pops up while the player asked everything to hide.
                if (_spawnChipsWindow is null && !_hotkeyHidden)
                {
                    var chips = new SpawnChipsWindow(this, _spawnsVm, SetChipScale);
                    chips.Closed += (_, _) =>
                    {
                        if (ReferenceEquals(_spawnChipsWindow, chips)) _spawnChipsWindow = null;
                    };
                    _spawnChipsWindow = chips;
                    chips.Show(this);
                }
                _spawnChipsWindow?.RefreshChips(DateTime.Now);
            }
            else
                CloseSpawnChips();
        }
        else
            CloseSpawnChips();

        // The fight-side chip stack lives its own life, independent of spawn tracking:
        // mez chips park next to the fight, spawn chips are ambient. Optional since the
        // 2026-08-11 Reddit ask — a non-CC class never wants the stack. Slow chips (#94)
        // ride the same stack: both are "active effect, counting down, parked next to
        // the fight", and one window means one saved position. Emptiness is probed
        // cheaply first — building the full chip list twice a second to learn it was
        // empty was pure churn.
        var chipsNow = DateTime.Now;
        // Options open = placement preview: the stack exists (with a placeholder if
        // empty) so it can be parked before the first real debuff (#94 follow-up).
        var chipPlacement = _optionsWindow is { IsVisible: true }
            && (_settings.MezChipsEnabled || _settings.SlowAlertEnabled);
        var haveFightChips = !_hiddenForFocus
            && (chipPlacement
                || (_settings.MezChipsEnabled && _mezTracker.Any(chipsNow))
                || (SlowChipsVisible(chipsNow) && _slowTracker.Any(chipsNow)));
        if (haveFightChips)
        {
            // Same lifecycle contract as the spawn stack above: null = gone, non-null
            // hidden = reuse without re-Show, and no new window mid-toggleAll.
            if (_mezChipsWindow is null && !_hotkeyHidden)
            {
                var chips = new MezChipsWindow(_settings, FightChips, SetChipScale);
                chips.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_mezChipsWindow, chips)) _mezChipsWindow = null;
                };
                _mezChipsWindow = chips;
                chips.Show(this);
            }
            _mezChipsWindow?.RefreshChips(DateTime.Now);
        }
        else
            CloseMezChips();

        if (DateTime.Now - _lastCharScan > TimeSpan.FromSeconds(5))
        {
            _lastCharScan = DateTime.Now;
            FollowActiveCharacter();
        }
        if (DateTime.Now - _lastUpdateCheck > TimeSpan.FromHours(6))
        {
            _lastUpdateCheck = DateTime.Now;
            CheckForUpdates(manual: false);
        }
        if (_settings.LogFolder is { } folder && DateTime.Now - _lastJanitorRun > TimeSpan.FromMinutes(10))
        {
            _lastJanitorRun = DateTime.Now;
            var prune = _settings.TruncateLogs && !_settings.ShowTutorial;
            var archive = _settings.ArchiveLogs;
            Task.Run(() =>
            {
                EqConfig.EnsureLoggingEnabled(folder);
                if (prune) EqConfig.TruncateStaleLogs(folder, SessionStats.SessionGap, archive: archive);
            });
        }

        UpdateLoggingStatus();
        if (_upToDateNoticeUntil != DateTime.MinValue && DateTime.Now > _upToDateNoticeUntil && _pendingUpdate is null && !_installingUpdate)
        {
            _updateBanner.IsVisible = false;
            _upToDateNoticeUntil = DateTime.MinValue;
        }
        if (_watcher.LastError is { } err) App.LogError(err);

        var s = CurrentSnapshot();
        ProcessTrackedAlerts(s);
        // Every 5 min: checkpoint the active session so a crash loses little.
        // Review replays are read-only — their sessions are already history (#74).
        if (_reviewPath is null && DateTime.Now - _lastCheckpoint > TimeSpan.FromMinutes(5))
        {
            _lastCheckpoint = DateTime.Now;
            _archiver.Checkpoint(s);
        }
        if (_miniRoot.IsVisible) UpdateMiniChips(s);
        UpdateBreakouts(s);

        // Hidden while the game is unfocused: everything the player can't see stops
        // here — alerts, chips, timers, and checkpoints above already ran (perf
        // audit #1b: the full element rebuild used to run every second into a
        // window that wasn't even shown).
        if (_hiddenForFocus) return;

        _zoneText.Text = s.CurrentZone.Length > 0 ? s.CurrentZone : "-";
        CurrentZoneName = s.CurrentZone;
        var active = TimeSpan.FromSeconds(s.ActiveSeconds);
        _sessionText.Text = s.SessionStart is { } start
            ? $"session {(int)s.Elapsed.TotalHours}:{s.Elapsed.Minutes:D2} - active {(int)active.TotalMinutes}m (since {start:h:mm tt})"
            : "waiting for log activity...";
        UpdateHeadlines(s);
        ApplySessionSubsections();

        // Perf audit #1: identical content was re-rendered every tick. Expanded
        // sections now rebuild only when an event actually arrived; a 10 s heartbeat
        // keeps time-derived rates (xp/hr, coin/hr, recent-window dps) honest during
        // long AFKs. Headers/chips/alerts above stay per-tick; so do the Watch card
        // (live cue countdowns) and Buffs (the countdowns ARE the content).
        var fullRender = s.Version != _lastRenderedVersion ||
                         DateTime.Now - _lastFullRender > TimeSpan.FromSeconds(10);
        if (fullRender)
        {
            _lastRenderedVersion = s.Version;
            _lastFullRender = DateTime.Now;
            RefreshExpandedSections(s);
        }
        RenderTracked(s);   // per-tick: live ⏳ cue countdowns and "last: … ago" ages
        RenderBuffs();      // per-tick: the countdowns ARE the content
        if (fullRender) RenderRaids();   // changes on kills and imports only
        UpdatePerfStats();  // #112: self-measurement, every few seconds, off by default
    }

    /// <summary>Card headers plus the KPI strip — always painted, every tick, even
    /// while the full-render gate keeps card bodies quiet.</summary>
    private void UpdateHeadlines(StatsSnapshot s)
    {
        _combatHeader.Text = s.CurrentDps > 0 ? $"{s.SessionDps:0} dps (now {s.CurrentDps:0})" : $"{s.SessionDps:0} dps";
        // KPI strip (2026-08-11): the headline numbers, always painted — current DPS
        // while fighting, session DPS between fights.
        _kpiDps.Text = s.CurrentDps > 0 ? $"{s.CurrentDps:0}" : $"{s.SessionDps:0}";
        _kpiKills.Text = $"{s.YourKillCount}";
        _kpiLoot.Text = $"{s.LootTotal}";
        _kpiXp.Text = $"{s.XpPerHour:0.#}%";
        _killsHeader.Text = s.PartyKillCount > 0 ? $"{s.YourKillCount} (+{s.PartyKillCount})" : $"{s.YourKillCount}";
        _lootHeader.Text = s.CraftedTotal > 0 ? $"{s.LootTotal} items (+{s.CraftedTotal} made)" : $"{s.LootTotal} item{(s.LootTotal == 1 ? "" : "s")}";
        var motes = Motes.Summarize(s.Loot, s.Elapsed);
        _motesHeader.Text = motes.Total > 0 ? $"{motes.Total} · {motes.PerHour:0.#}/hr" : "0";
        UpdateSkyQuestChecklist(s);
        UpdateGearHeaderOnly();
        _moneyHeader.Text = StatsSnapshot.FormatCoin(s.Copper);
        _progressHeader.Text = $"{s.XpPercent:0.0}% xp" + (s.Levels.Count > 0 ? $", +{s.Levels.Count} lvl" : "") + (s.AaGained > 0 ? $", +{s.AaGained} aa" : "");
        _factionHeader.Text = s.Faction.Count > 0 ? $"{s.Faction.Count} factions" : "-";
        _miscHeader.Text = $"{s.Deaths.Count} death{(s.Deaths.Count == 1 ? "" : "s")}";
    }

    /// <summary>Paint a snapshot into the cards, without the timer-driven housekeeping
    /// RefreshUi also does (character rescan, update check, log janitor). Exists so the
    /// headless render tests can exercise the code path every refresh takes — which is where
    /// a card that mis-formats or dereferences null actually breaks — without a log folder,
    /// a network, or a five-second wait.</summary>
    internal void RenderSnapshotForTest(StatsSnapshot s,
        IReadOnlyDictionary<string, DateTime>? dueByRule = null)
    {
        UpdateHeadlines(s);
        ApplySessionSubsections();
        RefreshExpandedSections(s);
        RenderTracked(s, dueByRule);
        RenderBuffs();
        RenderRaids();
    }

    private void RefreshExpandedSections(StatsSnapshot s)
    {
        RefreshOptionalSectionVisibility(s);

        if (_sections["combat"].IsExpanded)
        {
            var acc = s.HitCount + s.MissCount > 0 ? (double)s.HitCount / (s.HitCount + s.MissCount) * 100 : 0;
            var critRate = s.HitCount > 0 ? (double)s.CritCount / s.HitCount * 100 : 0;
            var incomingSwings = s.AvoidedIncoming + s.MeleeHitsTaken;
            var avoidance = incomingSwings > 0 ? (double)s.AvoidedIncoming / incomingSwings * 100 : 0;
            var combatTime = TimeSpan.FromSeconds(s.CombatSeconds);
            ShowLastFight(s, _combatFightLabel, _combatFightBody, _combatFightText,
                _combatFightList, healing: false, _settings.ShowCombatFight);
            _combatFightCopy.IsVisible = s.LastFight is not null;
            _combatFightTimeline.IsVisible = _combatFightCopy.IsVisible;
            _combatSummary.Text =
                $"Dealt {s.DamageDealt:N0} ({s.MeleeDamage:N0} melee / {s.SpellDamage:N0} spell)\n" +
                $"{s.CritCount} crits ({critRate:0.#}% rate) - {acc:0}% accuracy\n" +
                $"In combat {(int)combatTime.TotalMinutes}m {combatTime.Seconds}s this session\n" +
                // Both DPS models, labeled (Companion-parity ask): in-combat is the
                // honest camp number (medding doesn't dilute it), wall-clock is what a
                // raid night actually produced. Neither is "the" DPS; say which is which.
                (s.SessionDps > 0 && s.SessionStart is { } ss0 && s.LastEventTime is { } le0
                    ? $"Session dps: {s.SessionDps:0.#} in combat - " +
                      $"{s.DamageDealt / Math.Max(1, (le0 - ss0).TotalSeconds):0.#} wall-clock\n"
                    : "") +
                (s.Recent is { } rc ? $"Last {(int)rc.Window.TotalMinutes}m: {rc.Dps:0.#} dps{(rc.HasFullWindow ? "" : " (partial window)")}\n" : "") +
                $"Biggest hit: {s.MaxHit:N0} ({s.MaxHitDesc})\n" +
                $"Taken {s.DamageTaken:N0} - avoided {s.AvoidedIncoming} of {incomingSwings} melee attacks ({avoidance:0}%)" +
                (s.SpecialHits.Count > 0 ? "\n" + string.Join(" - ", s.SpecialHits.Select(x => $"{x.Name} {x.Count}")) : "") +
                (s.DotDamage + s.DirectSpellDamage > 0
                    ? $"\nYour spells: {s.DotDamage:N0} over time / {s.DirectSpellDamage:N0} direct"
                    : "") +
                (s.CastCompletion is { } completion
                    ? $"\nCasts {s.CastsStarted} · {completion * 100:0}% completed" +
                      $" ({s.CastsInterrupted} interrupted · {s.Fizzles} fizzled · {s.Resists} resisted)"
                    : s.Fizzles + s.Resists > 0 ? $"\nFizzles {s.Fizzles} - resists {s.Resists}" : "") +
                (s.CurrentStance.Length > 0 ? $"\nStance: {s.CurrentStance}" : "");
            PaintCombatSpark(s);
            FillBreakdown(_damageSourceList, s.DamageBySource, _dmgOutSort, s.CombatSeconds, "dps",
                SpellResistLookup(s));
            // Shares the damage sort bar above it — it's the same rows, one level down.
            // The overall Pet row is already visible above, so keep this potentially long
            // per-ability list folded until the player asks for it.
            _petAbilityLabel.IsVisible = s.PetAbilities.Count > 0;
            _petAbilityLabel.Text = _settings.ShowPetAbilities
                ? "▾ Pet abilities"
                : $"▸ Pet abilities ({s.PetAbilities.Count})";
            _petAbilityList.IsVisible = _settings.ShowPetAbilities && s.PetAbilities.Count > 0;
            if (_petAbilityList.IsVisible)
                FillBreakdown(_petAbilityList, s.PetAbilities, _dmgOutSort, s.CombatSeconds, "dps");
            FillStatList(_damageTakenList, s.DamageByAttacker, _dmgInSort, "hit");
            _recentFightsLabel.IsVisible = s.RecentEncounters.Count > 0;
            var topFightDps = Math.Max(0.1, s.RecentEncounters.Count > 0
                ? s.RecentEncounters.Max(f => f.Dps)
                : 0);
            var fightBrush = AccentBarBrush();
            _recentFightsList.ItemsSource = s.RecentEncounters.Select(f => BarRow(f.Name,
                $"{f.DurationSeconds:0}s - {f.Dps:0.#} dps{(f.Outcome == "Timeout" ? " - ?" : "")}",
                f.Dps / topFightDps, fightBrush,
                $"{f.DamageOut:N0} damage over {f.DurationSeconds:0}s")).ToList();
            // Per cast, not per target: one cast's total damage is the useful comparison
            // when deciding whether an area spell is worthwhile for the pull size.
            _areaSpellLabel.IsVisible = s.AreaSpells.Count > 0;
            FillList(_areaSpellList, s.AreaSpells.Select(x =>
                (x.Name, $"{x.DamagePerCast:N0}/cast - x{x.Casts} - {x.AvgTargets:0.#} targets" +
                         (x.MaxTargets > x.AvgTargets + 0.05 ? $" (best {x.MaxTargets})" : ""))));
            // Procs per combat-minute (#85, Kerdude): same denominator as DPS, so
            // downtime doesn't flatter the weapon.
            _procLabel.IsVisible = s.Procs.Count > 0;
            var combatMinutes = Math.Max(1.0 / 60, s.CombatSeconds / 60.0);
            FillList(_procList, s.Procs.Select(x =>
                (x.Name, $"x{x.Count} - {x.Damage:N0} dmg - {x.Count / combatMinutes:0.#}/min")));
            _stanceLabel.IsVisible = s.Stances.Count > 0;
            FillList(_stanceList, s.Stances.Select(x =>
                (x.Name, $"{x.Damage:N0} dmg - {(int)x.CombatSeconds}s - {x.Dps:0.#} dps")));
            _invocationLabel.IsVisible = s.Invocations.Count > 0;
            FillList(_invocationList, s.Invocations.Select(x =>
                (x.Name, $"{x.Damage:N0} dmg - {(int)x.CombatSeconds}s - {x.Dps:0.#} dps")));
        }
        _healingHeader.Text = s.Hps > 0 ? $"{s.Hps:0.#} hps" : $"{s.HealingDone:N0} healed";
        if (_sections["healing"].IsExpanded)
        {
            ShowLastFight(s, _healFightLabel, _healFightBody, _healFightText,
                _healFightList, healing: true, _settings.ShowHealFight);
            _healingSummary.Text = $"Done {s.HealingDone:N0} - received {s.HealingReceived:N0}" +
                (s.Recent is { Hps: > 0 } rh ? $"\nLast {(int)rh.Window.TotalMinutes}m: {rh.Hps:0.#} hps" : "") +
                (s.RegenTicks > 0 ? $"\n{s.RegenTicks} regen/hymn ticks (game logs no amounts for these)" : "") +
                (s.RuneBlockCount > 0
                    ? $"\nRune absorbed {s.RuneBlockCount} hit{(s.RuneBlockCount == 1 ? "" : "s")}" +
                      $" (best streak {s.RuneBlockStreakMax}" +
                      (s.RuneBlockStreak > 0 ? $", current {s.RuneBlockStreak}" : "") + ")"
                    : "");
            var showSpells = s.HealsBySpell.Count > 0;
            _healSpellsLabel.IsVisible = showSpells;
            _healSortBar.IsVisible = showSpells;
            FillBreakdown(_healSpellList, s.HealsBySpell, _healSort, s.CombatSeconds, "hps");
            _healersLabel.IsVisible = s.HealsByHealer.Count > 0;
            FillList(_healerList, s.HealsByHealer.Select(h => (h.Name, $"{h.Total:N0} - {h.Hits} heal{(h.Hits == 1 ? "" : "s")}")));
        }
        if (_sections["kills"].IsExpanded)
        {
            _killsSummary.Text = $"{s.KillsPerHour:0.0} kills/hr - {s.KillsPerActiveHour:0.0} active" +
                (s.Recent is { } rk ? $" - last {(int)rk.Window.TotalMinutes}m: {rk.Kills}" : "");
            FillList(_killList, s.YourKills.Select(k => (k.Name, $"x{k.Count}")));
            var farmed = s.Mobs.Where(m => m.Kills > 0).ToList();
            _farmingLabel.IsVisible = farmed.Count > 0;
            var farmRows = new List<(string, string)>();
            foreach (var m in farmed)
            {
                farmRows.Add((m.Name,
                    $"avg {m.AvgFightSeconds:0}s - {StatsSnapshot.FormatCoin(m.Copper)} - {m.XpPercent:0.0}% xp"));
                foreach (var l in m.Loot)
                    farmRows.Add(($"      {l.Item}", l.DropRatePct is { } pct ? $"x{l.Count} - {pct:0}%" : $"x{l.Count}"));
            }
            FillList(_farmingList, farmRows);
            _partyKillsLabel.IsVisible = s.PartyKillsByKiller.Count > 0;
            FillList(_partyKillList, s.PartyKillsByKiller.Select(k => (k.Name, $"x{k.Count}")));
        }
        if (_sections["loot"].IsExpanded)
        {
            FillList(_lootList, s.Loot.Select(l => (l.Item, $"x{l.Count}")),
                onNameClick: ShowItemInfo,
                tooltip: n => QuestAwareTooltip(n, ItemHoverStats(n)), questBadges: true);
            _craftedLabel.IsVisible = s.Crafted.Count > 0;
            FillList(_craftedList, s.Crafted.Select(c => (c.Name, $"x{c.Count}")));
            RenderTargetDrops(s);
        }
        if (_sections["motes"].IsExpanded)
        {
            var motes = Motes.Summarize(s.Loot, s.Elapsed);
            _motesSummary.Text = motes.Total > 0
                ? $"{motes.PerHour:0.#} motes/hr this session"
                : "No motes yet this session — every Mote of … Potential you loot " +
                  "(or store as currency) lands here.";
            FillList(_motesList, motes.Tiers.Select(t => (t.Item, $"x{t.Count}")),
                onNameClick: ShowItemInfo, tooltip: ItemHoverStats);
        }
        if (_sections["sky"].IsExpanded && _skyQuestDirty)
        {
            RenderSkyQuestChecklist();
            _skyQuestDirty = false;
        }
        if (_sections["gear"].IsExpanded && _gearChecklistDirty)
        {
            RenderGearChecklist();
            _gearChecklistDirty = false;
        }
        if (_sections["money"].IsExpanded)
        {
            _moneySummary.Text = $"Corpses {StatsSnapshot.FormatCoin(s.CorpseCopper)} ({s.CoinDrops} drops, biggest {StatsSnapshot.FormatCoin(s.BiggestDrop)})\n" +
                $"Merchant sales {StatsSnapshot.FormatCoin(s.VendorCopper)} ({s.SalesCount} sales)\n" +
                $"{StatsSnapshot.FormatCoin(s.CopperPerHour)} per hour - {StatsSnapshot.FormatCoin(s.CopperPerActiveHour)} per active hour" +
                (s.Recent is { } rm ? $"\nLast {(int)rm.Window.TotalMinutes}m: {StatsSnapshot.FormatCoin(rm.Copper)}" : "");
            _soldLabel.IsVisible = s.SoldItems.Count > 0;
            // Sold items are drops too (#74, Snagglefern: "if an item is unknown on
            // the wiki I definitely sold it") — same click, tooltip, and quest badges
            // as the Loot card, with the count moved to the value column so the name
            // stays a clean lookup key.
            FillList(_soldList, s.SoldItems.Select(i =>
                    (i.Item, (i.Count > 1 ? $"x{i.Count} - " : "") + StatsSnapshot.FormatCoin(i.Copper))),
                onNameClick: ShowItemInfo,
                tooltip: n => QuestAwareTooltip(n, ItemHoverStats(n)), questBadges: true);
        }
        if (_sections["progress"].IsExpanded)
        {
            _progressSummary.Text = $"{s.XpTicks} xp gains - {s.XpPerHour:0.0}%/hr - {s.XpPerActiveHour:0.0}% active - {s.SkillUpTotal} skill-ups" +
                (s.Recent is { } rx ? $"\nLast {(int)rx.Window.TotalMinutes}m: {rx.XpPerHour:0.0}%/hr" : "") +
                (s.AaGained > 0 ? $"\n{s.AaGained} AA point{(s.AaGained == 1 ? "" : "s")} - {s.AaPerHour:0.0} AA/hr (now {s.AaTotal} unspent)" : "") +
                (s.HoursToLevel is { } eta ? $"\nNext level in {FormatEta(eta)} at this pace" : "") +
                (s.Levels.Count > 0
                    ? "\n" + string.Join(", ", s.Levels.Select((l, i) =>
                    {
                        var from = i == 0 ? s.SessionStart : s.Levels[i - 1].Time;
                        var mins = from is { } f ? (int)(l.Time - f).TotalMinutes : 0;
                        return $"{l.Text} at {l.Time:h:mm tt} ({mins}m)";
                    }))
                    : "");
            FillList(_skillList, s.SkillUps.Select(k => (k.Skill, $"{k.Value} (+{k.Ups})")));
            // AA display, rethought (Reddit, 2026-08-11: "is it supposed to just show
            // newly learned this session?" — yes, now it is): session-new AAs lead,
            // the full ledger folds behind a click, same idiom as Pet abilities.
            var newAas = s.SessionStart is { } sess
                ? s.AaAbilities.Where(a => a.Time >= sess).ToList()
                : [];
            _aaNewLabel.IsVisible = newAas.Count > 0;
            _aaNewList.IsVisible = _aaNewLabel.IsVisible;
            FillList(_aaNewList, newAas.Select(a =>
                    (a.Name, a.Rank > 1 ? $"rank {a.Rank}" : "")),
                tooltip: name => AaCatalog.Find(name)?.Effect);
            _aaAbilitiesLabel.IsVisible = s.AaAbilities.Count > 0;
            _aaAbilitiesLabel.Text = _settings.ShowAllAAs
                ? "▾ All AA abilities"
                : $"▸ All AA abilities ({s.AaAbilities.Count})";
            _aaAbilityList.IsVisible = _settings.ShowAllAAs;
            if (_settings.ShowAllAAs)
                FillList(_aaAbilityList, s.AaAbilities.Select(a =>
                        (a.Name, a.Rank > 1 ? $"rank {a.Rank}" : "")),
                    tooltip: name => AaCatalog.Find(name)?.Effect);
        }
        if (_sections["faction"].IsExpanded)
            FillList(_factionList, s.Faction.Select(f => (f.Faction, EQBuddy.UI.Shared.FactionFormat.Net(f))),
                valueBrush: f => f.StartsWith('-') ? AppTheme.BadBrush : AppTheme.GoodBrush);
        if (_sections["misc"].IsExpanded)
        {
            FillList(_deathList, s.Deaths.Select(d => (d.Text, d.Time.ToString("h:mm tt"))));
            FillList(_zoneList, s.Zones.Select(z => (z.Text, z.Time.ToString("h:mm tt"))));
            _markersLabel.IsVisible = s.Markers.Count > 0;
            FillList(_markerList, s.Markers.Select(m => (m.Text, m.Time.ToString("h:mm tt"))));
        }

        if (_expandForTesting)
        {
            try
            {
                var dump = $"dmgSrc={_damageSourceList.Children.Count} dmgTaken={_damageTakenList.Items.Count} " +
                    $"kills={_killList.Items.Count} party={_partyKillList.Items.Count} loot={_lootList.Items.Count} " +
                    $"crafted={_craftedList.Items.Count} skills={_skillList.Items.Count} faction={_factionList.Items.Count} " +
                    $"zones={_zoneList.Items.Count} deaths={_deathList.Items.Count} " +
                    $"actualH={Bounds.Height:0} actualW={Bounds.Width:0}";
                File.WriteAllText(AppPaths.File("debug.txt"), dump);
            }
            catch { }
        }
    }

    private void RefreshOptionalSectionVisibility(StatsSnapshot s)
    {
        _recentFightsLabel.IsVisible = s.RecentEncounters.Count > 0;
        _petAbilityLabel.IsVisible = s.PetAbilities.Count > 0;
        _stanceLabel.IsVisible = s.Stances.Count > 0;
        _invocationLabel.IsVisible = s.Invocations.Count > 0;
        _farmingLabel.IsVisible = s.Mobs.Any(m => m.Kills > 0);
        _partyKillsLabel.IsVisible = s.PartyKillsByKiller.Count > 0;
        _craftedLabel.IsVisible = s.Crafted.Count > 0;
        _soldLabel.IsVisible = s.SoldItems.Count > 0;
        _healSpellsLabel.IsVisible = s.HealsBySpell.Count > 0;
        _healSortBar.IsVisible = s.HealsBySpell.Count > 0;
        _healersLabel.IsVisible = s.HealsByHealer.Count > 0;
        _markersLabel.IsVisible = s.Markers.Count > 0;
    }

    // Keyed by TrackedRule.Id — a display name can be shared by two rules, and keying
    // on it made same-named rules share baselines and cooldowns.
    private readonly Dictionary<string, int> _ruleBaseline = new(StringComparer.Ordinal);
    // #137 (bjstrange): last-seen per-item counts per rule, so a burst catching several
    // distinct items names each one instead of "{last} ×N". Written and reset in
    // lock-step with _ruleBaseline — the two must never disagree about "last seen".
    private readonly Dictionary<string, Dictionary<string, int>> _ruleItemBaseline = new(StringComparer.Ordinal);
    private readonly HashSet<string> _watchExpandedRules = new(StringComparer.Ordinal);
    private readonly EQBuddy.UI.Shared.AlertCooldowns _ruleCooldowns = new();
    private readonly EQBuddy.UI.Shared.SoundGate _soundGate = new();
    private string? _alertBaselinePath;

    /// <summary>The floating alert tile, created on first use and owned by the widget.</summary>
    internal AlertWindow AlertTile => _alertWindow ??= new AlertWindow(_settings, this);

    private void RenderTracked(StatsSnapshot s,
        IReadOnlyDictionary<string, DateTime>? dueOverride = null)
    {
        if (_settings.HiddenSections.Contains("tracked")) return;   // layout collapsed it
        _sections["tracked"].IsVisible = true;
        _trackedHeader.Text = s.Tracked.Sum(t => t.TotalQuantity).ToString();
        if (!_sections["tracked"].IsExpanded) return;

        if (_settings.TrackedRules.Count == 0)
        {
            _trackedPanel.Children.Clear();
            _trackedPanel.Children.Add(EmptyCardLine(
                "No watch rules yet — add one under ⚙ Options (or pick a recent log line there)."));
            return;
        }

        _trackedPanel.Children.Clear();

        // Sort links (#105, wizen): manual follows the Options list order (arrange
        // with ▲▼ there); the rest re-order the display without touching the rules.
        if (s.Tracked.Count > 1)
        {
            var sortBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 2, 2, 0),
            };
            sortBar.Children.Add(AppTheme.DimText("sort:", new Thickness(0, 0, 4, 0)));
            foreach (var (mode, label) in new[]
                     { ("manual", "manual"), ("alpha", "a–z"), ("total", "total"), ("recent", "recent") })
            {
                var active = _settings.WatchSortMode == mode;
                var link = new TextBlock
                {
                    Text = label, FontSize = 10, Cursor = new Cursor(StandardCursorType.Hand),
                    Margin = new Thickness(0, 0, 6, 0),
                    FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal,
                    Foreground = active ? AppTheme.AccentBrush : AppTheme.DimBrush,
                };
                if (mode == "manual")
                    ToolTip.SetTip(link,
                        "The Options list order — rearrange rules with ▲▼ in Options → watch rules");
                var picked = mode;
                link.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    _settings.WatchSortMode = picked;
                    _settings.Save();
                    RenderTracked(CurrentSnapshot());
                };
                sortBar.Children.Add(link);
            }
            _trackedPanel.Children.Add(sortBar);
        }

        var ordered = _settings.WatchSortMode switch
        {
            "alpha" => s.Tracked.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            "total" => s.Tracked.OrderByDescending(t => t.TotalQuantity).ToList(),
            // Never-matched rules sink to the bottom rather than jumbling the top.
            "recent" => s.Tracked.OrderByDescending(t => t.LastMatch ?? DateTime.MinValue).ToList(),
            _ => s.Tracked,
        };

        var now = DateTime.Now;
        var dueByRule = dueOverride ?? _delayedAlerts.NextDueByRule(now);
        foreach (var r in ordered)
        {
            var head = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            head.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            head.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var counting = dueByRule.TryGetValue(r.Id, out var dueAt);
            head.Children.Add(new TextBlock
            {
                Text = counting
                    ? $"{r.Name.ToUpperInvariant()} ⏳ {EQBuddy.UI.Shared.Countdown.Format(dueAt - now)}"
                    : r.Name.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = counting ? AppTheme.WarnBrush : AppTheme.AccentBrush,
            });
            var rate = AppTheme.DimText($"{r.TotalQuantity} total - {r.PerHour:0.#}/hr - {r.PerActiveHour:0.#}/active hr");
            Grid.SetColumn(rate, 1);
            head.Children.Add(rate);
            _trackedPanel.Children.Add(head);

            _trackedPanel.Children.Add(AppTheme.DimText(
                r.LastMatch is { } lm && !string.IsNullOrWhiteSpace(r.LastItem)
                    ? $"last: {r.LastItem} · {FormatAge(now - lm)} ago"
                    : "no matches yet",
                new Thickness(6, 1, 0, 2)));

            if (r.Items.Count > 1)
            {
                var expanded = _watchExpandedRules.Contains(r.Id);
                if (expanded)
                    foreach (var item in r.Items)
                        _trackedPanel.Children.Add(new TextBlock
                        {
                            Text = $"{item.Name}   x{item.Count}",
                            FontSize = 12,
                            Foreground = AppTheme.TextBrush,
                            Margin = new Thickness(12, 1, 0, 0),
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        });

                var ruleId = r.Id;
                var toggle = AppTheme.DimText(
                    expanded ? "▾ less" : $"▸ all {r.Items.Count} kinds",
                    new Thickness(6, 1, 0, 2));
                toggle.Cursor = new Cursor(StandardCursorType.Hand);
                toggle.PointerPressed += (_, e) =>
                {
                    if (!_watchExpandedRules.Remove(ruleId))
                        _watchExpandedRules.Add(ruleId);
                    RenderTracked(CurrentSnapshot());
                    e.Handled = true;
                };
                _trackedPanel.Children.Add(toggle);
            }
        }
    }

    private static string FormatAge(TimeSpan age) => age.TotalMinutes < 1
        ? $"{Math.Max(0, (int)age.TotalSeconds)}s"
        : age.TotalHours < 1 ? $"{(int)age.TotalMinutes}m" : $"{(int)age.TotalHours}h {age.Minutes}m";

    /// <summary>Per-rule alert cooldown for text rules. Shorter than the 5 s used elsewhere
    /// (ALERT-008): a heal rotation announces every few seconds by design, and swallowing
    /// those repeats would silence exactly the case this rule kind exists for.</summary>
    private static readonly TimeSpan TextAlertCooldown = TimeSpan.FromSeconds(1);

    /// <summary>
    /// A Text watch rule matched, straight off the ingest thread. Alerting here rather than
    /// from the next snapshot removes a whole refresh interval of lag from the one rule
    /// kind that's about reacting in time. Suppressed during initial ingest, like every
    /// other alert, so replaying today's log at startup fires nothing.
    /// </summary>
    private void OnTextMatched(RawLineEvent raw)
    {
        // Immediate alerts stay suppressed during the startup re-read, but a delayed cue
        // whose due time is still ahead is recovered with the time it has left — losing a
        // running respawn timer to an app restart is exactly when you needed it.
        var ingesting = !_watcher.InitialIngestDone;
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var rule in _settings.TrackedRules)
            {
                if (!rule.Enabled || rule.Kind != WatchKind.Text) continue;
                if (!rule.Matches(raw.Line)) continue;
                if (ingesting && rule.AlertDelaySeconds <= 0) continue;

                var name = rule.Name.Length > 0 ? rule.Name : rule.Pattern;
                var line = raw.Line.Length <= 80 ? raw.Line : raw.Line[..79].TrimEnd() + "…";
                AlertOrCue(rule, name, line, TextAlertCooldown, raw.Time);
            }
        });
    }

    private readonly EQBuddy.UI.Shared.DelayedAlerts _delayedAlerts = new();

    /// <summary>
    /// Alert now, or set a cue for later when the rule asks for a delay
    /// (<see cref="TrackedRule.AlertDelaySeconds"/>) — a complete-heal chain wants the sound
    /// a couple of seconds *after* the call, and a mez wants it before the spell breaks.
    ///
    /// One dispatcher timer per cue rather than the periodic refresh, so a 2.5 s cue lands
    /// at 2.5 s. The cooldown applies when the alert fires, not when it was scheduled: with
    /// a delay set, what matters is how long since you last heard something.
    /// </summary>
    private void AlertOrCue(TrackedRule rule, string ruleName, string label, TimeSpan cooldown,
        DateTime? matchTime = null)
    {
        if (rule.AlertDelaySeconds <= 0)
        {
            FireAlert(rule, ruleName, label, cooldown);
            return;
        }
        // Scheduled from when the line was written, not when we read it.
        var from = matchTime ?? DateTime.Now;
        var remaining = from.AddSeconds(rule.AlertDelaySeconds) - DateTime.Now;
        if (remaining <= TimeSpan.Zero) return;
        if (_delayedAlerts.Schedule(rule, ruleName, label, from) is not { } pending) return;

        DispatcherTimer? timer = null;
        timer = new DispatcherTimer { Interval = remaining };
        timer.Tick += (_, _) =>
        {
            timer!.Stop();
            if (_delayedAlerts.Claim(pending))
                FireAlert(pending.Rule, pending.RuleName, pending.Label, cooldown);
        };
        timer.Start();
    }

    private void FireAlert(TrackedRule rule, string ruleName, string label, TimeSpan cooldown)
    {
        if (!_ruleCooldowns.ShouldFire(rule, label, cooldown, DateTime.Now)) return;

        if (rule.AlertBanner)
            AlertTile.ShowAlert($"★ {ruleName}: {label}",
                EQBuddy.UI.Shared.AlertColors.Hex(rule.AlertColor));
        if (EQBuddy.UI.Shared.AlertSoundCatalog.Resolve(rule, _settings.AlertSound) is { } sound)
            PlayAlertSound(sound, coalesce: true);
        if (rule.AlertSpeech)
            EQBuddy.UI.Shared.SpokenAlerts.Speak(
                EQBuddy.UI.Shared.SpokenAlerts.ResolvePhrase(rule.SpokenPhrase, label));
    }

    /// <summary>Deaths seen last refresh, so a new one can cancel pending cues — a reminder
    /// to recast something is noise once you're dead.</summary>
    private int _knownDeaths;

    /// <summary>The "Last fight" line above a card's session totals, and the "Session so far"
    /// heading that separates the two. Hidden until there's been a fight.</summary>
    private void ShowLastFight(StatsSnapshot s, Button label, StackPanel body, TextBlock text,
        Panel list, bool healing, bool open)
    {
        if (s.LastFight is not { } f)
        {
            label.IsVisible = body.IsVisible = false;
            return;
        }
        label.IsVisible = true;
        body.IsVisible = open;
        label.Content = $"{(open ? "v" : ">")} {(f.InProgress ? "Current fight" : "Last fight")}";
        if (!open) return;

        // Rates within the fight use the fight's own length, not session combat time.
        FillBreakdown(list, healing ? f.HealsBySpell : f.ByAbility,
            healing ? _healSort : _dmgOutSort, f.DurationSeconds, healing ? "hps" : "dps");
        if (!healing)
        {
            // Same treatment as the WPF card: split line, "Your damage", "Damage you took".
            _combatFightSplit.IsVisible = f.Fights.Count > 1;
            if (f.Fights.Count > 1)
                _combatFightSplit.Text = string.Join(" - ",
                    f.Fights.Select(x => $"{x.Name} {x.DamageOut:N0}"));
            _combatFightOutLabel.IsVisible = f.ByAbility.Count > 0;
            _combatFightInLabel.IsVisible = f.ByIncoming.Count > 0;
            FillList(_combatFightInList, f.ByIncoming.Select(x =>
                (x.Name, $"{x.Total:N0} - x{x.Hits} - avg {(double)x.Total / Math.Max(1, x.Hits):0.#}")));
        }
        text.Text = healing
            ? $"{f.Name} - {f.Healed:N0} healed - {f.Hps:0.#} hps over {f.DurationSeconds:0}s"
              + (f.InProgress ? " (fighting)" : "")
            : $"{f.Name} - {f.DamageOut:N0} dmg - {f.Dps:0.#} dps over {f.DurationSeconds:0}s"
              + $" - took {f.DamageIn:N0}"
              + (f.InProgress ? " (fighting)" : f.Outcome == "Killed" ? "" : $" - {f.Outcome}");
    }

    private void ProcessTrackedAlerts(StatsSnapshot s)
    {
        if (!_watcher.InitialIngestDone) return;
        if (_alertBaselinePath != _watcher.CurrentPath)
        {
            // First run isn't a character switch — cancelling here wiped cues recovered from
            // the log seconds earlier, which is the restart case they exist for.
            var switchedCharacter = _alertBaselinePath is not null;
            _alertBaselinePath = _watcher.CurrentPath;
            _ruleBaseline.Clear();
            _ruleItemBaseline.Clear();
            foreach (var r in s.Tracked)
            {
                _ruleBaseline[r.Id] = r.TotalQuantity;
                _ruleItemBaseline[r.Id] = EQBuddy.UI.Shared.WatchAlertText.ItemCounts(r);
            }
            if (switchedCharacter) _delayedAlerts.CancelAll();
            _knownDeaths = s.Deaths.Count;
            return;
        }
        // Combat cues only: a respawn timer doesn't care that you died.
        if (s.Deaths.Count > _knownDeaths) _delayedAlerts.CancelCombatCues();
        _knownDeaths = s.Deaths.Count;

        foreach (var r in s.Tracked)
        {
            var baseline = _ruleBaseline.TryGetValue(r.Id, out var b) ? b : 0;
            if (r.TotalQuantity <= baseline)
            {
                _ruleBaseline[r.Id] = r.TotalQuantity;
                _ruleItemBaseline[r.Id] = EQBuddy.UI.Shared.WatchAlertText.ItemCounts(r);
                continue;
            }
            var delta = r.TotalQuantity - baseline;
            var previousItems = _ruleItemBaseline.TryGetValue(r.Id, out var prevItems) ? prevItems : null;
            _ruleBaseline[r.Id] = r.TotalQuantity;
            _ruleItemBaseline[r.Id] = EQBuddy.UI.Shared.WatchAlertText.ItemCounts(r);
            var rule = _settings.TrackedRules.FirstOrDefault(x => x.Id == r.Id);
            if (rule is null) continue;
            // Text rules already alerted from the ingest thread the moment the line
            // arrived (OnTextMatched). The baseline above still had to move so this rule
            // doesn't look like a fresh burst later.
            if (rule.Kind == WatchKind.Text) continue;
            AlertOrCue(rule, r.Name,
                EQBuddy.UI.Shared.WatchAlertText.MatchLabel(rule, r, delta, previousItems),
                TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>The Combat card's session-pace sparkline (2026-08-11): damage per
    /// calendar minute across the last 30, zeros filled in — quiet minutes stay flat
    /// instead of being edited out, because pacing is the honest story. Same
    /// monotone-cubic smoothing as the fight timeline, sampled densely so the control
    /// stays a Polyline.</summary>
    private void PaintCombatSpark(StatsSnapshot s)
    {
        var pts = s.DamageTimeline;
        if (pts.Count < 2) { _combatSparkHost.IsVisible = false; return; }
        var end = pts[^1].Time;
        var perMinute = pts.ToDictionary(p => p.Time, p => p.Damage);
        var series = new List<long>();
        for (var m = 29; m >= 0; m--)
            series.Add(perMinute.GetValueOrDefault(end.AddMinutes(-m)));
        if (series.Count(v => v > 0) < 2) { _combatSparkHost.IsVisible = false; return; }
        _combatSparkHost.IsVisible = true;

        var w = _combatSparkHost.Bounds.Width > 20 ? _combatSparkHost.Bounds.Width : 300;
        const double h = 30;
        var max = Math.Max(1, series.Max());
        var xs = new double[series.Count];
        var ys = new double[series.Count];
        var peakIdx = 0;
        for (var i = 0; i < series.Count; i++)
        {
            xs[i] = w * i / (series.Count - 1);
            ys[i] = h - h * series[i] / max;
            if (series[i] > series[peakIdx]) peakIdx = i;
        }
        // 3 samples/segment: visually identical at sparkline size, half the points
        // pushed through layout each combat second.
        var line = new List<Point>();
        foreach (var (px, py) in EQBuddy.UI.Shared.MonotoneCurve.Sample(xs, ys, samplesPerSegment: 3))
            line.Add(new Point(px, py));
        _combatSpark.Points = line;
        var fill = new List<Point>(line) { new(w, h + 2), new(0, h + 2) };
        _combatSparkFill.Points = fill;
        // Rebuilt whenever the series color moved (a theme switch mutates the live
        // brush): gradient stops snapshot the color, so a build-once fill would keep
        // painting the old theme's wash forever.
        var you = AppTheme.ChartYouBrush.Color;
        if (_combatSparkFill.Fill is null || _combatSparkFillColor != you)
        {
            _combatSparkFillColor = you;
            _combatSparkFill.Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x40, you.R, you.G, you.B), 0),
                    new GradientStop(Color.FromArgb(0x00, you.R, you.G, you.B), 1),
                },
            };
        }
        _combatSparkPeak.IsVisible = true;
        _combatSparkPeak.Margin = new Thickness(
            Math.Max(0, xs[peakIdx] - 3), Math.Max(0, ys[peakIdx] - 3), 0, 0);
        ToolTip.SetTip(_combatSparkHost,
            $"Damage per minute, last 30 — hottest minute {max:N0} at {end.AddMinutes(peakIdx - 29):HH:mm}");
    }

    /// <summary>#89 (jeremycranfill): the fight as a Discord-ready code block on the
    /// clipboard — the official Discord bans image sharing, so parses travel as text.</summary>
    private async void OnCopyFight(object? sender, EventArgs e)
    {
        if (CurrentSnapshot().LastFight is not { } f) return;
        try
        {
            if (Clipboard is { } clipboard)
                await clipboard.SetTextAsync(EQBuddy.UI.Shared.FightExport.ToText(
                    f, Identity.Character, $"v{UpdateChecker.CurrentVersion}"));
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    /// <summary>
    /// The Buffs card: every buff believed active on you, soonest-fading first, with
    /// a countdown. "est" marks a wiki-base duration (ranks and AAs lengthen buffs;
    /// a natural fade teaches the real number and the label drops). Unresolved
    /// landings show the line itself and the longest candidate duration — honest
    /// range, never a guess.
    /// </summary>
    private void RenderBuffs()
    {
        if (_settings.HiddenSections.Contains("buffs")) return;   // layout collapsed it
        _sections["buffs"].IsVisible = true;
        var count = _buffTracker.ActiveCount;   // header needs a number, not a list
        _buffsHeader.Text = count.ToString();
        if (!_sections["buffs"].IsExpanded) return;
        var now = DateTime.Now;
        var buffs = count > 0 ? _buffTracker.Snapshot(now) : [];

        // Expiring-only mode (David): the card stays quiet until a buff is inside the
        // warning window — "tell me when it matters", with the rest counted honestly.
        var quiet = 0;
        if (_settings.BuffTimersExpiringOnly && buffs.Count > 0)
        {
            var warn = Math.Max(10, _settings.BuffWarnSeconds);
            var urgent = buffs.Where(b => b.RemainingSeconds(now) is { } r && r <= warn).ToList();
            quiet = buffs.Count - urgent.Count;
            buffs = urgent;
        }

        var signature = string.Join("|", buffs.Select(b => b.Label + (b.Estimated ? "~" : ""))) + "·" + quiet;
        if (signature == _buffsSignature)
        {
            // Same rows, newer clocks: update text and urgency tint in place.
            for (var i = 0; i < _buffClocks.Count && i < buffs.Count; i++)
            {
                var remaining = buffs[i].RemainingSeconds(now);
                _buffClocks[i].Clock.Text = BuffClockText(remaining, buffs[i].Estimated);
                _buffClocks[i].Clock.Foreground = remaining is < 60 ? AppTheme.WarnBrush : AppTheme.DimBrush;
            }
            return;
        }
        _buffsSignature = signature;
        _buffClocks.Clear();

        _buffsPanel.Children.Clear();
        if (buffs.Count == 0)
        {
            _buffsPanel.Children.Add(EmptyCardLine(_settings.BuffTimersExpiringOnly && quiet > 0
                ? $"{quiet} running quietly — timers appear at {Math.Max(10, _settings.BuffWarnSeconds):0}s left."
                : "Nothing running — a buff landing on you starts its countdown here."));
            return;
        }
        foreach (var b in buffs)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var name = new TextBlock
            {
                Text = b.Label, FontSize = 12,
                Foreground = AppTheme.TextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ToolTip.SetTip(name,
                (b.Candidates.Length > 1
                    ? "One of: " + string.Join(", ", b.Candidates) + " · "
                    : "")
                + (b.Caster.Length > 0 ? $"cast by {b.Caster} · " : "")
                + $"landed {b.LandedAt:h:mm:ss tt}"
                + (b.Estimated ? " · est = wiki base; a natural fade teaches your real duration" : ""));
            row.Children.Add(name);
            var remaining = b.RemainingSeconds(now);
            var clock = new TextBlock
            {
                Text = BuffClockText(remaining, b.Estimated), FontSize = 12,
                Foreground = remaining is < 60 ? AppTheme.WarnBrush : AppTheme.DimBrush,
            };
            Grid.SetColumn(clock, 1);
            row.Children.Add(clock);
            _buffsPanel.Children.Add(row);
            _buffClocks.Add((clock, b.Label));
        }
    }

    private static string BuffClockText(double? remaining, bool estimated) => remaining is { } r
        ? $"{(int)r / 60}:{(int)r % 60:00}{(estimated ? " est" : "")}"
        : "?";

    /// <summary>
    /// The Raids card: every raid target the game's own achievements list names, per
    /// zone, with the personal record — witnessed kills with dates, or the imported
    /// Conqueror achievement for clears from before EQBuddy. The badge is the highest
    /// difficulty PROVEN by a witnessed kill; kills from before tiers existed carry no
    /// tier and earn no badge — honesty over flattery.
    /// </summary>
    private void RenderRaids()
    {
        if (_settings.HiddenSections.Contains("raids")) return;   // layout collapsed it
        _sections["raids"].IsVisible = true;
        var defeated = _raidLedger.DefeatedCount();
        var catalog = RaidTargetCatalog.Default;
        _raidsHeader.Text = $"{defeated} / {catalog.BossCount}";
        if (!_sections["raids"].IsExpanded) return;
        if (defeated == 0)
        {
            _raidsPanel.Children.Clear();
            _raidsPanel.Children.Add(EmptyCardLine(
                "Nothing defeated yet — kills your log witnesses land here, and importing " +
                "/outputfile achievements marks clears from before EQBuddy."));
            return;
        }

        _raidsPanel.Children.Clear();
        foreach (var zone in catalog.Zones)
        {
            var records = zone.Bosses.Select(b => (Boss: b, Rec: _raidLedger.For(b))).ToList();
            var done = records.Count(x => x.Rec is { } r && (r.Kills > 0 || r.AchievementComplete));
            _raidsPanel.Children.Add(new TextBlock
            {
                Text = $"{zone.Zone} — {done}/{zone.Bosses.Length}",
                FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 1),
                Foreground = done == zone.Bosses.Length ? AppTheme.GoodBrush : AppTheme.AccentBrush,
            });
            foreach (var (boss, rec) in records)
            {
                var cleared = rec is { } rr && (rr.Kills > 0 || rr.AchievementComplete);
                var badge = rec?.HighestDifficulty() is { } hd ? $"D{hd} · " : "";
                var detail = rec switch
                {
                    { Kills: > 0 } k =>
                        $"{badge}{(k.Kills > 1 ? $"×{k.Kills} · " : "")}last {k.LastKill:MMM d}",
                    { AchievementComplete: true } => "cleared (from achievements)",
                    _ => "",
                };
                var row = new TextBlock
                {
                    Text = $"{(cleared ? "✓" : "·")} {boss}{(detail.Length > 0 ? $" — {detail}" : "")}",
                    FontSize = 11.5, Margin = new Thickness(6, 0, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = cleared ? AppTheme.TextBrush : AppTheme.DimBrush,
                };
                if (rec is { TierKills.Count: > 0 } tk)
                    ToolTip.SetTip(row, "Kills by difficulty: " + string.Join(" · ",
                        new[] { "d4", "d3", "d2", "d1", "d0", "open", "instance", "unknown" }
                            .Where(k => tk.TierKills.ContainsKey(k))
                            .Select(k => $"{(k.StartsWith('d') ? k.ToUpperInvariant() : k)} ×{tk.TierKills[k]}"))
                        + (tk.Kills > tk.TierKills.Values.Sum()
                            ? $" · {tk.Kills - tk.TierKills.Values.Sum()} earlier kill(s) predate tier tracking"
                            : ""));
                _raidsPanel.Children.Add(row);
            }
        }
        _raidsPanel.Children.Add(new TextBlock
        {
            Text = "Kills count when your log sees the boss die; import /outputfile achievements to mark older clears.",
            FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
            Foreground = AppTheme.DimBrush,
        });
    }

    private void UpdateLoggingStatus()
    {
        DateTime? lastActivity = _watcher.LastGrowth;
        if (lastActivity is null && _watcher.CurrentPath is { } p && File.Exists(p))
            lastActivity = File.GetLastWriteTime(p);
        var age = lastActivity is { } t ? DateTime.Now - t : TimeSpan.MaxValue;
        var brush = age < TimeSpan.FromSeconds(30) ? AppTheme.GoodBrush : age < TimeSpan.FromMinutes(2) ? AppTheme.WarnBrush : AppTheme.BadBrush;
        var tip = lastActivity is { } la ? $"Last log activity: {la:h:mm:ss tt}" : "No log file activity yet";
        _statusDot.Fill = brush;
        _miniDot.Fill = brush;
        ToolTip.SetTip(_statusDot, tip);
        ToolTip.SetTip(_miniDot, tip);
        _logBanner.IsVisible = age > TimeSpan.FromMinutes(2);
    }

    private void SetMode(bool mini)
    {
        _settings.Minimized = mini;
        _miniRoot.IsVisible = mini;
        _normalRoot.IsVisible = !mini;
        if (_heightGrip is { } grip) grip.IsVisible = !mini;
        Topmost = true;
        // Switching modes opens and closes the breakout windows, and a new window starts at
        // whatever level Avalonia gave it — raise them now rather than leaving them behind
        // the game until the next tick.
        EnsureOverlayLevel();
        _settings.Save();
        if (!mini) _dismissedBreakouts.Clear();
        var snapshot = CurrentSnapshot();
        if (mini) UpdateMiniChips(snapshot);
        UpdateBreakouts(snapshot);
    }

    private static readonly (BreakoutKind Kind, string Star)[] BreakoutStars =
        [(BreakoutKind.Damage, "dps"), (BreakoutKind.Healing, "hps"), (BreakoutKind.Pet, "pet")];

    private void UpdateBreakouts(StatsSnapshot snapshot)
    {
        // Avalonia refuses Show(owner) while the owner itself isn't visible — and the
        // ctor's SetMode lands here before the main window ever opens. A profile saved
        // minimized then CRASHED ON EVERY LAUNCH, unrecoverably (issue #82, Bazzite/KDE:
        // "can't reopen"). The 1-second tick calls back the moment we're actually up.
        // A focus-hide is the exception: then the pass runs solely to HIDE breakouts.
        if (!IsVisible && !_hiddenForFocus) return;
        foreach (var (kind, star) in BreakoutStars)
        {
            var wanted = _settings.Minimized && !_hiddenForFocus
                && _settings.MiniStats.Contains(star)
                && !_settings.DisabledBreakouts.Contains(kind.ToString())
                && !_dismissedBreakouts.Contains(kind);
            _breakouts.TryGetValue(kind, out var window);
            if (wanted)
            {
                if (window is null)
                {
                    window = new BreakoutWindow(_settings, kind)
                    {
                        OpenTimeline = OpenFightTimeline,
                        CharacterName = () => Identity.Character,
                    };
                    window.Dismissed += dismissed =>
                    {
                        _dismissedBreakouts.Add(dismissed);
                        // ✕ is now persistent, like WPF (#45): a permanent state change
                        // must announce itself, and leave a timestamp behind.
                        if (!_settings.DisabledBreakouts.Contains(dismissed.ToString()))
                            _settings.DisabledBreakouts.Add(dismissed.ToString());
                        _settings.Save();
                        AlertTile.ShowAlert($"{dismissed} breakout hidden — re-enable in ⚙ Options → Breakout windows");
                        CoreLog.Error($"{dismissed} breakout hidden via its ✕ (re-enable: Options → Breakout windows)");
                    };
                    _breakouts[kind] = window;
                }
                try
                {
                    if (!window.IsVisible) window.Show(this);
                    window.Update(snapshot);
                }
                catch (Exception ex)
                {
                    // A breakout must never take the whole widget down with it (#82).
                    App.LogError(ex);
                }
            }
            else if (window is { IsVisible: true }) window.HideAndSave();
        }
    }

    /// <summary>One mini-dashboard stat (2026-08-11, take two — David: no ovals):
    /// glyph + semibold tabular value as clean text, separated from its neighbor by
    /// a thin hairline divider rather than any chip chrome. A counting-down watch
    /// rule still announces itself by color alone.</summary>
    private static StackPanel MiniChip(string glyph, string value, IBrush valueBrush)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = glyph, FontSize = 11.5, Opacity = 0.9,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = value, FontSize = 12.5, FontWeight = FontWeight.SemiBold,
            Foreground = valueBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new Border
        {
            Width = 1, Margin = new Thickness(10, 2, 0, 2),
            Background = AppTheme.HairlineBrush,
        });
        return panel;
    }

    /// <summary>The last chip's divider has nothing to divide — trim it.</summary>
    private static void TrimLastMiniDivider(Panel chips)
    {
        if (chips.Children.Count > 0 && chips.Children[^1] is StackPanel { Children.Count: > 0 } last
            && last.Children[^1] is Border divider)
            divider.IsVisible = false;
    }

    private void UpdateMiniChips(StatsSnapshot s)
    {
        _miniChips.Children.Clear();
        var selected = MiniStatOrder.Where(_settings.MiniStats.Contains).ToList();
        foreach (var key in selected)
        {
            var (glyph, text) = key switch
            {
                "kills" => ("\U0001F480", $"{s.YourKillCount}"),
                "dps" => ("⚔", s.CurrentDps > 0 ? $"{s.CurrentDps:0} dps" : $"{s.SessionDps:0} dps"),
                "hps" => ("✚", $"{s.Hps:0.#} hps"),
                "pet" => ("🐾", $"{s.PetAbilities.Sum(row => row.Total) / Math.Max(1, s.CombatSeconds):0.#} dps"),
                // Same denominator as the Procs card: combat minutes, so downtime
                // doesn't flatter the weapon.
                "procs" => ("⚡", $"{s.Procs.Sum(p => p.Count) / Math.Max(1.0 / 60, s.CombatSeconds / 60.0):0.#}/min"),
                "loot" => ("\U0001F392", $"{s.LootTotal}"),
                "motes" => ("\U0001F52E", Motes.Summarize(s.Loot, s.Elapsed) is { Total: > 0 } mo
                    ? $"{mo.Total} · {mo.PerHour:0.#}/hr" : "0"),
                "money" => ("\U0001F4B0", StatsSnapshot.FormatCoin(s.Copper)),
                // Rate, not total: minimized is farming mode, and "how fast am I
                // gaining" is the number a farmer watches (MorrolanTV, discussion #63).
                "xp" => ("\U0001F4C8", $"{s.XpPerHour:0.#}%/hr" +
                        (s.HoursToLevel is { } eta ? $" · lvl {FormatEta(eta)}" : "")),
                "deaths" => ("☠", $"{s.Deaths.Count}"),
                _ => ("", ""),
            };
            _miniChips.Children.Add(MiniChip(glyph, text, AppTheme.AccentBrush));
        }
        // Per-rule pins, not every enabled rule: a mini bar with eight chips isn't a mini bar.
        var due = _delayedAlerts.NextDueByRule(DateTime.Now);
        foreach (var rule in _settings.PinWatchChips
                     ? _settings.TrackedRules.Where(r => r.Enabled && r.Pinned)
                     : [])
        {
            var name = rule.Name.Length > 0 ? rule.Name : rule.Pattern;
            var result = s.Tracked.FirstOrDefault(t => t.Id == rule.Id);
            // While a cue is counting down, when it fires is the only thing worth the space.
            var counting = due.TryGetValue(rule.Id, out var at);
            _miniChips.Children.Add(counting
                ? MiniChip("⏳", $"{name} {EQBuddy.UI.Shared.Countdown.Format(at - DateTime.Now)}",
                    AppTheme.WarnBrush)
                : MiniChip("🎯", $"{name} {result?.TotalQuantity ?? 0}", AppTheme.AccentBrush));
        }

        TrimLastMiniDivider(_miniChips);
        // Only when there is genuinely nothing to show — it used to return early when no
        // stats were starred, hiding pinned watch chips behind the hint.
        if (_miniChips.Children.Count == 0)
            _miniChips.Children.Add(AppTheme.DimText("* star stats in full view"));
    }

    private static string FormatEta(double hours) => hours >= 1
        ? $"~{(int)hours}h {(int)((hours - (int)hours) * 60)}m"
        : $"~{Math.Max(1, (int)(hours * 60))}m";

    private void OnOptions(object? sender, EventArgs e)
    {
        if (_optionsWindow is { IsVisible: true })
        {
            _optionsWindow.Activate();
            return;
        }
        _optionsWindow = new OptionsWindow(this);
        WireOptionsWindow(_optionsWindow);
        _optionsWindow.Closed += (_, _) => _alertWindow?.ExitPlacement();
        _optionsWindow.Show(this);
        AlertTile.EnterPlacement();
    }

    internal void RegisterOptionsWindow(OptionsWindow window)
    {
        _optionsWindow = window;
        WireOptionsWindow(window);
    }

    /// <summary>The Options window's live-side-effect hooks (its settings writes work
    /// without them; these make the change visible immediately).</summary>
    private void WireOptionsWindow(OptionsWindow window)
    {
        window.RecentLinesSource = RecentLogLines;
        window.ApplyChipScale = SetChipScale;
        window.ApplyGridOverlay = SetGridOverlay;
        window.ApplyGridSpacing = RefreshGridSpacing;
        window.ApplyCursorRing = SetCursorRing;
        window.ApplyHotkeys = ApplyHotkeys;
        window.RefreshGearCard = RefreshGearCard;
    }

    private void OnTutorial(object? sender, EventArgs e) => new TutorialWindow(this).Show(this);

    /// <summary>One switch keeps settings, Options, and tracker window in sync (the
    /// menu item moved to Options with the 1.67.1 menu reshuffle).</summary>
    internal void SetTrackSpawns(bool on)
    {
        _settings.TrackSpawns = on;
        _settings.Save();
        if (_optionsWindow is { IsVisible: true } options)
            options.SyncTrackSpawns(on);
        if (!on)
        {
            CloseSpawnChips();
            if (_spawnsWindow is { } window)
            {
                _spawnsWindow = null;
                window.Close();
            }
        }
    }

    internal void ShowSpawnsWindow(string? zone = null)
    {
        if (_spawnsWindow is { IsVisible: true })
        {
            _spawnsWindow.Activate();
            return;
        }
        var window = new SpawnsWindow(this, _spawnsVm, zone);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_spawnsWindow, window)) _spawnsWindow = null;
        };
        _spawnsWindow = window;
        window.Show(this);
    }

    private void CloseSpawnChips()
    {
        if (_spawnChipsWindow is not { } chips) return;
        _spawnChipsWindow = null;
        chips.Close();
    }

    private void CloseMezChips()
    {
        if (_mezChipsWindow is not { } chips) return;
        _mezChipsWindow = null;
        chips.Close();
    }

    // ---- satellite windows new since v1.32: quests, drops, map, travel, inventory,
    // gear locker, fight timeline — all reuse-if-open, mirroring WPF ----

    private QuestsWindow? _questsWindow;
    private DropsWindow? _dropsWindow;
    private MapWindow? _mapWindow;
    private TravelWindow? _travelWindow;
    private InventoryWindow? _inventoryWindow;
    private GearLockerWindow? _gearLockerWindow;
    private FightTimelineWindow? _timelineWindow;

    private void OnQuestsWindow(object? sender, EventArgs e) => ShowQuestsWindow();

    /// <summary>Open (or front) the Quest Tracker; with an item, jump straight to that
    /// item's quests — the 🗺 badge path from the Loot views.</summary>
    internal void ShowQuestsWindow(string? filterItem = null)
    {
        // Reopen contract for every satellite here: a null field means the window is
        // closed for real (each Closed handler nulls it — Avalonia's Show() throws on a
        // closed window, so WPF's `is not { IsLoaded: true }` becomes a null check), and
        // a non-null field is reused via Show()+Activate() — Show() no-ops when it's
        // already visible and resurfaces one hidden by the toggleAll hotkey.
        if (_questsWindow is null)
        {
            var window = new QuestsWindow(this);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_questsWindow, window)) _questsWindow = null;
            };
            _questsWindow = window;
        }
        _questsWindow.Show();
        if (filterItem is { Length: > 0 }) _questsWindow.FilterToItem(filterItem);
        _questsWindow.Activate();
    }

    private void OnDropsWindow(object? sender, EventArgs e)
    {
        if (_dropsWindow is null)
        {
            var window = new DropsWindow(this);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_dropsWindow, window)) _dropsWindow = null;
            };
            _dropsWindow = window;
        }
        _dropsWindow.Update(CurrentSnapshot());
        _dropsWindow.Show();
        _dropsWindow.Activate();
    }

    private void OnZoneMap(object? sender, EventArgs e)
    {
        if (_mapWindow is null)
        {
            var window = new MapWindow(this);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_mapWindow, window)) _mapWindow = null;
            };
            _mapWindow = window;
        }
        _mapWindow.Show(this);
        _mapWindow.Activate();
    }

    private void OnTravelRoute(object? sender, EventArgs e)
    {
        if (_travelWindow is null)
        {
            var window = new TravelWindow(this);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_travelWindow, window)) _travelWindow = null;
            };
            _travelWindow = window;
        }
        _travelWindow.Show(this);
        _travelWindow.RenderRoute();
        _travelWindow.Activate();
    }

    private void OnInventoryWindow(object? sender, EventArgs e)
    {
        if (_inventoryWindow is { IsVisible: true } w) { w.Activate(); return; }
        _inventoryWindow = new InventoryWindow(refresh => LatestInventory(refresh));
        _inventoryWindow.Closed += (_, _) => _inventoryWindow = null;
        _inventoryWindow.Show(this);
    }

    private void OnGearLocker(object? sender, EventArgs e)
    {
        if (_gearLockerWindow is { IsVisible: true } open) { open.Activate(); return; }
        _gearLockerWindow = new GearLockerWindow(_wikiItems,
            refresh => LatestInventory(refresh),
            () => QuestLedger?.ClassesFor(QuestCharacterKey) ?? [],
            () => CurrentSnapshot().InferredClass);
        _gearLockerWindow.Closed += (_, _) => _gearLockerWindow = null;
        _gearLockerWindow.Show(this);
    }

    /// <summary>The fight timeline (⧗): one window, re-fronted if already open —
    /// reachable from the Combat card and the Damage breakout alike.</summary>
    internal void OpenFightTimeline()
    {
        if (_timelineWindow is null)
        {
            var window = new FightTimelineWindow(_settings, TimelineSource)
            { SourceVersion = () => _stats.CurrentVersion };
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_timelineWindow, window)) _timelineWindow = null;
            };
            _timelineWindow = window;
        }
        _timelineWindow.Show();
        _timelineWindow.Activate();
    }

    /// <summary>The timeline's data pull, called on its 1 s tick: the current/last
    /// pull plus the journal slice under it. A live fight's window extends a couple
    /// of seconds so the newest events aren't clipped by the running duration.</summary>
    private (LastFightInfo?, List<GameEvent>, string) TimelineSource()
    {
        var s = CurrentSnapshot();
        if (s.LastFight is not { } f || f.Start == default) return (null, [], "");
        var end = f.Start.AddSeconds(f.DurationSeconds + (f.InProgress ? 2 : 0));
        return (f, _stats.JournalWindow(f.Start, end), s.PetName);
    }

    /// <summary>Mez chips as the fight stack sees them — MezChipsWindow.BuildChips is
    /// the shared builder (names numbered, draining gauge fractions included).</summary>
    private List<SpawnChip> MezChips(DateTime now) =>
        MezChipsWindow.BuildChips(_mezTracker.Snapshot(now), now);

    /// <summary>Everything the fight-side chip stack shows: mez chips and slow chips,
    /// each behind its own Options toggle, sharing one window and saved position.</summary>
    private List<SpawnChip> FightChips(DateTime now)
    {
        var chips = _settings.MezChipsEnabled ? MezChips(now) : [];
        if (SlowChipsVisible(now)) chips.AddRange(SlowChips(now));
        // Placement preview (#94 follow-up): the stack only exists while a mez or
        // slow is live, so there was no way to park it BEFORE the first mid-fight
        // debuff. While Options is open, an empty stack shows one draggable
        // placeholder — same idea as the alert tile's placement mode.
        if (chips.Count == 0 && _optionsWindow is { IsVisible: true })
            chips.Add(new SpawnChip(Zone: "", Name: "drag me — chips appear here",
                CountdownText: "", IsDue: false,
                Detail: "Placement preview: 💤 mez and 🐌 slow chips will stack at this "
                    + "spot. Drag it where you'll notice them; it disappears when "
                    + "Options closes.",
                Icon: "🐌"));
        return chips;
    }

    private bool SlowChipsVisible(DateTime now) =>
        _settings.SlowAlertEnabled
        && (!_settings.SlowAlertRaidOnly || _slowTracker.InRaid(now));

    /// <summary>Slow chips (#94): the debuff's honest % (a range when several slows
    /// share the landing line), time left when the wiki documents a duration, and the
    /// cure line in the tooltip — "how do I get rid of this" attached to the alert.</summary>
    private List<SpawnChip> SlowChips(DateTime now) =>
        _slowTracker.Snapshot(now).Select(s =>
        {
            var remaining = s.RemainingSeconds(now);
            var detail = string.Join(" · ", new[]
            {
                s.Spells.Length == 1 ? s.Spells[0] : "One of: " + string.Join(", ", s.Spells),
                s.CounterText,
                _slowTracker.CureLine(s),
                $"landed {s.LandedAt:h:mm:ss tt}",
            }.Where(part => part.Length > 0));
            return new SpawnChip(
                Zone: "", Name: EQBuddy.UI.Shared.SlowChipText.Label(s),
                CountdownText: remaining is { } r ? $"{(int)r / 60}:{(int)r % 60:00}" : "?",
                IsDue: false, Detail: detail, Icon: "🐌")
            {
                Fraction = s.ExpiresAt is { } exp && (exp - s.LandedAt).TotalSeconds is > 0 and var dur
                    ? Math.Clamp((now - s.LandedAt).TotalSeconds / dur, 0, 1)
                    : null,
            };
        }).ToList();

    /// <summary>Live-apply the chips/alerts scale to whichever family windows exist right
    /// now; windows created later pick it up in their constructors.</summary>
    public void SetChipScale(double scale)
    {
        _settings.ChipScale = Math.Clamp(scale, 0.5, 2.0);
        foreach (var w in new Window?[] { _spawnChipsWindow, _mezChipsWindow, _alertWindow })
            if (w is not null) ChipScale.Apply(w, _settings.ChipScale);
        _settings.Save();
    }

    // ---- the alignment grid (discussion #34) ----

    private GridOverlayWindow? _gridOverlay;

    /// <summary>Options checkbox lands here (the SetTrackSpawns pattern). The overlay
    /// window exists only while the grid is on — nothing invisible lingers.</summary>
    internal void SetGridOverlay(bool on)
    {
        _settings.ShowGridOverlay = on;
        _settings.Save();
        if (on)
        {
            if (_gridOverlay is not { IsVisible: true })
                _gridOverlay = new GridOverlayWindow(_settings);
            _gridOverlay.Show();
            _gridOverlay.ApplySpacing();
        }
        else
        {
            _gridOverlay?.Close();
            _gridOverlay = null;
        }
    }

    /// <summary>Live spacing updates from the Options slider.</summary>
    internal void RefreshGridSpacing() => _gridOverlay?.ApplySpacing();

    // ---- the cursor-finder ring (issue #81) ----

    private CursorRingWindow? _cursorRing;

    /// <summary>Same lockstep shape as SetGridOverlay: the window exists only while
    /// the ring is on.</summary>
    internal void SetCursorRing(bool on)
    {
        _settings.ShowCursorRing = on;
        _settings.Save();
        if (on)
        {
            if (_cursorRing is not { IsVisible: true })
                _cursorRing = new CursorRingWindow(_settings);
            _cursorRing.ApplySize();
            _cursorRing.Show();
        }
        else if (_cursorRing is { } ring)
        {
            _cursorRing = null;
            ring.Close();
        }
    }

    // ---- global hotkeys, opt-in only (#100 — see HotkeyManager) ----

    private readonly HotkeyManager _hotkeys = new();
    private bool _hotkeyHidden;
    private readonly List<Window> _hotkeyHiddenWindows = [];
    private TrayIcon? _trayIcon;

    /// <summary>Registers whatever the player bound in Options; called at startup
    /// and again after any Options edit. Windows-only for now — HotkeyManager logs
    /// the degradation once elsewhere.</summary>
    internal void ApplyHotkeys() =>
        _hotkeys.Apply(this, _settings.Hotkeys,
            action => Dispatcher.UIThread.Post(() => HandleHotkeyAction(action)));

    /// <summary>The hotkey dispatch, out of the registration lambda so headless tests
    /// can exercise the window lifecycles without a Win32 RegisterHotKey.</summary>
    internal void HandleHotkeyAction(string action)
    {
        switch (action)
        {
            case "toggleAll":
                // The get-out-of-my-way key: everything hides as one, comes back
                // as it was. Same idea as focus-hide, but on demand.
                if (_hotkeyHidden)
                {
                    foreach (var w in _hotkeyHiddenWindows)
                    {
                        w.Closed -= OnHotkeyHiddenWindowClosed;
                        w.Show();
                    }
                    _hotkeyHiddenWindows.Clear();
                    _hotkeyHidden = false;
                }
                else
                {
                    foreach (var w in AppWindows())
                        if (w.IsVisible)
                        {
                            _hotkeyHiddenWindows.Add(w);
                            // A window that closes while hidden (a chip stack whose
                            // timers ran out, a tracker closed by SetTrackSpawns) must
                            // leave the restore list — Avalonia's Show() throws on a
                            // closed window (WPF gets away with an IsLoaded check).
                            w.Closed += OnHotkeyHiddenWindowClosed;
                            w.Hide();
                        }
                    _hotkeyHidden = _hotkeyHiddenWindows.Count > 0;
                }
                break;
            case "toggleMap":
                if (_mapWindow is { IsVisible: true }) _mapWindow.Hide();
                else if (_mapWindow is not null) _mapWindow.Show();
                else OnZoneMap(this, EventArgs.Empty);
                break;
            case "toggleQuests":
                if (_questsWindow is { IsVisible: true }) _questsWindow.Hide();
                else if (_questsWindow is not null) _questsWindow.Show();
                else OnQuestsWindow(this, EventArgs.Empty);
                break;
            case "toggleSpawns":
                if (_spawnsWindow is { IsVisible: true }) _spawnsWindow.Hide();
                else if (_spawnsWindow is not null) _spawnsWindow.Show();
                else ShowSpawnsWindow();
                break;
            case "toggleClickThrough":
                SetClickThrough(!_clickThrough);
                break;
            // #100 round two (jlcrisp): the pill/dashboard flip, from the keyboard.
            case "toggleMinimize":
                SetMode(!_settings.Minimized);
                break;
        }
    }

    private void OnHotkeyHiddenWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        window.Closed -= OnHotkeyHiddenWindowClosed;
        _hotkeyHiddenWindows.Remove(window);
    }

    /// <summary>Every open app window, for the toggleAll capture. Headless tests have
    /// no desktop lifetime to enumerate, so they supply the list themselves (the
    /// MarkUserMovedForTests pattern).</summary>
    internal Func<IReadOnlyList<Window>>? WindowEnumeratorForTests;

    private IReadOnlyList<Window> AppWindows() =>
        WindowEnumeratorForTests?.Invoke()
        ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.Windows.ToList()
        ?? [];

    /// <summary>
    /// A slow landed on the player, straight off the ingest thread. Speaks once per
    /// landing (never on the refresh of one already showing — a chain-slowing NPC must
    /// not turn the voice into a metronome); the chip itself appears via the 1 s tick.
    /// Suppressed during the startup replay like every other alert — a slow from an
    /// hour ago is history, not news.
    /// </summary>
    private void OnSlowLanded(SlowState state, bool isNew)
    {
        if (!isNew || !_watcher.InitialIngestDone) return;
        if (!_settings.SlowAlertEnabled || !_settings.SlowAlertSpoken) return;
        if (_settings.SlowAlertRaidOnly && !_slowTracker.InRaid(state.LandedAt)) return;
        var pct = state.PctMin == state.PctMax
            ? $"{state.PctMax} percent"
            : $"up to {state.PctMax} percent";
        EQBuddy.UI.Shared.SpokenAlerts.Speak($"Slowed {pct}");
    }

    /// <summary>#88 (typical-usual-chaos): read the game's own `/outputfile achievements`
    /// dump and pre-mark Sky rewards completed before EQBuddy existed. Preview first,
    /// nothing applies until confirmed, and the import only ever adds — the same
    /// never-regress rule the AA ledger lives by. Unmatched names are shown, not
    /// silently dropped (reward names drift from the wiki's).</summary>
    private async void OnImportAchievements(object? sender, EventArgs e)
    {
        // /outputfile writes beside eqgame.exe — the Logs folder's parent.
        string? startIn = null;
        if (_settings.LogFolder is { Length: > 0 } lf
            && System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(lf)) is { } root
            && Directory.Exists(root))
            startIn = root;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pick the game's achievements dump (/outputfile achievements)",
            AllowMultiple = false,
            SuggestedStartLocation = startIn is null
                ? null
                : await StorageProvider.TryGetFolderFromPathAsync(startIn),
            FileTypeFilter =
            [
                new FilePickerFileType("Achievements dump (*.txt)") { Patterns = ["*.txt"] },
                FilePickerFileTypes.All,
            ],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try
        {
            var achievements = AchievementsImport.Parse(File.ReadLines(path));
            var (matches, unmatched, autoGranted) =
                AchievementsImport.SkyRewards(achievements, _settings.SkyQuestChecklist);
            // The same dump carries the Conqueror sections — the Raids card's memory
            // of clears from before EQBuddy. Marking is add-only and idempotent, so
            // it needs no preview step of its own.
            _raidLedger.MarkAchievements(achievements);
            ShowAchievementsPreview(matches, unmatched, autoGranted, achievements.Count);
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            AlertTile.ShowAlert($"Couldn't read that file — {ex.Message}");
        }
    }

    private void ShowAchievementsPreview(List<SkyRewardMatch> matches, List<string> unmatched,
        List<string> autoGranted, int total)
    {
        var win = new Window
        {
            Title = "Import achievements — preview",
            Width = 460, Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = AppTheme.BgBrush,
        };
        var panel = new StackPanel { Margin = new Thickness(10) };
        void Add(string text, IBrush brush, bool bold = false) =>
            panel.Children.Add(new TextBlock
            {
                Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 1),
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = brush,
            });

        var fresh = matches.Where(m =>
            !IsSkyRewardCompleted(m.ClassName, m.Reward)).ToList();
        Add($"{total} achievements read · {matches.Count} Sky rewards recognized", AppTheme.TextBrush, bold: true);
        Add(fresh.Count > 0
            ? $"{fresh.Count} will be marked turned-in (the rest already are):"
            : "Everything recognized is already marked — nothing to apply.", AppTheme.TextBrush);
        foreach (var m in matches)
        {
            var already = !fresh.Contains(m);
            Add($"  ✓ {m.ClassName} — {m.Reward}" + (already ? "   (already marked)" : ""),
                already ? AppTheme.DimBrush : AppTheme.GoodBrush);
        }
        if (autoGranted.Count > 0)
        {
            Add($"Skipped — auto-granted, not earned ({autoGranted.Count}):", AppTheme.WarnBrush, bold: true);
            Add("Your primary class unlock is granted at creation, and the game marks its " +
                "reward criteria complete without the items ever existing (#101) — so these " +
                "prove nothing and are never imported. Turn them in for real and the Sky " +
                "card tracks them the normal way.", AppTheme.DimBrush);
            foreach (var g in autoGranted) Add($"  ⊘ {g}", AppTheme.DimBrush);
        }
        if (unmatched.Count > 0)
        {
            Add($"Completed in the file but not recognized ({unmatched.Count}) — left untouched; " +
                "tell the discussions board and matching improves:", AppTheme.WarnBrush, bold: true);
            foreach (var u in unmatched) Add($"  ? {u}", AppTheme.DimBrush);
        }
        Add("Applying only ADDS: nothing currently tracked gets unchecked.", AppTheme.DimBrush);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10),
        };
        var apply = ZoneTheming.Button($"Apply ({fresh.Count})", isDefault: true);
        apply.IsEnabled = fresh.Count > 0;
        apply.Click += (_, _) =>
        {
            AchievementsImport.Apply(matches, _settings);
            _settings.Save();
            UpdateSkyQuestHeaderOnly();
            _skyQuestDirty = true;
            win.Close();
        };
        var cancel = ZoneTheming.Button("Cancel", isCancel: true);
        cancel.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        win.Content = root;
        cancel.Click += (_, _) => win.Close();
        win.ShowDialog(this);
    }

    private readonly TextBlock _skyQuestHeader = AppTheme.StatValue("0/0");

    private void UpdateSkyQuestHeaderOnly()
    {
        var total = _settings.SkyQuestChecklist.Count;
        var acquired = _settings.SkyQuestChecklist.Count(i => i.Acquired);
        _skyQuestHeader.Text = $"{acquired}/{total}";
    }

    private void UpdateSkyQuestChecklist(StatsSnapshot s)
    {
        var changed = AutoCheckSkyQuestLoot(s);
        UpdateSkyQuestHeaderOnly();
        if (changed)
        {
            _skyQuestDirty = true;
            _settings.Save();
        }
    }

    private bool AutoCheckSkyQuestLoot(StatsSnapshot s)
    {
        var changed = false;
        // The class-scoping rules live in Core (SkyLootAutoCheck) where they are
        // tested: shared items tick your selected classes / active tab (#98),
        // single-class items tick their class unconditionally (#106 — a Berserker
        // staff looted on the Druid tab is still Berserker progress).
        var myClasses = QuestLedger?.ClassesFor(QuestCharacterKey) ?? [];
        var lootByName = s.Loot
            .GroupBy(l => l.Item, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Count), StringComparer.OrdinalIgnoreCase);

        foreach (var key in _skyQuestLootSeen.Keys.ToList())
            if (!lootByName.ContainsKey(key))
                _skyQuestLootSeen[key] = 0;

        foreach (var (name, count) in lootByName)
        {
            _skyQuestLootSeen.TryGetValue(name, out var seen);
            _skyQuestLootSeen[name] = count;
            if (count <= seen) continue;
            changed |= SkyLootAutoCheck.Apply(_settings.SkyQuestChecklist, name,
                count - seen, myClasses, _settings.SkyQuestClass);
        }

        return changed;
    }

    /// <summary>Sky state lens (David, 2026-08-11): same vocabulary as the quest
    /// tracker's filter — "ready" is the Sky-specific prize: every piece collected,
    /// the turn-in still to make. Session-scoped like the tracker's.</summary>
    private string _skyState = "any state";

    private void OnSkyStateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_skyStateCombo.SelectedItem is not string s) return;
        _skyState = s;
        _skyQuestDirty = true;
    }

    private void OnSkySearchChanged(object? sender, EventArgs e)
    {
        _skyQuestDirty = true;
        RenderSkyQuestChecklist();
    }

    private void RenderSkyQuestChecklist()
    {
        if (_skyStateCombo.Items.Count == 0)
        {
            foreach (var s in new[] { "any state", "open", "ready", "done" }) _skyStateCombo.Items.Add(s);
            _skyStateCombo.SelectedIndex = 0;
        }

        // Search (#108, bjstrange): one box instead of a fourteen-tab tour. Crosses
        // every class and ignores the state lens — filters shape tabs, never search
        // (the tracker's rule since 1.57.4). Clearing the box restores the tabs.
        var query = (_skySearchBox.Text ?? "").Trim();
        _skySearchScroll.IsVisible = query.Length > 0;
        _skyQuestTabs.IsVisible = query.Length == 0;
        if (query.Length > 0)
        {
            RenderSkySearch(query);
            return;
        }
        // Live selection wins; the persisted class restores the tab across restarts.
        var selectedClass = (_skyQuestTabs.SelectedItem as TabItem)?.Tag as string
            ?? (_settings.SkyQuestClass.Length > 0 ? _settings.SkyQuestClass : null);
        _skyQuestTabs.Items.Clear();

        foreach (var classGroup in _settings.SkyQuestChecklist.GroupBy(i => i.ClassName).OrderBy(g => g.Key))
        {
            var classTotal = classGroup.Count();
            var classDone = classGroup.Count(i => i.Acquired);
            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

            // Unfinished quests float to the top (Reddit, 2026-08-11), and within
            // the unfinished, CLOSEST TO DONE leads — the question a tab answers is
            // "which quest is actually in reach". The ✔ rows are trophies and read
            // fine from the bottom of the list.
            foreach (var rewardGroup in classGroup.GroupBy(i => i.Reward)
                         .OrderBy(g => IsSkyRewardCompleted(classGroup.Key, g.Key))
                         .ThenByDescending(g => (double)g.Count(i => i.Acquired) / g.Count())
                         .ThenBy(g => g.Key))
            {
                // The reward line is itself a checkbox: "I turned this in" (#73).
                // Manual only — the log shows nothing reliable at the NPC hand-over.
                var completed = IsSkyRewardCompleted(classGroup.Key, rewardGroup.Key);
                var stateOk = _skyState switch
                {
                    "open" => !completed && rewardGroup.Any(i => !i.Acquired),
                    "ready" => !completed && rewardGroup.All(i => i.Acquired),
                    "done" => completed,
                    _ => true,
                };
                if (!stateOk) continue;
                var rewardItems = rewardGroup.ToList();
                // The header carries the quest's own score — "2/3" says how close
                // without opening anything; "ready" says the running is over.
                var have = rewardItems.Count(i => i.Acquired);
                var progress = completed ? "" : have == rewardItems.Count
                    ? " · ready" : $" · {have}/{rewardItems.Count}";
                var rewardCheck = new CheckBox
                {
                    IsChecked = completed,
                    Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 6, 0, 1),
                    Content = new TextBlock
                    {
                        Text = (completed ? $"✔ {rewardGroup.Key}" : rewardGroup.Key) + progress,
                        FontSize = 11,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = AppTheme.AccentBrush,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                };
                ToolTip.SetTip(rewardCheck, $"{rewardGroup.Key} - {rewardGroup.First().Npc}\n" +
                    "Check when you've turned everything in — quest complete.");
                var (cls, reward) = (classGroup.Key, rewardGroup.Key);
                rewardCheck.IsCheckedChanged += (box, _) =>
                    OnSkyRewardToggled(cls, reward, rewardItems, ((CheckBox)box!).IsChecked == true);
                panel.Children.Add(rewardCheck);

                // Within a quest, what's MISSING leads; what's banked follows.
                foreach (var item in rewardGroup.OrderBy(i => i.Acquired).ThenBy(i => i.QuestItem))
                    panel.Children.Add(SkyItemCheckBox(item, completed));
            }

            if (panel.Children.Count == 0)
                panel.Children.Add(new TextBlock
                {
                    Text = _skyState == "ready"
                        ? "Nothing fully collected yet — \"open\" shows what's still missing."
                        : $"No {_skyState} quests for this class.",
                    FontSize = 11, TextWrapping = TextWrapping.Wrap,
                    Foreground = AppTheme.DimBrush,
                    Margin = new Thickness(0, 4, 0, 4),
                });

            var tab = new TabItem
            {
                Header = $"{ClassAbbrev(classGroup.Key)} {classDone}/{classTotal}",
                Tag = classGroup.Key,
                FontSize = 11,
                Content = new ScrollViewer
                {
                    Content = panel,
                    MaxHeight = SkyQuestListMaxHeight(),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Padding = new Thickness(0, 0, 4, 0),
                },
            };
            ToolTip.SetTip(tab, classGroup.Key);
            _skyQuestTabs.Items.Add(tab);
            if (string.Equals(selectedClass, classGroup.Key, StringComparison.Ordinal))
                _skyQuestTabs.SelectedItem = tab;
        }

        if (_skyQuestTabs.SelectedIndex < 0 && _skyQuestTabs.Items.Count > 0)
            _skyQuestTabs.SelectedIndex = 0;
    }

    private CheckBox SkyItemCheckBox(SkyQuestChecklistItem item, bool completed)
    {
        var text = new StackPanel();
        // * = the auto-tick parked a multi-class item here because no class lens
        // claimed it (#106) — the player decides where it belongs.
        text.Children.Add(new TextBlock
        {
            Text = item.AcquiredUnassigned ? item.QuestItem + " *" : item.QuestItem,
            FontSize = 12,
            Foreground = AppTheme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = item.Source,
            FontSize = 10,
            Foreground = AppTheme.DimBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var tip = $"{item.Reward}: {item.QuestItem} ({item.Source})";
        if (item.AcquiredUnassigned)
        {
            var others = _settings.SkyQuestChecklist
                .Where(i => i.QuestItem.Equals(item.QuestItem, StringComparison.OrdinalIgnoreCase)
                         && !i.ClassName.Equals(item.ClassName, StringComparison.OrdinalIgnoreCase))
                .Select(i => i.ClassName).Distinct().ToList();
            tip += "\n* Auto-ticked here, but this item is also wanted by: "
                + string.Join(", ", others)
                + ". Untick it and tick the right class if this guess is wrong.";
        }

        var check = new CheckBox
        {
            IsChecked = item.Acquired,
            Content = text,
            Margin = new Thickness(0, 1, 0, 1),
            // A completed quest's items are history, not a to-do list.
            IsEnabled = !completed,
            Opacity = completed ? 0.55 : 1.0,
        };
        ToolTip.SetTip(check, tip);
        check.IsCheckedChanged += (box, _) => OnSkyQuestToggled(item, ((CheckBox)box!).IsChecked == true);
        return check;
    }

    /// <summary>The search view: matching checklist rows across EVERY class, grouped
    /// by item so "who wants this drop?" is answered in one glance — each class's row
    /// keeps its live checkbox, with completed quests read-only exactly like the tabs.</summary>
    private void RenderSkySearch(string query)
    {
        _skySearchScroll.MaxHeight = SkyQuestListMaxHeight();
        _skySearchResults.Children.Clear();
        var matches = _settings.SkyQuestChecklist
            .Where(i => i.QuestItem.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || i.Reward.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            _skySearchResults.Children.Add(new TextBlock
            {
                Text = $"Nothing in Sky wants \"{query}\" — searched every class's tests.",
                FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Foreground = AppTheme.DimBrush,
                Margin = new Thickness(0, 4, 0, 4),
            });
            return;
        }

        foreach (var itemGroup in matches.GroupBy(i => i.QuestItem, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var first = itemGroup.First();
            _skySearchResults.Children.Add(new TextBlock
            {
                Text = itemGroup.Key,
                FontSize = 12, FontWeight = FontWeight.SemiBold,
                Foreground = AppTheme.AccentBrush,
                Margin = new Thickness(0, _skySearchResults.Children.Count == 0 ? 2 : 8, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (first.Source.Length > 0)
                _skySearchResults.Children.Add(new TextBlock
                {
                    Text = first.Source, FontSize = 10,
                    Foreground = AppTheme.DimBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });

            foreach (var item in itemGroup.OrderBy(i => i.ClassName, StringComparer.OrdinalIgnoreCase))
            {
                var completed = IsSkyRewardCompleted(item.ClassName, item.Reward);
                var check = new CheckBox
                {
                    IsChecked = item.Acquired,
                    Margin = new Thickness(8, 1, 0, 1),
                    IsEnabled = !completed,
                    Opacity = completed ? 0.55 : 1.0,
                    Content = new TextBlock
                    {
                        Text = $"{ClassAbbrev(item.ClassName)} · {item.Reward}{(completed ? " ✔" : "")}",
                        FontSize = 11.5,
                        Foreground = AppTheme.TextBrush,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                };
                ToolTip.SetTip(check, $"{item.ClassName} — {item.Reward} ({item.Npc})");
                var captured = item;
                check.IsCheckedChanged += (box, _) => OnSkyQuestToggled(captured, ((CheckBox)box!).IsChecked == true);
                _skySearchResults.Children.Add(check);
            }
        }
    }

    private double SkyQuestListMaxHeight()
    {
        var available = _sectionScroll.MaxHeight > 0 ? _sectionScroll.MaxHeight - 220 : 260;
        return Math.Clamp(available, 180, 320);
    }

    /// <summary>Reward turned in (#73): completing checks the reward's items too —
    /// they were acquired and then handed over. Unchecking reopens the quest but
    /// leaves the item boxes as they were; the player knows what they still hold.</summary>
    private void OnSkyRewardToggled(string className, string reward,
        List<SkyQuestChecklistItem> items, bool done)
    {
        var key = SkyRewardKey(className, reward);
        if (done)
        {
            if (!_settings.SkyQuestCompleted.Contains(key)) _settings.SkyQuestCompleted.Add(key);
            foreach (var i in items) i.Acquired = true;
        }
        else
        {
            _settings.SkyQuestCompleted.Remove(key);
        }
        _settings.Save();
        UpdateSkyQuestHeaderOnly();
        _skyQuestDirty = true;   // rebuild next tick: ✔ label, dimmed items, counts
    }

    /// <summary>Manual toggle: the box itself is already right, so only the counts
    /// need refreshing — no rebuild, the control under the cursor stays put.</summary>
    private void OnSkyQuestToggled(SkyQuestChecklistItem item, bool acquired)
    {
        item.Acquired = acquired;
        // The player deciding IS the resolution of an auto-parked tick (#106) —
        // whichever way they toggled, the * has served its purpose.
        item.AcquiredUnassigned = false;
        _settings.Save();
        UpdateSkyQuestHeaderOnly();
        UpdateSkyQuestTabHeader(item.ClassName);
    }

    /// <summary>Persist the class tab the player works in — it scopes loot auto-check
    /// and picks the tab shown after a restart.</summary>
    private void OnSkyQuestTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Items.Clear() during a rebuild fires this with no selection — ignore.
        if ((_skyQuestTabs.SelectedItem as TabItem)?.Tag is string cls &&
            !string.Equals(_settings.SkyQuestClass, cls, StringComparison.Ordinal))
        {
            _settings.SkyQuestClass = cls;
            _settings.Save();
        }
    }

    private void UpdateSkyQuestTabHeader(string className)
    {
        foreach (var tab in _skyQuestTabs.Items.OfType<TabItem>())
            if (string.Equals(tab.Tag as string, className, StringComparison.Ordinal))
            {
                var done = _settings.SkyQuestChecklist.Count(i =>
                    string.Equals(i.ClassName, className, StringComparison.Ordinal) && i.Acquired);
                var total = _settings.SkyQuestChecklist.Count(i =>
                    string.Equals(i.ClassName, className, StringComparison.Ordinal));
                tab.Header = $"{ClassAbbrev(className)} {done}/{total}";
            }
    }

    private static string ClassAbbrev(string className) => className switch
    {
        "Bard" => "BRD",
        "Beastlord" => "BST",
        "Berserker" => "BER",
        "Cleric" => "CLR",
        "Druid" => "DRU",
        "Enchanter" => "ENC",
        "Magician" => "MAG",
        "Monk" => "MNK",
        "Necromancer" => "NEC",
        "Paladin" => "PAL",
        "Ranger" => "RNG",
        "Rogue" => "ROG",
        "Shadow Knight" => "SHD",
        "Shaman" => "SHM",
        "Warrior" => "WAR",
        "Wizard" => "WIZ",
        _ => className,
    };

    // ---- the imported gear checklist card (#113) ----

    internal void ImportGearChecklist(GearChecklistImportResult import)
    {
        _settings.GearChecklist = import.Items;
        _settings.GearChecklistName = import.Name;
        _settings.Save();
        RefreshGearCard();
    }

    internal void ClearGearChecklist()
    {
        _settings.GearChecklist.Clear();
        _settings.GearChecklistName = "";
        _settings.Save();
        RefreshGearCard();
    }

    /// <summary>The Options window's hook after an import/clear: re-render now, not
    /// on the next event.</summary>
    internal void RefreshGearCard()
    {
        _gearChecklistDirty = true;
        UpdateGearHeaderOnly();
        RefreshUi();
    }

    private void RenderGearChecklist()
    {
        _gearChecklistPanel.Children.Clear();
        var total = _settings.GearChecklist.Count;
        if (total == 0)
        {
            _gearListName.Text = "Import an EQ Legends Tools shopping-list HTML in Options.";
            _gearChecklistPanel.Children.Add(EmptyCardLine("No gear list imported."));
            UpdateGearHeaderOnly();
            return;
        }

        UpdateGearListName();
        foreach (var item in _settings.GearChecklist)
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = item.Slot,
                FontSize = 10,
                Foreground = AppTheme.DimBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            text.Children.Add(new TextBlock
            {
                Text = item.Item,
                FontSize = 12,
                Foreground = AppTheme.TextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (item.Source.Length > 0)
                text.Children.Add(new TextBlock
                {
                    Text = item.Source,
                    FontSize = 10,
                    Foreground = AppTheme.DimBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });

            var tip = $"{item.Slot}: {item.Item}";
            if (item.Source.Length > 0) tip += "\n" + item.Source;
            if (item.Url.Length > 0) tip += "\n" + item.Url;
            var check = new CheckBox
            {
                IsChecked = item.Acquired,
                Content = text,
                Margin = new Thickness(0, 2, 0, 2),
            };
            ToolTip.SetTip(check, tip);
            var captured = item;
            check.IsCheckedChanged += (box, _) => OnGearToggled(captured, ((CheckBox)box!).IsChecked == true);
            _gearChecklistPanel.Children.Add(check);
        }

        UpdateGearHeaderOnly();
    }

    private void OnGearToggled(GearChecklistItem item, bool acquired)
    {
        item.Acquired = acquired;
        _settings.Save();
        UpdateGearHeaderOnly();
        UpdateGearListName();
    }

    private void UpdateGearListName()
    {
        var total = _settings.GearChecklist.Count;
        var done = _settings.GearChecklist.Count(i => i.Acquired);
        _gearListName.Text = _settings.GearChecklistName.Length > 0
            ? $"{_settings.GearChecklistName} - {done}/{total}"
            : $"{done}/{total} imported gear pieces";
    }

    private void UpdateGearHeaderOnly()
    {
        var total = _settings.GearChecklist.Count;
        var acquired = _settings.GearChecklist.Count(i => i.Acquired);
        _gearHeader.Text = $"{acquired}/{total}";
    }

    private static string SkyRewardKey(string className, string reward) => className + "|" + reward;

    private bool IsSkyRewardCompleted(string className, string reward) =>
        _settings.SkyQuestCompleted.Contains(SkyRewardKey(className, reward));

    private void OnOpenWebsite(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                "https://github.com/DranakCorps-bot/EQBuddy") { UseShellExecute = true });
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    private void OnHistory(object? sender, EventArgs e)
    {
        _archiver.CheckpointSync(CurrentSnapshot());
        if (_historyWindow is null)
        {
            var window = new HistoryWindow(_repo, _settings);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_historyWindow, window)) _historyWindow = null;
            };
            _historyWindow = window;
        }
        _historyWindow.Show();
        _historyWindow.Activate();
    }

    private void DropCampMarker()
    {
        var s = CurrentSnapshot();
        _stats.AddMarker($"Marker {s.Markers.Count + 1}" +
            (s.CurrentZone.Length > 0 ? $" - {s.CurrentZone}" : ""));
    }

    // Global hotkeys removed 2026-08-06 (Reddit: system-wide registration ate common
    // browser shortcuts like Ctrl+Shift+T). Click-through's trigger is now the context
    // menu + the amber 🔒 unlock chip, mirroring WPF (#7): a menu can't be reached
    // through a transparent window, so the chip is the one solid thing left to click.

    /// <summary>Menu toggle for click-through. Engages only if the platform call actually
    /// succeeds — on Wayland, a missing XFixes, or a backend with no implementation the
    /// state must not flip, or the menu would lie about what clicks do (the backend
    /// logs why).</summary>
    private void SetClickThrough(bool on)
    {
        if (on && !ClickThrough.Set(this, enabled: true)) return;
        if (!on) ClickThrough.Set(this, enabled: false);
        _clickThrough = on;
        _root.BorderBrush = on ? AppTheme.WarnBrush : AppTheme.HairlineBrush;
        ToolTip.SetTip(_root, on ? "Click-through ON — click the \U0001F512 chip to interact again" : null);
        _clickThroughItem.Header = (on ? "✓ " : "") + "Click-through (game clicks pass through)";
        if (on)
        {
            _unlockChip ??= new ClickThroughChip(() => SetClickThrough(false));
            _unlockChip.ShowNear(this);
        }
        else
        {
            _unlockChip?.Hide();
        }
    }

    /// <summary>
    /// Keeps every EQBuddy window above a fullscreen CrossOver game on macOS. Topmost alone
    /// is not enough — see <see cref="MacOverlayLevel"/> — and the raise is scoped to the
    /// game being frontmost so EQBuddy does not sit over every other app's fullscreen.
    /// </summary>
    private static void EnsureOverlayLevel()
    {
        if (OperatingSystem.IsMacOS()) MacOverlayLevel.Update();
    }

    // ---- hide while the game is unfocused / not running (FOCUS-*, #41 / #114) ----

    private bool _hiddenForFocus;
    private bool _focusProbeUnsupportedLogged;
    private bool _fgProbeFailureLogged;
    // Perf audit #6: the foreground answer is memoized per HWND (same window in
    // front → same verdict), and "is the game running" refreshes at most every 5 s.
    private (IntPtr Fg, bool IsGame) _lastFgProbe = (IntPtr.Zero, false);
    private (DateTime At, bool Running) _lastGameProbe = (DateTime.MinValue, false);

    private void UpdateFocusHide()
    {
        var hide = ShouldHideForFocus();
        if (hide == _hiddenForFocus) return;
        _hiddenForFocus = hide;
        if (hide) Hide();
        else Show();
    }

    /// <summary>Two opt-ins share this gate (#41 unfocused / #114 not running); the
    /// actual decision lives in UI.Shared.FocusHide where tests reach it. Everything
    /// here is per-OS probe plumbing, degrading to "always visible" where the
    /// platform can't answer (logged once).</summary>
    private bool ShouldHideForFocus()
    {
        if (!_settings.HideWhenGameUnfocused && !_settings.HideWhenGameNotRunning) return false;
        if (OperatingSystem.IsWindows()) return ShouldHideForFocusWindows();
        if (OperatingSystem.IsMacOS()) return ShouldHideForFocusMac();
        // X11/Wayland: there is no portable foreground-window probe; hiding on a
        // wrong guess would strand the overlay, so it stays visible.
        if (!_focusProbeUnsupportedLogged)
        {
            _focusProbeUnsupportedLogged = true;
            App.LogError("Hide-when-game-unfocused/not-running: no foreground probe " +
                "on this platform; the overlay stays visible.");
        }
        return false;
    }

    private bool ShouldHideForFocusWindows()
    {
        var fg = FocusNative.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        FocusNative.GetWindowThreadProcessId(fg, out var fgPid);
        if (fgPid == (uint)Environment.ProcessId) return false;
        if (fg != _lastFgProbe.Fg)
        {
            bool isGame;
            try
            {
                using var p = Process.GetProcessById((int)fgPid);
                isGame = p.ProcessName.Equals("eqgame", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                // Foreground process already gone — don't flicker. Expected in a race,
                // but logged once so a probe that fails EVERY tick isn't invisible.
                if (!_fgProbeFailureLogged)
                {
                    _fgProbeFailureLogged = true;
                    App.LogError($"Foreground-process probe failed (logged once): {ex}");
                }
                return false;
            }
            _lastFgProbe = (fg, isGame);
        }
        if (_lastFgProbe.IsGame) return false;

        if (DateTime.Now - _lastGameProbe.At > TimeSpan.FromSeconds(5))
            _lastGameProbe = (DateTime.Now, EqConfig.IsGameRunning());
        return EQBuddy.UI.Shared.FocusHide.Decide(
            _settings.HideWhenGameUnfocused, _settings.HideWhenGameNotRunning,
            foregroundIsSelf: false, foregroundIsGame: false, _lastGameProbe.Running);
    }

    /// <summary>macOS: the CrossOver frontmost-app detection MacOverlayLevel already
    /// does answers "is the game in front". "Is the game running while backgrounded"
    /// has no honest probe under a Wine bottle, so a frontmost sighting is remembered
    /// as running for the probe window — degrading toward visible, never toward a
    /// wrongly hidden overlay.</summary>
    private bool ShouldHideForFocusMac()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.Windows.Any(w => w.IsActive))
            return false;   // the player is using EQBuddy itself
        var wineFront = MacOverlayLevel.IsWineHostFrontmost();
        if (wineFront is null) return false;   // unidentifiable frontmost app — stay visible
        var fgIsGame = wineFront == true;
        if (fgIsGame || DateTime.Now - _lastGameProbe.At > TimeSpan.FromSeconds(5))
            _lastGameProbe = (DateTime.Now, fgIsGame || EqConfig.IsGameRunning());
        return EQBuddy.UI.Shared.FocusHide.Decide(
            _settings.HideWhenGameUnfocused, _settings.HideWhenGameNotRunning,
            foregroundIsSelf: false, fgIsGame, _lastGameProbe.Running);
    }

    private static class FocusNative
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public static readonly IntPtr HwndTopmost = new(-1);
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoMove = 0x0002;
        public const uint SwpNoActivate = 0x0010;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);
    }

    /// <summary>Every EQBuddy surface is Topmost, but Windows keeps topmost windows
    /// in the order they claimed the band — an overlay created AFTER ours (Lossless
    /// Scaling's upscale surface was the field case, discussion #91) sits above the
    /// widget and nothing re-asserts on its own. A periodic no-activate re-place
    /// lifts every visible EQBuddy window back to the top of the band. macOS gets the
    /// same effect from MacOverlayLevel.Update every tick; X11 stacking belongs to
    /// the window manager, so no re-place there.</summary>
    private const int TopmostReassertSeconds = 5;
    private int _topmostTick;

    private void ReassertTopmost()
    {
        if (!_settings.KeepAboveOverlays) return;   // #91: opt out for capture setups
        if (++_topmostTick < TopmostReassertSeconds) return;
        _topmostTick = 0;
        if (!OperatingSystem.IsWindows()) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        foreach (var w in desktop.Windows)
        {
            if (!w.Topmost || !w.IsVisible) continue;
            if (w.TryGetPlatformHandle() is { Handle: not 0 } handle)
                FocusNative.SetWindowPos(handle.Handle, FocusNative.HwndTopmost, 0, 0, 0, 0,
                    FocusNative.SwpNoMove | FocusNative.SwpNoSize | FocusNative.SwpNoActivate);
        }
    }

    /// <summary>
    /// Someone launched a second EQBuddy. Surface this one instead — which is almost
    /// certainly what they wanted, since the usual reason to relaunch is that the widget
    /// is hidden or buried behind a fullscreen game.
    /// </summary>
    internal void RestoreFromAnotherInstance()
    {
        try
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            // Clear the hide state DIRECTLY — relying on Activate() winning foreground
            // left a visible-but-frozen widget when the OS refused the focus switch:
            // RefreshUi gates on this flag, so a stale true froze stats and kept
            // satellites hidden. An explicit show IS the user's choice.
            _hiddenForFocus = false;
            Topmost = true;
            Activate();
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    private void OnGear(object? sender, EventArgs e) => _root.ContextMenu?.Open(_root);

    private void OnStarChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var btn = (Button)sender!;
        var key = (string)btn.Tag!;
        if (_settings.MiniStats.Contains(key))
        {
            _settings.MiniStats.Remove(key);
        }
        else
        {
            _settings.MiniStats.Add(key);
        }
        UpdateStarVisuals();
        _settings.Save();
    }

    private void UpdateStarVisuals()
    {
        foreach (var star in _stars.Values)
        {
            var isSelected = _settings.MiniStats.Contains((string)star.Tag!);
            star.Content = AppTheme.Icon(isSelected ? AppIcon.StarFilled : AppIcon.Star, isSelected ? AppTheme.AccentBrush : AppTheme.DimBrush, 13);
        }
    }

    private void CheckForUpdates(bool manual)
    {
        Task.Run(async () =>
        {
            var folder = UpdateChecker.FindUpdateFolder(_settings.UpdateFolder);
            var info = await UpdateChecker.FindBestAsync(_settings.UpdateFolder);
            Dispatcher.UIThread.Post(() =>
            {
                if (_installingUpdate) return;
                if (info is not null && UpdateChecker.IsNewer(info))
                {
                    _pendingUpdate = info;
                    _updateText.Text = UpdateOffer.OfferText(info, OperatingSystem.IsWindows(),
                        UpdateChecker.IsInstalledCopy);
                    _updateBanner.IsVisible = true;
                }
                else if (manual)
                {
                    _pendingUpdate = null;
                    _updateText.Text = info is null && folder is null
                        ? "Couldn't check for updates (no update folder, GitHub unreachable)."
                        : $"You're up to date (v{UpdateChecker.CurrentVersion}).";
                    _updateBanner.IsVisible = true;
                    _upToDateNoticeUntil = DateTime.Now.AddSeconds(6);
                }
            });
        });
    }

    /// <summary>Freedesktop sound-theme equivalents of the shared palette. The NAMES
    /// are owned by <see cref="EQBuddy.UI.Shared.AlertSoundCatalog"/> (one list, both
    /// UIs and every picker); only the per-platform file mapping lives here.</summary>
    internal static readonly (string Name, string File)[] AlertSounds =
    [
        ("Ding", "bell.oga"),
        ("Notify", "message-new-instant.oga"),
        ("Chimes", "service-login.oga"),
        ("Chord", "device-added.oga"),
        ("Tada", "complete.oga"),
        ("Exclamation", "dialog-warning.oga"),
        ("Alarm", "alarm-clock-elapsed.oga"),
    ];

    internal void PlayAlertSound() => PlayAlertSound(_settings.AlertSound);

    /// <summary>
    /// Play a specific sound: a built-in name, or the full path of a custom file. The
    /// argument exists so per-rule sounds work — the point of giving each rule its own sound
    /// is telling them apart by ear, which a single shared sound can't do.
    /// With <paramref name="coalesce"/> on, sounds within <see cref="EQBuddy.UI.Shared.SoundGate.Window"/>
    /// of the last are dropped — several rules firing together are one audio alert (here they
    /// would literally overlap, one player process per sound). Previews keep coalesce off.
    /// </summary>
    internal void PlayAlertSound(string choiceOrPath, bool coalesce = false)
    {
        if (coalesce && !_soundGate.TryClaim(DateTime.Now)) return;
        try
        {
            // Legacy SystemSounds values map through the shared catalog — one
            // normalization for both UIs and every picker.
            var choice = EQBuddy.UI.Shared.AlertSoundCatalog.Normalize(choiceOrPath);
            var named = Array.Find(AlertSounds, x => x.Name == choice);
            var file = named.File is { } systemFile
                ? FindBuiltInSound(choice, systemFile)
                : choice;
            // Sound themes are not required to carry every freedesktop event. A named
            // built-in should still make noise when its preferred clip is absent.
            if (file.Length == 0 && named.File is not null)
                file = FindBuiltInSound("Ding", "bell.oga");
            if (file.Length > 0 && File.Exists(file))
            {
                var volume = Math.Clamp(_settings.AlertVolume, 0.0, 1.0);
                _ = Task.Run(() => PlaySoundFile(file, volume));
                return;
            }
            App.LogError($"Alert sound file was not found: {choiceOrPath}");
            Console.Beep();
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    /// <summary>macOS ships no freedesktop sound theme, so every built-in resolved to
    /// nothing there and alerts were silent (#93, pmcginn). Map the seven built-ins onto
    /// the system clips every Mac has instead; the names are chosen for character, not
    /// literal translation (Chimes has no freedesktop twin, Glass is the closest ear).</summary>
    private static readonly Dictionary<string, string> MacSounds = new()
    {
        ["Ding"] = "Ping", ["Notify"] = "Glass", ["Chimes"] = "Blow", ["Chord"] = "Pop",
        ["Tada"] = "Hero", ["Exclamation"] = "Sosumi", ["Alarm"] = "Submarine",
    };

    private static string FindBuiltInSound(string name, string desktopFile)
    {
        if (!OperatingSystem.IsMacOS()) return FindDesktopSound(desktopFile);
        var clip = MacSounds.GetValueOrDefault(name, "Ping");
        foreach (var dir in new[]
                 {
                     System.IO.Path.Combine(Environment.GetFolderPath(
                         Environment.SpecialFolder.UserProfile), "Library", "Sounds"),
                     "/Library/Sounds", "/System/Library/Sounds",
                 })
        {
            var path = System.IO.Path.Combine(dir, clip + ".aiff");
            if (File.Exists(path)) return path;
        }
        // A Mac missing even Ping.aiff is unheard of, but fall back rather than go silent.
        var ping = "/System/Library/Sounds/Ping.aiff";
        return File.Exists(ping) ? ping : "";
    }

    private static string FindDesktopSound(string fileName)
    {
        var dataDirs = new List<string>();
        var userData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(userData))
            dataDirs.Add(userData);
        else
            dataDirs.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"));

        var systemData = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        dataDirs.AddRange(string.IsNullOrWhiteSpace(systemData)
            ? ["/usr/local/share", "/usr/share"]
            : systemData.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var dataDir in dataDirs)
        {
            var path = System.IO.Path.Combine(dataDir, "sounds", "freedesktop", "stereo", fileName);
            if (File.Exists(path)) return path;
        }

        // Ubuntu, Fedora, and desktop environments often install the clip only in the
        // active theme (Yaru, Oxygen, etc.). Prefer freedesktop above for consistency,
        // then accept the same event from any installed theme.
        foreach (var dataDir in dataDirs)
        {
            var sounds = System.IO.Path.Combine(dataDir, "sounds");
            if (!Directory.Exists(sounds)) continue;
            try
            {
                var match = Directory.EnumerateFiles(sounds, fileName, SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (match is not null) return match;
            }
            catch { /* an unreadable theme must not prevent the remaining locations */ }
        }
        return "";
    }

    /// <summary>Try the platform's audio backends in order and verify their exit status.
    /// Merely starting pw-play is not success: it can launch and immediately fail to
    /// connect or decode an .oga file, which used to swallow the alert without trying
    /// paplay. macOS gets afplay first — the Linux list is not installed there, so alerts
    /// were silently falling through to a Console.Beep the platform ignores (#93,
    /// pmcginn: "can't get sound working").</summary>
    private static void PlaySoundFile(string file, double volume)
    {
        // Each backend expresses volume differently. afplay takes a 0..1 (and beyond)
        // scalar, Canberra uses decibels, PipeWire a 0..1 scalar, PulseAudio 0..65536.
        // ALSA's aplay has no per-stream volume, so it remains the last-resort fallback.
        var decibels = volume <= 0 ? -100 : 20 * Math.Log10(volume);
        var players = OperatingSystem.IsMacOS()
            ? [("afplay", new[] { "-v", $"{volume:0.###}", file })]
            : new (string Command, string[] Args)[]
        {
            ("canberra-gtk-play", ["--volume", $"{decibels:0.##}", "--file", file]),
            ("pw-play", ["--volume", $"{volume:0.###}", file]),
            ("paplay", ["--volume", $"{(int)Math.Round(volume * 65536)}", file]),
            ("aplay", [file]),
        };
        foreach (var (command, args) in players)
            if (TryPlay(command, args)) return;

        try { Console.Beep(); }
        catch { }
        App.LogError($"Alert sound could not be played by any available backend: {file}");
    }

    private static bool TryPlay(string command, IReadOnlyList<string> args)
    {
        try
        {
            var start = new ProcessStartInfo(command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process is null) return false;
            _ = process.StandardError.ReadToEndAsync(); // drain it so a noisy failure cannot block WaitForExit
            if (!process.WaitForExit(10_000))
            {
                try { process.Kill(entireProcessTree: true); }
                catch { }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    private void OnUpdateBannerClick(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (_pendingUpdate is not { } info || _installingUpdate) return;

        if (!UpdateOffer.CanAutoInstall(info, OperatingSystem.IsWindows(), UpdateChecker.IsInstalledCopy))
        {
            var target = UpdateOffer.BrowserTarget(info, OperatingSystem.IsWindows());
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                _pendingUpdate = null;
                _updateText.Text = UpdateOffer.OpenedText(info, OperatingSystem.IsWindows(),
                    UpdateChecker.IsInstalledCopy);
                _upToDateNoticeUntil = DateTime.Now.AddSeconds(10);
            }
            catch (Exception ex)
            {
                App.LogError(ex);
                // A URL the user must retype should be the short release page, even when
                // the click would have gone straight to the tarball asset.
                _updateText.Text = $"Couldn't open browser - visit {UpdateChecker.GitHubLatestPage}";
            }
            return;
        }
        _installingUpdate = true;
        _updateText.Text = info.DownloadUrl is not null
            ? "Downloading update - EQBuddy will restart itself..."
            : "Installing update - EQBuddy will restart itself...";
        Task.Run(async () =>
        {
            try
            {
                var staged = await UpdateChecker.StageForInstall(info);
                Process.Start(staged, "/SILENT");
                Dispatcher.UIThread.Post(Shutdown);
            }
            catch (Exception ex)
            {
                App.LogError(ex);
                Dispatcher.UIThread.Post(() =>
                {
                    _installingUpdate = false;
                    _updateText.Text = "Update failed to start - see error.log.";
                });
            }
        });
    }

    /// <summary>Per-spell resist tallies as a row-lookup dict (session-scoped; empty →
    /// null so rows skip the lookup entirely). BreakoutWindow consumes this too — WPF
    /// keeps it on MainWindow, breakouts borrow it.</summary>
    internal static IReadOnlyDictionary<string, (int Casts, int Resists)>? SpellResistLookup(
        StatsSnapshot s) =>
        s.SpellResists.Count == 0
            ? null
            : s.SpellResists.ToDictionary(x => x.Spell, x => (x.Casts, x.Resists),
                StringComparer.OrdinalIgnoreCase);

    /// <summary>Card lists cap at 30 rows with a spoken overflow line (David's field
    /// report: a long session's Combat card built EVERY ability row ever seen — procs
    /// and clickies included — and first-expand paid seconds of layout for rows below
    /// the fold). Sorting still surfaces anything; breakouts and History stay uncapped.</summary>
    private const int CardRowCap = 30;

    /// <summary>Details!-style breakdown, delegated to the shared BreakdownRows (one
    /// row layout for cards, breakouts, and History — WPF's move when the breakout
    /// windows grew sort bars).</summary>
    private static void FillBreakdown(Panel list, IEnumerable<SourceDamage> stats,
        StatSort sort, double combatSeconds, string rateLabel,
        IReadOnlyDictionary<string, (int Casts, int Resists)>? resists = null) =>
        BreakdownRows.FillAbilityRowsSorted(list, stats, sort, combatSeconds, rateLabel,
            CardRowCap, resists);

    private static Grid BarRow(string name, string value, double fraction, IBrush barBrush, string? tooltip)
    {
        fraction = Math.Clamp(fraction, 0.004, 1.0);
        var row = new Grid
        {
            Margin = new Thickness(0, 1, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var bar = new Border
        {
            Background = barBrush,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
        };
        row.SizeChanged += (_, args) => bar.Width = Math.Max(0, args.NewSize.Width * fraction);
        row.Children.Add(bar);

        var content = new Grid { Margin = new Thickness(4, 1, 0, 1) };
        content.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        content.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        content.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = AppTheme.TextBrush,
        });
        var right = new TextBlock
        {
            Text = value,
            FontSize = 11,
            Foreground = AppTheme.DimBrush,
            Margin = new Thickness(8, 1, 2, 0),
        };
        Grid.SetColumn(right, 1);
        content.Children.Add(right);
        row.Children.Add(content);
        if (tooltip is not null) ToolTip.SetTip(row, tooltip);
        return row;
    }

    private static SolidColorBrush AccentBarBrush()
    {
        var accent = ((SolidColorBrush)AppTheme.AccentBrush).Color;
        return new SolidColorBrush(Color.FromArgb(0x2E, accent.R, accent.G, accent.B));
    }

    private void FillStatList(ItemsControl list, IEnumerable<SourceDamage> stats, StatSort sort, string unit)
    {
        var sorted = sort switch
        {
            StatSort.Hits => stats.OrderByDescending(d => d.Hits),
            StatSort.Avg => stats.OrderByDescending(d => (double)d.Total / d.Hits),
            _ => stats.OrderByDescending(d => d.Total),
        };
        FillList(list, sorted.Select(d => (d.Name, $"{d.Total:N0} - {d.Hits} {unit}{(d.Hits == 1 ? "" : "s")} - avg {(double)d.Total / d.Hits:0.#}")));
    }

    private static StatSort ParseSort(object sender) => (string)((TextBlock)sender).Tag! switch
    {
        "hits" => StatSort.Hits,
        "avg" => StatSort.Avg,
        "rate" => StatSort.Rate,
        _ => StatSort.Total,
    };

    private static void SetSortVisual(StatSort mode, TextBlock total, TextBlock hits, TextBlock avg,
        TextBlock? rate = null)
    {
        total.Foreground = mode == StatSort.Total ? AppTheme.AccentBrush : AppTheme.DimBrush;
        hits.Foreground = mode == StatSort.Hits ? AppTheme.AccentBrush : AppTheme.DimBrush;
        avg.Foreground = mode == StatSort.Avg ? AppTheme.AccentBrush : AppTheme.DimBrush;
        if (rate is not null)
            rate.Foreground = mode == StatSort.Rate ? AppTheme.AccentBrush : AppTheme.DimBrush;
    }

    private void OnSortDmgOut(object? sender, PointerPressedEventArgs e)
    {
        _dmgOutSort = ParseSort(sender!);
        SetSortVisual(_dmgOutSort, _dmgOutSortTotal, _dmgOutSortHits, _dmgOutSortAvg, _dmgOutSortDps);
        RefreshUi();
    }

    private void OnSortDmgIn(object? sender, PointerPressedEventArgs e)
    {
        _dmgInSort = ParseSort(sender!);
        SetSortVisual(_dmgInSort, _dmgInSortTotal, _dmgInSortHits, _dmgInSortAvg);
        RefreshUi();
    }

    private void OnSortHeal(object? sender, PointerPressedEventArgs e)
    {
        _healSort = ParseSort(sender!);
        SetSortVisual(_healSort, _healSortTotal, _healSortHits, _healSortAvg, _healSortHps);
        RefreshUi();
    }

    private static readonly FontFamily MonoFamily = new("monospace");

    private void FillList(ItemsControl list, IEnumerable<(string Name, string Value)> rows,
        Func<string, IBrush>? valueBrush = null, Action<string>? onNameClick = null,
        Func<string, string?>? tooltip = null, bool questBadges = false)
    {
        list.ItemsSource = rows.Select(row =>
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var left = new TextBlock
            {
                Text = row.Name,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = AppTheme.TextBrush,
                Margin = new Thickness(0, 1, 8, 1),
            };
            if (tooltip?.Invoke(row.Name) is { Length: > 0 } tip)
            {
                var tipText = new TextBlock
                {
                    Text = tip,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 340,
                    Foreground = AppTheme.TextBrush,
                };
                // Multi-line tips are stat blocks — monospace keeps their columns readable.
                if (tip.Contains('\n')) tipText.FontFamily = MonoFamily;
                ToolTip.SetTip(left, tipText);
            }
            if (onNameClick is not null)
            {
                var itemName = row.Name;
                left.Cursor = new Cursor(StandardCursorType.Hand);
                if (tooltip is null) ToolTip.SetTip(left, "Click for item info (eqlwiki)");
                left.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(left).Properties.IsLeftButtonPressed) return;
                    onNameClick(itemName);
                    e.Handled = true;
                };
            }
            grid.Children.Add(left);
            if (questBadges && IsActiveQuestItem(row.Name))
            {
                // 🗺 next to quest loot → the Quest Tracker, filtered to this item's
                // quests; each card's name opens the wiki walkthrough from there
                // (David's final shape, 2026-08-07: item click = item page, 🗺 = tracker).
                var badgeName = row.Name;
                var badge = new TextBlock
                {
                    Text = "🗺", FontSize = 11, Margin = new Thickness(0, 1, 6, 1),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Foreground = AppTheme.GoodBrush,
                };
                ToolTip.SetTip(badge, "Part of a quest — click for its quest info");
                badge.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(badge).Properties.IsLeftButtonPressed) return;
                    OpenQuestInfoForItem(badgeName);
                    e.Handled = true;
                };
                Grid.SetColumn(badge, 1);
                grid.Children.Add(badge);
            }
            var right = new TextBlock
            {
                Text = row.Value,
                FontSize = 12,
                Foreground = valueBrush?.Invoke(row.Value) ?? AppTheme.DimBrush,
            };
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            return grid;
        }).ToList();
    }

    internal void ShowItemInfo(string itemName)
    {
        if (_itemInfoWindow is null)
        {
            _itemInfoWindow = new ItemInfoWindow(_wikiItems, _settings);
            _itemInfoWindow.Closed += (_, _) => _itemInfoWindow = null;
        }
        _itemInfoWindow.Show(this);
        _itemInfoWindow.Activate();
        _itemInfoWindow.Lookup(itemName);
    }

    private void RenderTargetDrops(StatsSnapshot snapshot)
    {
        var targets = _settings.ShowTargetDrops ? snapshot.CurrentTargets : [];
        if (targets.Count == 0)
        {
            _targetDropsBlock.IsVisible = false;
            return;
        }
        _targetDropsBlock.IsVisible = true;
        foreach (var target in targets)
        {
            if (_targetResults.ContainsKey(target)) continue;
            _targetResults[target] = null;
            _ = LookupTargetAsync(target, snapshot.CurrentZone);
        }

        var observed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var kills = 0;
        foreach (var target in targets)
        {
            var mob = snapshot.Mobs.FirstOrDefault(m =>
                m.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
            if (mob is null) continue;
            kills += mob.Kills;
            foreach (var loot in mob.Loot)
            {
                var name = EqlWikiItemService.NormalizeTitle(loot.Item);
                observed[name] = observed.GetValueOrDefault(name) + loot.Count;
            }
        }

        var rows = new List<(string Name, string Value)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (item, count) in observed.OrderByDescending(pair => pair.Value))
        {
            var rate = targets.Count == 1 && kills > 0 ? $" · {100.0 * count / kills:0}%" : "";
            rows.Add((item, $"{count} this session{rate}"));
            seen.Add(item);
        }
        foreach (var target in targets)
        {
            if (_targetResults.GetValueOrDefault(target)?.Mob is not { } mob) continue;
            foreach (var (item, rarity) in mob.Drops)
                if (seen.Add(EqlWikiItemService.NormalizeTitle(item)))
                    rows.Add((item, rarity.Length > 0 ? rarity : "listed"));
        }

        var extra = Math.Max(0, rows.Count - 14);
        var names = string.Join(" + ", targets.Take(3)) +
            (targets.Count > 3 ? $" +{targets.Count - 3}" : "");
        var state = targets.Count == 1
            ? _targetResults.GetValueOrDefault(targets[0]) switch
            {
                null => "looking up…",
                { State: ItemLookupState.Live } => "LIVE",
                { State: ItemLookupState.Cached, FetchedAt: { } at } => $"CACHED {at:M/d}",
                { State: ItemLookupState.StaleCache, FetchedAt: { } at } => $"STALE {at:M/d}",
                { State: ItemLookupState.Offline } => "OFFLINE",
                _ => "NOT ON WIKI",
            }
            : targets.Any(target => _targetResults.GetValueOrDefault(target) is null)
                ? "looking up…" : "merged pull";
        _targetDropsHeader.Text = $"🎯 Fighting: {names}" +
            (kills > 0 ? $" — {kills} kill{(kills == 1 ? "" : "s")} this session" : "") +
            $" · drops (eqlwiki · {state}{(extra > 0 ? $" · +{extra} more" : "")})";
        FillList(_targetDropsList, rows.Take(14), onNameClick: ShowItemInfo,
            tooltip: n => QuestAwareTooltip(n, ItemHoverStats(n)), questBadges: true);
    }

    private async Task LookupTargetAsync(string target, string zone)
    {
        try
        {
            var result = await _wikiMobs.LookupAsync(target, zone);
            _targetResults[target] = result;
            Dispatcher.UIThread.Post(() => RenderTargetDrops(CurrentSnapshot()));
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    private void OnDrag(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2 && _miniRoot.IsVisible)
        {
            SetMode(false);
            return;
        }
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _uiTimer.Stop();
        _trayIcon?.Dispose();   // a ghost tray icon outliving its process reads as a crash
        _gridOverlay?.Close();
        _cursorRing?.Close();
        foreach (var breakout in _breakouts.Values) breakout.Close();
        // Never let an unmoved fallback overwrite a real saved spot (#117).
        (_settings.WindowLeft, _settings.WindowTop) = WindowPlacement.PositionToPersist(
            _restoredSavedPosition, _placedPosition.X, _placedPosition.Y,
            Position.X, Position.Y, _settings.WindowLeft, _settings.WindowTop);
        _settings.Save();
        if (_clickThrough)
            ClickThrough.Set(this, enabled: false);
        _alertWindow?.Close();
        _spawnPoints.Flush();   // debounced archives; anything missed replays from the log
        _stats.QuestStore?.Flush();   // debounced writers get their last word (audit #3)
        _stats.AaStore?.Flush();
        _stats.Spells.Flush();        // learned spell categories (audit #13, same idiom)
        if (_reviewPath is null)   // a review session is already history (#74)
            _archiver.FinalizeActiveSync(CurrentSnapshot(), "ApplicationExit");
        _watcher.Dispose();
        _repo.Dispose();
        base.OnClosed(e);
        Shutdown();
    }

    private static void Shutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private static Ellipse Dot() => new()
    {
        Width = 9,
        Height = 9,
        Fill = AppTheme.BadBrush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // Takes an already-translucent wash brush (AppTheme.GoodWashBrush/WarnWashBrush)
    // directly rather than deriving one, so a live theme switch repaints it — the brush
    // reference is the same instance AppTheme.Apply mutates in place.
    private static Border Banner(IBrush brush) => new()
    {
        Background = brush,
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(8, 6),
        IsVisible = false,
    };
}
