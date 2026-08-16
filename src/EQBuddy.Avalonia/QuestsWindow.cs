using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Avalonia;

/// <summary>What the Quest Tracker needs from the shell. Mirrors the WPF MainWindow's
/// quest surface member-for-member so the integration pass can implement it on the
/// Avalonia MainWindow verbatim and construct the window with <c>this</c>.</summary>
public interface IQuestsHost
{
    AppSettings Settings { get; }
    QuestCatalog QuestCatalog { get; }
    QuestLedgerStore? QuestLedger { get; }
    ZoneGraph ZoneGraph { get; }
    string QuestCharacterKey { get; }
    string CurrentZoneName { get; }
    StatsSnapshot CurrentSnapshot();
    InventoryFile.Snapshot? LatestInventory(bool refresh = false);
    string? CachedItemStats(string itemName);
    Task<string?> FetchItemTooltip(string itemName);
}

/// <summary>
/// The standalone Quest Tracker (QUEST-*, David's spec 2026-08-07): every wiki quest
/// whose turn-in items overlap what this character owns — looted since the ledger began,
/// or declared via "+ I have this" for pre-EQBuddy inventory. One card per quest,
/// most-complete first; expanding a card lists each item as have/need; the quest name
/// opens the eqlwiki walkthrough. "all quests" flips from the overlap view to the whole
/// catalog for browsing ahead.
/// </summary>
public sealed class QuestsWindow : Window
{
    private readonly IQuestsHost _main;
    private readonly AppSettings _settings;
    private string _signature = "";
    private DateTime _lastRefresh = DateTime.MinValue;
    private string _mode = "mine";   // mine = items+pins · zone = current zone · all
    private bool _restored;
    private PixelPoint _placed;
    /// <summary>The last on-screen position, so Closed never persists a torn-down
    /// window's 0,0 (#169).</summary>
    private LastVisiblePosition _seen;

    private readonly TextBlock _titleText = new()
    {
        Text = "🗺 Quest Tracker", FontWeight = FontWeight.Bold, FontSize = 14,
    };
    private readonly TextBox _addItemBox = InputBox(
        "Item you already have in inventory (from before EQBuddy could see it)");
    private readonly TextBox _addQtyBox = InputBox("How many");
    private readonly ListBox _suggestList = new()
    {
        MaxHeight = 120, FontSize = 12, IsVisible = false,
        Background = AppTheme.ComboBoxBrush, Foreground = AppTheme.TextBrush,
        BorderBrush = AppTheme.BorderBrush,
    };
    private readonly TextBox _filterBox = InputBox(
        "Search the whole catalog — quest names, turn-in items, rewards, quest givers, " +
        "zones. Search ignores the class/era/state filters. Pin 📌 a result to track it.");
    private readonly ComboBox _eraCombo = new() { Width = 104, FontSize = 11 };
    private readonly ComboBox _stateCombo = new() { Width = 86, FontSize = 11 };
    private readonly Button _classBtn;
    private readonly StackPanel _classCheckPanel = new();
    private readonly StackPanel _questsPanel = new() { Margin = new Thickness(0, 0, 6, 0) };
    private readonly ScrollViewer _bodyScroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(6, 0),
    };
    private readonly List<(TextBlock Tab, string Key)> _modeTabs = [];

    public QuestsWindow(IQuestsHost main)
    {
        _main = main;
        _settings = main.Settings;
        Title = "EQBuddy Quest Tracker";
        Width = 530;
        SizeToContent = SizeToContent.Height;
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;

        _classBtn = ActionButton("Any class");
        _classBtn.FontSize = 11;
        _classBtn.Padding = new Thickness(8, 2);
        ToolTip.SetTip(_classBtn, "Pick your class(es) — quests any of them can do stay visible");
        Content = BuildContent();
        WindowZoom.Attach(this, "quests", _settings);
        BuildClassChecks();
        _eraCombo.Items.Add("Any era");
        foreach (var era in QuestEraLadder.Eras) _eraCombo.Items.Add($"≤ {era}");
        var savedEra = Array.IndexOf(QuestEraLadder.Eras, _settings.QuestEraFilter);
        _eraCombo.SelectedIndex = savedEra >= 0 ? savedEra + 1 : 0;
        foreach (var s in new[] { "any state", "open", "ready", "done" }) _stateCombo.Items.Add(s);
        _stateCombo.SelectedIndex = 0;
        _eraCombo.SelectionChanged += (_, _) => OnEraChanged();
        _stateCombo.SelectionChanged += (_, _) => OnStateChanged();
        ApplyModeVisual();

        PointerPressed += OnDrag;
        Opened += (_, _) =>
        {
            UpdateHeightLimit();
            // Never let an unmoved fallback overwrite a real saved spot (#117).
            _restored = ScreenGuard.OnScreen(this, _settings.QuestsLeft, _settings.QuestsTop, Width, 200);
            if (_restored)
                Position = new PixelPoint((int)_settings.QuestsLeft, (int)_settings.QuestsTop);
            else if (Screens.Primary is { } screen)
                Position = new PixelPoint(
                    screen.WorkingArea.X
                        + (screen.WorkingArea.Width - (int)(Width * screen.Scaling)) / 2,
                    screen.WorkingArea.Y + 80);
            _placed = Position;
        };
        PositionChanged += (_, _) =>
        {
            UpdateHeightLimit();
            _seen.Observe(Position.X, Position.Y, IsVisible);
        };
        Closed += (_, _) =>
        {
            // A closing window reports 0,0 on X11/Wayland; persist only what was seen
            // while it was on screen, else leave the saved spot alone (#169).
            var (curX, curY) = _seen.Or(_settings.QuestsLeft, _settings.QuestsTop);
            (_settings.QuestsLeft, _settings.QuestsTop) = WindowPlacement.PositionToPersist(
                _restored, _placed.X, _placed.Y, curX, curY,
                _settings.QuestsLeft, _settings.QuestsTop);
            _settings.Save();
        };
        Refresh(force: true);
    }

    private Control BuildContent()
    {
        _titleText.Foreground = AppTheme.AccentBrush;
        var close = AppTheme.IconButton("✕", "Close");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Click += (_, _) => Close();
        var header = new Grid { Margin = new Thickness(16, 16, 16, 6) };
        header.Children.Add(_titleText);
        header.Children.Add(close);

        // "I already have this" entry: item name + count, suggestions from the quest
        // catalog so the name lands exactly as the wiki spells it.
        var addRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,52,Auto") };
        addRow.Children.Add(_addItemBox);
        _addQtyBox.Text = "1";
        _addQtyBox.Margin = new Thickness(6, 0, 0, 0);
        _addQtyBox.TextAlignment = TextAlignment.Center;
        Grid.SetColumn(_addQtyBox, 1);
        addRow.Children.Add(_addQtyBox);
        var addBtn = ActionButton("+ I have this");
        addBtn.Margin = new Thickness(6, 0, 0, 0);
        addBtn.Click += (_, _) => OnAddItem();
        Grid.SetColumn(addBtn, 2);
        addRow.Children.Add(addBtn);

        _addItemBox.TextChanged += (_, _) => OnAddItemTyped();
        _addItemBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { OnAddItem(); e.Handled = true; }
            if (e.Key == Key.Escape) _suggestList.IsVisible = false;
        };
        _suggestList.PointerReleased += (_, _) => OnSuggestPicked();

        var filterRow = new Grid
        {
            Margin = new Thickness(0, 6, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"),
        };
        _filterBox.MinWidth = 110;
        _filterBox.TextChanged += (_, _) => Refresh(force: true);
        filterRow.Children.Add(_filterBox);
        ToolTip.SetTip(_eraCombo,
            "Hide quests from later eras than the world has (unmarked quests always show)");
        _eraCombo.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(_eraCombo, 1);
        filterRow.Children.Add(_eraCombo);
        // State filter (Reddit ask, 2026-08-11): every tab and search can narrow to
        // open / ready / completed.
        ToolTip.SetTip(_stateCombo,
            "Any state · open (not yet completed) · ready (turn-ins in hand) · done (marked completed)");
        _stateCombo.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(_stateCombo, 2);
        filterRow.Children.Add(_stateCombo);
        // Multiclass filter (Legends: up to 3 active classes): a checkbox flyout
        // (WPF's Popup, StaysOpen=false → light dismiss), selection remembered
        // per character.
        _classBtn.Margin = new Thickness(6, 0, 0, 0);
        _classBtn.Flyout = new Flyout
        {
            Placement = PlacementMode.Bottom,
            Content = new Border
            {
                Background = AppTheme.PopupBrush,
                CornerRadius = new CornerRadius(6),
                BorderBrush = AppTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8),
                Child = _classCheckPanel,
            },
        };
        Grid.SetColumn(_classBtn, 3);
        filterRow.Children.Add(_classBtn);
        var modeStrip = ModeStrip();
        Grid.SetColumn(modeStrip, 4);
        filterRow.Children.Add(modeStrip);

        var entry = new StackPanel { Margin = new Thickness(16, 0, 16, 4) };
        entry.Children.Add(addRow);
        entry.Children.Add(_suggestList);
        entry.Children.Add(filterRow);

        _bodyScroll.Margin = new Thickness(10, 2, 4, 0);
        _bodyScroll.Content = _questsPanel;

        var footer = new StackPanel { Margin = new Thickness(16, 8, 16, 14) };
        footer.Children.Add(AppTheme.DimText(
            "Counts what you loot, minus what the log sees leave (sales, merges, destroys), " +
            "plus what you add above. Hand-ins aren't in the log — click ✔ ready when you " +
            "turn in, or right-click an item row to clear it. Click a quest name for the " +
            "full wiki walkthrough."));
        // The accuracy contract, said plainly (David, 2026-08-11): we mirror the wiki,
        // we're exactly as right as it is, and the door to fixing BOTH swings from here.
        footer.Children.Add(AppTheme.DimText(
            "Every quest here mirrors eqlwiki.com — verified item-for-item against it, so " +
            "EQBuddy is exactly as accurate as the wiki is today. Spot something wrong? ⚑ " +
            "on the card tells us; fixing the wiki page itself fixes it for everyone (the " +
            "catalog re-harvests weekly). Your own discoveries flow back too: Drops by " +
            "Creature marks wiki-unknown drops in red with a paste-ready page edit.",
            new Thickness(0, 6, 0, 0)));

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        layout.Children.Add(header);
        Grid.SetRow(entry, 1);
        layout.Children.Add(entry);
        Grid.SetRow(_bodyScroll, 2);
        layout.Children.Add(_bodyScroll);
        Grid.SetRow(footer, 3);
        layout.Children.Add(footer);
        // Same chrome family as Spawns: the body scrolls, the title row stays reachable.
        return new Border
        {
            Background = AppTheme.BgBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = layout,
        };
    }

    // mine = your items + 📌 pins · zone = doable where you're standing ·
    // all = the whole catalog
    private Border ModeStrip()
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (key, text, tip) in new[]
        {
            ("mine", "mine", "Quests matching your items and pins"),
            ("zone", "zone", "Everything you can work on in the zone you're in"),
            ("held", "held", "Quests you could turn in with what your bags already hold — " +
                $"from the game's {GameCommands.OutputfileInventory} dump. In game, type " +
                $"{GameCommands.OutputfileInventory}, and this tab reads the file the game writes."),
            ("done", "done", "Quests you've marked completed — every card has a ✓ done " +
                "control, so returning players can check off history"),
            ("all", "all", "The whole quest catalog"),
        })
        {
            var tab = new TextBlock
            {
                Text = text, FontSize = 10.5, Padding = new Thickness(7, 1),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(tab, tip);
            var mode = key;
            tab.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                _mode = mode;
                ApplyModeVisual();
                Refresh(force: true);
            };
            _modeTabs.Add((tab, key));
            strip.Children.Add(tab);
        }
        return new Border
        {
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(1),
            Child = strip,
        };
    }

    /// <summary>Jump the window to one item's quests (the 🗺 badge in the Loot views):
    /// browse mode + the item as filter, so the quests appear even before any overlap
    /// and each carries its 📌 as the invitation to track.</summary>
    public void FilterToItem(string item)
    {
        _mode = "all";
        ApplyModeVisual();
        _filterBox.Text = item;
        Refresh(force: true);
        Activate();
    }

    /// <summary>Programmatic mode switch (screenshot hook + the 🗺 badge path).</summary>
    internal void SetMode(string mode)
    {
        _mode = mode is "zone" or "all" or "held" or "done" ? mode : "mine";
        ApplyModeVisual();
        Refresh(force: true);
    }

    private void ApplyModeVisual()
    {
        foreach (var (tab, key) in _modeTabs)
        {
            tab.Foreground = key == _mode ? AppTheme.AccentBrush : AppTheme.DimBrush;
            // WPF uses ToggleHighlightBrush; this side's AppTheme doesn't carry that
            // key, and PanelHoverBrush is the same "quiet emphasis" tint.
            tab.Background = key == _mode ? AppTheme.PanelHoverBrush : Brushes.Transparent;
        }
    }

    // ---- multiclass filter (Legends: up to three active classes; David 2026-08-07) ----

    private readonly List<CheckBox> _classChecks = [];
    private bool _syncingClasses;

    private void BuildClassChecks()
    {
        foreach (var cls in QuestClassFilter.Classes)
        {
            var check = new CheckBox { Margin = new Thickness(0, 1, 0, 1) };
            check.Content = new TextBlock
            {
                Text = cls, FontSize = 12, Foreground = AppTheme.TextBrush,
            };
            check.IsCheckedChanged += (_, _) => OnClassCheckChanged();
            _classChecks.Add(check);
            _classCheckPanel.Children.Add(check);
        }
    }

    private List<string> SelectedClasses() =>
        _classChecks.Where(c => c.IsChecked == true)
            .Select(c => ((TextBlock)c.Content!).Text ?? "").ToList();

    private void OnClassCheckChanged()
    {
        if (_syncingClasses) return;
        var selected = SelectedClasses();
        var key = _main.QuestCharacterKey;
        if (_main.QuestLedger is { } ledger && key.Length > 0)
            ledger.SetClasses(key, selected);
        UpdateClassButton(selected);
        Refresh(force: true);
    }

    private void UpdateClassButton(List<string> selected) =>
        _classBtn.Content = selected.Count switch
        {
            0 => "Any class",
            1 => selected[0],
            _ => string.Join(" · ", selected.Select(QuestClassFilter.Abbrev)),
        };

    /// <summary>Load the character's saved classes into the checkboxes (character
    /// switches included — the selection follows the ledger, not the window).</summary>
    private void SyncClassChecks(List<string> saved)
    {
        var current = SelectedClasses();
        if (current.SequenceEqual(saved, StringComparer.OrdinalIgnoreCase)) return;
        _syncingClasses = true;
        foreach (var check in _classChecks)
            check.IsChecked = saved.Contains(((TextBlock)check.Content!).Text ?? "",
                StringComparer.OrdinalIgnoreCase);
        _syncingClasses = false;
        UpdateClassButton(saved);
    }

    // The state filter (Reddit ask, 2026-08-11): cuts across every tab and search —
    // session-scoped on purpose, like the search box; a sticky "done" filter would
    // read as an empty tracker tomorrow.
    private string _state = "any state";

    private void OnStateChanged()
    {
        if (_stateCombo.SelectedItem is not string s) return;
        _state = s;
        Refresh(force: true);
    }

    private void OnEraChanged()
    {
        if (_eraCombo.SelectedIndex < 0) return;
        _settings.QuestEraFilter = _eraCombo.SelectedIndex == 0
            ? "" : QuestEraLadder.Eras[_eraCombo.SelectedIndex - 1];
        _settings.Save();
        Refresh(force: true);
    }

    /// <summary>Called from MainWindow's 1 s tick while visible; cheap unless the ledger
    /// or filters actually changed (signature idiom, same as the chip windows).</summary>
    public void MaybeRefresh()
    {
        if ((DateTime.Now - _lastRefresh).TotalSeconds >= 2) Refresh(force: false);
    }

    private void Refresh(bool force)
    {
        _lastRefresh = DateTime.Now;
        var key = _main.QuestCharacterKey;
        var character = key.Length > 0 ? key.Split('_')[0] : "";
        _titleText.Text = character.Length > 0
            ? $"🗺 Quest Tracker — {char.ToUpper(character[0])}{character[1..]}"
            : "🗺 Quest Tracker";

        var owned = _main.QuestLedger?.For(key)
            ?? new Dictionary<string, QuestLedgerStore.Entry>(StringComparer.OrdinalIgnoreCase);
        var tracked = _main.QuestLedger?.TrackedFor(key)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hidden = _main.QuestLedger?.HiddenFor(key)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var completed = _main.QuestLedger?.CompletedFor(key)
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var filter = (_filterBox.Text ?? "").Trim();
        var classes = _main.QuestLedger?.ClassesFor(key) ?? [];
        SyncClassChecks(classes);
        // No classes picked? The log's own evidence pre-filters — ALWAYS labeled
        // inferred, never persisted, and one popup pick overrides it (David,
        // 2026-08-11: players swap classes, so this is a reading, not a fact).
        var inferred = "";
        if (classes.Count == 0 && _main.CurrentSnapshot().InferredClass is { Length: > 0 } inf)
        {
            inferred = inf;
            classes = [inf];
        }

        var sig = $"{key}|{filter}|{_mode}|st:{_state}|{string.Join("+", classes)}|inf:{inferred}|{_settings.QuestEraFilter}|{_main.CurrentZoneName}" +
            $"|{string.Join(";", tracked.Order(StringComparer.OrdinalIgnoreCase))}" +
            $"|{string.Join(";", hidden.Order(StringComparer.OrdinalIgnoreCase))}" +
            $"|{string.Join(";", completed.Select(kv => $"{kv.Key}:{kv.Value}"))}" +
            $"|{string.Join(",", owned.Select(kv => $"{kv.Key}:{kv.Value.Total}"))}";
        if (!force && sig == _signature) return;
        _signature = sig;

        _questsPanel.Children.Clear();
        if (inferred.Length > 0)
        {
            var note = new TextBlock
            {
                Text = $"🎭 Filtering for {inferred} (inferred from your most-used skills — " +
                    "pick classes above to override; inference follows you if you swap)",
                FontSize = 10.5, Margin = new Thickness(2, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.DimBrush,
            };
            _questsPanel.Children.Add(note);
        }

        var era = _settings.QuestEraFilter;
        // Era and class gate separately since 2026-08-11 (David's Crushbone session):
        // era = world availability, always honored; class = the browse lens — and
        // item-driven views (mine, held) show out-of-class quests in their own
        // section rather than hiding what your bags are literally holding pieces of.
        bool EraOk(QuestEntry q) => QuestEraLadder.Allowed(q.Era, era);
        bool ClassOnlyOk(QuestEntry q) => QuestClassFilter.MatchesAny(q.Classes, classes);
        bool ClassOk(QuestEntry q) => ClassOnlyOk(q) && EraOk(q);
        bool StateOk(QuestMatch m) => _state switch
        {
            "open" => completed.GetValueOrDefault(m.Quest.Name) == 0,
            "ready" => m.Complete && m.ItemsTotal > 0,
            "done" => completed.GetValueOrDefault(m.Quest.Name) > 0,
            _ => true,
        };
        QuestMatch Progressed(QuestEntry quest)
        {
            var progress = quest.Items
                .Select(i => new QuestItemProgress(i.Name, i.Qty,
                    owned.TryGetValue(i.Name, out var e) ? e.Total : 0)).ToList();
            return new QuestMatch(quest, progress.Count(p => p.Have > 0), progress.Count,
                progress, tracked.Contains(quest.Name));
        }
        void AddCard(QuestMatch m) => _questsPanel.Children.Add(
            Card(m, hidden.Contains(m.Quest.Name), completed.GetValueOrDefault(m.Quest.Name)));
        void EmptyNote(string text)
        {
            var note = new TextBlock
            {
                Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(6, 8, 0, 8), Foreground = AppTheme.DimBrush,
            };
            _questsPanel.Children.Add(note);
        }

        // A typed search reads the WHOLE catalog, whatever tab is active (David,
        // 2026-08-10: "type an item name and see quests using that; type a quest
        // name to find and track progress"). The tabs scope browsing; a search
        // scopes finding — otherwise an item search on the mine tab found nothing
        // until you already owned pieces, which is backwards.
        if (filter.Length > 0)
        {
            // A search answers with the WHOLE catalog — no class/era/state gating
            // (David's live catch, 2026-08-11: the Blue Orc Head badge found
            // "nothing" because The Falchion is Paladin and his class filter
            // wasn't). Each card states its own class and era; the reader decides.
            var found = QuestSearch.Find(_main.QuestCatalog, filter)
                .Select(Progressed)
                .OrderByDescending(m => m.Tracked)
                .ThenByDescending(m => m.Fraction)
                .ThenBy(m => m.Quest.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var scope = new TextBlock
            {
                Text = $"🔎 {found.Count} match{(found.Count == 1 ? "" : "es")} in the whole catalog — names, turn-in items, rewards, NPCs, zones. " +
                    "Search ignores your class/era/state filters; each card says whose it is.",
                FontSize = 11, Margin = new Thickness(2, 0, 0, 5),
                TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.DimBrush,
            };
            _questsPanel.Children.Add(scope);
            foreach (var m in found) AddCard(m);
            if (found.Count == 0)
                EmptyNote("Nothing matches. Searches cover quest names, turn-in items, " +
                          "rewards, quest givers, and zones — try fewer words.");
            return;
        }

        switch (_mode)
        {
            case "all":
                foreach (var m in _main.QuestCatalog.Quests
                             .Where(q => ClassOk(q))
                             .OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase)
                             .Select(Progressed)
                             .Where(StateOk))
                    AddCard(m);
                break;

            case "zone" when _main.CurrentZoneName.Length == 0:
                EmptyNote("No zone seen in the log yet — zone view fills in once " +
                          "you've zoned somewhere.");
                break;

            case "zone":
            {
                // Everything workable where you stand — including dialogue chains the
                // item parser found nothing for (David: "not everything is item driven").
                var zoneLabel = new TextBlock
                {
                    Text = $"📍 {_main.CurrentZoneName}", FontSize = 11,
                    FontWeight = FontWeight.SemiBold, Margin = new Thickness(2, 0, 0, 5),
                    Foreground = AppTheme.WarnBrush,
                };
                _questsPanel.Children.Add(zoneLabel);
                var zoneQuests = _main.QuestCatalog.Quests
                    .Where(q => q.TouchesZone(_main.CurrentZoneName)
                                && MatchesFilter(q, filter) && ClassOk(q))
                    .Select(Progressed)
                    .Where(StateOk)
                    .OrderByDescending(m => m.Tracked)
                    .ThenByDescending(m => m.Fraction)
                    .ThenBy(m => m.Quest.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (var m in zoneQuests) AddCard(m);
                if (zoneQuests.Count == 0)
                    EmptyNote($"No catalogued quests touch {_main.CurrentZoneName}.");
                break;
            }

            case "held":
            {
                // What the bags could turn in right now, AND what they contribute to
                // (David, 2026-08-11, round two): fully-covered quests lead, partial
                // overlaps follow sorted by closeness — "available quests based on
                // what's in my inventory". The /outputfile command is one click to
                // copy, one paste into the game's chat.
                var snap = _main.LatestInventory(refresh: force);
                Button CopyCmd()
                {
                    var b = ActionButton($"⧉ copy  {GameCommands.OutputfileInventory}");
                    b.FontSize = 11;
                    b.HorizontalAlignment = HorizontalAlignment.Left;
                    b.Margin = new Thickness(0, 4, 0, 6);
                    ToolTip.SetTip(b,
                        "Copies the command — paste it into the game's chat and the game " +
                        "writes your inventory file; this tab reads it. Re-run any time " +
                        "your bags change.");
                    b.Click += async (_, _) =>
                    {
                        try
                        {
                            if (Clipboard is { } cb)
                            {
                                await cb.SetTextAsync(GameCommands.OutputfileInventory);
                                b.Content = "✓ copied — paste in game chat";
                            }
                        }
                        catch (Exception ex) { App.LogError(ex); }   // clipboard momentarily held by another app
                    };
                    return b;
                }
                if (snap is null)
                {
                    EmptyNote("No inventory dump found yet. In game, run this (the game writes " +
                        "<name>_<server>-Inventory.txt beside its own folders and this tab reads " +
                        "it — EQBuddy never scans the game itself):");
                    _questsPanel.Children.Add(CopyCmd());
                    break;
                }
                var invAge = DateTime.Now - snap.WrittenAt;
                var invLabel = new TextBlock
                {
                    Text = $"📦 {Path.GetFileName(snap.Path)} — written " +
                        (invAge.TotalMinutes < 1 ? "just now" : invAge.TotalHours < 1
                            ? $"{(int)invAge.TotalMinutes}m ago" : $"{(int)invAge.TotalHours}h ago") +
                        " (plus everything looted since)",
                    FontSize = 11, Margin = new Thickness(2, 0, 0, 2),
                    TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.WarnBrush,
                };
                _questsPanel.Children.Add(invLabel);
                _questsPanel.Children.Add(CopyCmd());

                // NO class gate on the pool: your bags don't care what class a quest
                // is for (The Falchion's Blue Orc Head in a monk's bag is a farm,
                // not a mistake). In-class leads; the rest gets its own section.
                var overlapping = _main.QuestCatalog.Quests
                    .Where(q => q.Items.Count > 0 && !q.Collection && EraOk(q) && !hidden.Contains(q.Name))
                    .Select(q => new QuestMatch(q,
                        q.Items.Count(i => snap.CountOf(i.Name) > 0), q.Items.Count,
                        q.Items.Select(i => new QuestItemProgress(i.Name, i.Qty, snap.CountOf(i.Name))).ToList(),
                        tracked.Contains(q.Name)))
                    .Where(m => m.ItemsHave > 0)
                    .ToList();
                void Section(string text) => _questsPanel.Children.Add(SectionLabel(text));
                var mine2 = overlapping.Where(m => ClassOnlyOk(m.Quest)).ToList();
                var others = overlapping.Where(m => !ClassOnlyOk(m.Quest))
                    .OrderByDescending(m => m.Complete).ThenByDescending(m => m.Fraction)
                    .ThenBy(m => m.Quest.Name, StringComparer.OrdinalIgnoreCase).ToList();
                var ready = mine2.Where(m => m.Complete)
                    .OrderBy(m => m.Quest.Name, StringComparer.OrdinalIgnoreCase).ToList();
                var partial = mine2.Where(m => !m.Complete)
                    .OrderByDescending(m => m.Fraction)
                    .ThenBy(m => m.Quest.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (ready.Count > 0)
                {
                    Section($"Ready from your bags ({ready.Count})");
                    foreach (var m in ready) AddCard(m);
                }
                if (partial.Count > 0)
                {
                    Section($"Your bags contribute ({partial.Count})");
                    foreach (var m in partial) AddCard(m);
                }
                if (others.Count > 0)
                {
                    Section($"For other classes — you hold pieces anyway ({others.Count})");
                    foreach (var m in others) AddCard(m);
                }
                if (overlapping.Count == 0)
                    EmptyNote("Nothing in your bags matches a catalogued quest's turn-ins yet.");
                break;
            }

            case "done":
            {
                // The trophy shelf — and the catch-up surface: every card carries a ✓,
                // so returning players can mark history without touching items.
                var done = completed.Where(kv => kv.Value > 0)
                    .Select(kv => (_main.QuestCatalog.Quests.FirstOrDefault(q =>
                        q.Name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)), kv.Value))
                    .Where(x => x.Item1 is not null && ClassOk(x.Item1!))
                    .OrderBy(x => x.Item1!.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (var (q, _) in done) AddCard(Progressed(q!));
                if (done.Count == 0)
                    EmptyNote("Nothing marked completed yet. Every quest card has a ✓ — click it " +
                              "on quests you finished before EQBuddy and the tracker catches up " +
                              "(ready quests count themselves when you click their hand-in).");
                break;
            }

            default:
            {
                // "mine": item overlap + pins, minus dismissed and finished-for-good
                // (completed non-repeatables stay visible in zone/all with their ✓).
                var doneForGood = new HashSet<string>(
                    completed.Where(kv => kv.Value > 0).Select(kv => kv.Key)
                        .Where(name => _main.QuestCatalog.Quests.FirstOrDefault(q =>
                            q.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Repeatable: false }),
                    StringComparer.OrdinalIgnoreCase);
                doneForGood.UnionWith(hidden);
                var matches = QuestMatcher.Match(_main.QuestCatalog, owned, tracked, doneForGood);
                // Same rule as held (David's Crushbone session): items you LOOTED
                // outrank the class lens — out-of-class overlaps show in their own
                // section instead of vanishing.
                var eligible = matches
                    .Where(m => MatchesFilter(m.Quest, filter) && EraOk(m.Quest) && StateOk(m))
                    .ToList();
                var shown = eligible.Where(m => ClassOnlyOk(m.Quest)).ToList();
                var othersMine = eligible.Where(m => !ClassOnlyOk(m.Quest)).ToList();
                foreach (var m in shown) AddCard(m);
                if (othersMine.Count > 0)
                {
                    _questsPanel.Children.Add(SectionLabel(
                        $"For other classes — from your items ({othersMine.Count})"));
                    foreach (var m in othersMine) AddCard(m);
                }
                if (shown.Count == 0 && othersMine.Count == 0)
                    EmptyNote(matches.Count == 0
                        ? "Nothing yet — loot a quest item (they show green in the Loot list)\n" +
                          "or add what you already carry with \"+ I have this\" above.\n" +
                          "Try \"zone\" for what's workable here, or \"all\" to browse."
                        : "No quest matches that filter.");
                break;
            }
        }
    }

    // One search predicate, shared with the tests that guard it (QuestSearch in Core).
    private static bool MatchesFilter(QuestEntry q, string filter) => QuestSearch.Matches(q, filter);

    // ---- card building ----

    private Border Card(QuestMatch m, bool isHidden = false, int completedCount = 0)
    {
        var body = new StackPanel();

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,Auto"),
        };
        var name = new TextBlock
        {
            Text = m.Quest.Name, FontSize = 12.5, FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = AppTheme.AccentBrush,
        };
        ToolTip.SetTip(name, "Open the wiki walkthrough");
        OnClick(name, () => OpenUrl(m.Quest.Url));
        header.Children.Add(name);

        var count = new TextBlock
        {
            // Collection pages (CatalogHygiene): several quests share the page, so a
            // fraction over the union would lie — the label says what it is instead.
            Text = m.Quest.Collection ? "📚 set of quests"
                : m.ItemsTotal == 0 ? "steps"
                : m.Complete
                    ? m.Quest.Repeatable && m.ReadyCount > 1 ? $"✔ ready ×{m.ReadyCount}" : "✔ ready"
                    : $"{m.ItemsHave}/{m.ItemsTotal}",
            FontSize = 12, FontWeight = FontWeight.Bold, Margin = new Thickness(8, 0, 0, 0),
            Foreground = m.Quest.Collection ? AppTheme.DimBrush
                : m.Complete ? AppTheme.GoodBrush
                : m.ItemsHave > 0 ? AppTheme.AccentBrush : AppTheme.DimBrush,
        };
        if (m.Quest.Collection)
            ToolTip.SetTip(count,
                "This wiki page documents several quests at once, so per-page progress would " +
                "mislead — open the page for the individual quests. Your items still show below.");
        // A ready card's count doubles as the "I handed it in" button: consumes one set
        // of turn-ins and bumps the done counter. Dialogue quests mark done for free.
        if (m.Complete || m.ItemsTotal == 0)
        {
            ToolTip.SetTip(count, m.ItemsTotal == 0
                ? "Click when you finish this quest to mark it done"
                : "Click when you hand it in — consumes one set of turn-in items and counts a completion");
            OnClick(count, () =>
            {
                var key = _main.QuestCharacterKey;
                if (_main.QuestLedger is { } ledger && key.Length > 0)
                {
                    ledger.RecordCompletion(key, m.Quest.Name, m.Quest.Items);
                    Refresh(force: true);
                }
            });
        }
        Grid.SetColumn(count, 1);
        header.Children.Add(count);

        // 📌 = "keep this quest in front of me": tracked quests sort first and stay
        // visible even with zero items — the choose-to-track affordance (David,
        // 2026-08-07: "players can choose to track quests or not, easily").
        var pin = new TextBlock
        {
            Text = "📌", FontSize = 12, Margin = new Thickness(8, 0, 0, 0),
            Opacity = m.Tracked ? 1.0 : 0.35,
            Foreground = m.Tracked ? AppTheme.AccentBrush : AppTheme.DimBrush,
        };
        ToolTip.SetTip(pin, m.Tracked ? "Stop tracking this quest" : "Track this quest");
        OnClick(pin, () =>
        {
            var key = _main.QuestCharacterKey;
            if (_main.QuestLedger is { } ledger && key.Length > 0)
            {
                ledger.SetTracked(key, m.Quest.Name, !m.Tracked);
                Refresh(force: true);
            }
        });
        Grid.SetColumn(pin, 2);
        header.Children.Add(pin);

        // ✓ = "I did this before EQBuddy" (David, 2026-08-11): catch-up marking on
        // EVERY card, consuming nothing — RecordCompletion's consume path is for
        // hand-ins happening now. Clicking again unmarks a misclick. Completed
        // non-repeatables leave "mine" and gather under the done tab.
        var doneMark = new TextBlock
        {
            Text = "✓", FontSize = 12, Margin = new Thickness(8, 0, 0, 0),
            Opacity = completedCount > 0 ? 1.0 : 0.35,
            Foreground = completedCount > 0 ? AppTheme.GoodBrush : AppTheme.DimBrush,
        };
        ToolTip.SetTip(doneMark, completedCount > 0
            ? $"Completed ×{completedCount} — click to unmark"
            : "Did this before EQBuddy? Mark it completed (consumes nothing; click again to undo)");
        OnClick(doneMark, () =>
        {
            var key = _main.QuestCharacterKey;
            if (_main.QuestLedger is { } ledger && key.Length > 0)
            {
                ledger.SetCompleted(key, m.Quest.Name, completedCount == 0);
                Refresh(force: true);
            }
        });
        Grid.SetColumn(doneMark, 3);
        header.Children.Add(doneMark);

        // ✕ = "not interested": drops the quest from the overlap view AND un-greens
        // loot only it wants (David, 2026-08-07: "there are definitely some I don't
        // want to track"). Hidden quests reappear dimmed under "all quests", where ✕
        // becomes the way back.
        var dismiss = new TextBlock
        {
            Text = "✕", FontSize = 11, Margin = new Thickness(8, 0, 0, 0),
            Opacity = isHidden ? 1.0 : 0.35, Foreground = AppTheme.DimBrush,
        };
        ToolTip.SetTip(dismiss, isHidden
            ? "Show this quest again"
            : "Not interested — hide this quest (its items stop showing green unless another quest wants them)");
        OnClick(dismiss, () =>
        {
            var key = _main.QuestCharacterKey;
            if (_main.QuestLedger is { } ledger && key.Length > 0)
            {
                ledger.SetHidden(key, m.Quest.Name, !isHidden);
                Refresh(force: true);
            }
        });
        Grid.SetColumn(dismiss, 4);
        header.Children.Add(dismiss);

        // ⚑ = "this data is wrong" (David, 2026-08-11: one wrong quest drops faith in
        // everything). One click opens a prefilled report — the catalog's accuracy
        // loop runs on these, same as every parser fix ran on pasted log lines.
        var flag = new TextBlock
        {
            Text = "⚑", FontSize = 11, Margin = new Thickness(8, 0, 0, 0),
            Opacity = 0.35, Foreground = AppTheme.DimBrush,
        };
        ToolTip.SetTip(flag,
            "Something wrong with this quest's data (items, giver, zone)? " +
            "Open a prefilled report — fixes usually ship the same day.");
        OnClick(flag, () =>
        {
            var report =
                $"Quest: {m.Quest.Name}\nWiki page: {m.Quest.Url}\n" +
                $"EQBuddy shows: {m.ItemsTotal} turn-in item(s) — {string.Join(", ", m.Quest.Items.Select(i => i.Qty > 1 ? $"{i.Name} x{i.Qty}" : i.Name))}\n" +
                $"Giver: {m.Quest.QuestGiver} · Zone: {m.Quest.StartZone}\n\nWhat's wrong:\n\n\n" +
                "---\nNote: EQBuddy mirrors eqlwiki.com, so if the wiki page itself is wrong, " +
                "editing the page is the strongest fix — the catalog re-harvests it weekly. " +
                "If the page is right and EQBuddy read it wrong, this report is exactly the right place.\n";
            OpenUrl("https://github.com/DranakCorps-bot/EQBuddy/discussions/new?category=q-a" +
                "&title=" + Uri.EscapeDataString($"Quest data: {m.Quest.Name}") +
                "&body=" + Uri.EscapeDataString(report));
        });
        Grid.SetColumn(flag, 5);
        header.Children.Add(flag);
        body.Children.Add(header);

        if (m.Quest.Rewards.Count > 0)
        {
            // The payoff sits right under the title (David, 2026-08-07: "Crude Stein
            // Quest should show the Crude Stein item"), with the same hover/click as
            // loot: hover pulls the item's wiki stats live, click opens its page.
            var wrap = new WrapPanel { Margin = new Thickness(0, 1, 0, 1) };
            wrap.Children.Add(new TextBlock
            {
                Text = "Rewards:", FontSize = 10.5, Margin = new Thickness(0, 0, 6, 0),
                Foreground = AppTheme.DimBrush,
            });
            const int shown = 6;
            foreach (var reward in m.Quest.Rewards.Take(shown))
                wrap.Children.Add(RewardLink(reward));
            if (m.Quest.Rewards.Count > shown)
            {
                var more = new TextBlock
                {
                    Text = $"+{m.Quest.Rewards.Count - shown} more", FontSize = 10.5,
                    Foreground = AppTheme.DimBrush,
                };
                ToolTip.SetTip(more, string.Join("\n", m.Quest.Rewards.Skip(shown)));
                wrap.Children.Add(more);
            }
            body.Children.Add(wrap);
        }

        // "How far is the turn-in from here" — BFS hops over the harvested zone graph,
        // path in the tooltip (David, 2026-08-07: "3 zones away, zone 1 → zone 2 →
        // zone 3"). Multi-zone quests measure to the nearest listed start zone.
        var distance = "";
        string? route = null;
        if (_main.CurrentZoneName.Length > 0 && m.Quest.StartZone.Length > 0)
        {
            var best = m.Quest.StartZone.Split(',')
                .Select(z => _main.ZoneGraph.Distance(_main.CurrentZoneName, z.Trim()))
                .Where(d => d is not null)
                .OrderBy(d => d!.Value.Hops)
                .FirstOrDefault();
            if (best is { } b)
            {
                distance = b.Hops == 0 ? " · you're here" : $" · {b.Hops} zone{(b.Hops == 1 ? "" : "s")} away";
                route = b.Hops == 0 ? null : string.Join(" → ", b.Path);
            }
        }

        // Classes go LAST: they're the longest fragment and the only one that can
        // afford to vanish into the ellipsis — "done ×2" never should.
        var meta = string.Join(" · ", new[]
        {
            m.Quest.StartZone,
            m.Quest.QuestGiver.Length > 0 ? $"from {m.Quest.QuestGiver}" : "",
            m.Quest.MinLevel > 0 ? $"lvl {m.Quest.MinLevel}+" : "",
            m.Quest.Repeatable ? "repeatable" : "",
            completedCount > 0
                ? m.Quest.Repeatable ? $"done ×{completedCount}" : "✓ done"
                : "",
        }.Where(s => s.Length > 0));
        if (meta.Length > 0 || distance.Length > 0)
        {
            var full = meta + distance
                + (m.Quest.Classes.Length > 0 ? $" · {m.Quest.Classes}" : "");
            var metaText = new TextBlock
            {
                Text = full, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 2), Foreground = AppTheme.DimBrush,
            };
            ToolTip.SetTip(metaText, route is { Length: > 0 } ? $"{route}\n{full}" : full);
            body.Children.Add(metaText);
        }

        foreach (var item in m.Items)
            body.Children.Add(ItemRow(item));
        if (m.ItemsTotal == 0)
        {
            // The item parser found no turn-ins: a dialogue/kill/exploration chain.
            body.Children.Add(new TextBlock
            {
                Text = "Dialogue or task chain — steps on the wiki page.",
                FontSize = 11, FontStyle = FontStyle.Italic,
                Margin = new Thickness(8, 0.5, 0, 0.5), Foreground = AppTheme.DimBrush,
            });
        }

        // Hairline chrome; a READY card keeps the louder green edge — state has a shape.
        return new Border
        {
            Child = body, CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 7, 10, 8), Margin = new Thickness(0, 0, 0, 6),
            BorderThickness = new Thickness(1),
            Opacity = isHidden ? 0.55 : 1.0,
            Background = AppTheme.PanelBrush,
            BorderBrush = m.Complete ? AppTheme.GoodBrush : AppTheme.BorderBrush,
        };
    }

    /// <summary>One reward item: hover fetches its eqlwiki stats on the spot (the
    /// tooltip live-updates from "Looking up…", same as the Loot breakout rows), click
    /// opens the wiki page.</summary>
    private TextBlock RewardLink(string name)
    {
        var link = new TextBlock
        {
            Text = name, FontSize = 10.5, Margin = new Thickness(0, 0, 10, 1),
            Foreground = AppTheme.AccentBrush,
        };
        AttachWikiTip(link, name);
        OnClick(link, () => MainWindow.OpenWikiPage(name));
        return link;
    }

    private const string ItemRowHint =
        "Left-click: +1 (you have one more) · Right-click: clear your count (after a hand-in)";

    private TextBlock ItemRow(QuestItemProgress item)
    {
        var met = item.Have >= item.Need;
        var row = new TextBlock
        {
            Text = $"{(met ? "✔" : "•")} {item.Name} — {item.Have}/{item.Need}",
            FontSize = 11.5, Margin = new Thickness(8, 0.5, 0, 0.5),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Cursor = new Cursor(StandardCursorType.Hand),
            Foreground = met ? AppTheme.GoodBrush
                : item.Have > 0 ? AppTheme.TextBrush : AppTheme.DimBrush,
        };
        // Same live wiki-stats hover the Loot window has (David, 2026-08-07), with the
        // count-adjust hint riding underneath.
        AttachWikiTip(row, item.Name, "\n\n" + ItemRowHint);
        row.PointerPressed += (_, e) => e.Handled = true;   // a row press is a click, not a drag
        row.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            if (e.InitialPressMouseButton == MouseButton.Left) AdjustManual(item.Name, +1);
            else if (e.InitialPressMouseButton == MouseButton.Right) ClearCount(item.Name);
        };
        return row;
    }

    private void AdjustManual(string item, int delta)
    {
        var key = _main.QuestCharacterKey;
        if (_main.QuestLedger is not { } ledger || key.Length == 0) return;
        ledger.For(key).TryGetValue(item, out var entry);
        ledger.SetManual(key, item, (entry?.Manual ?? 0) + delta);
        Refresh(force: true);
    }

    /// <summary>A hand-in happened: zero the whole count for this item. The looted count
    /// is history we can't re-earn, so it becomes a negative manual offset instead —
    /// net zero now, and future loot counts up from there.</summary>
    private void ClearCount(string item)
    {
        var key = _main.QuestCharacterKey;
        if (_main.QuestLedger is not { } ledger || key.Length == 0) return;
        ledger.For(key).TryGetValue(item, out var entry);
        if (entry is null) return;
        ledger.SetManual(key, item, -entry.Looted);
        Refresh(force: true);
    }

    // ---- "+ I have this" ----

    private List<string> Suggestions(string typed) =>
        _main.QuestCatalog.ByItem().Keys
            .Where(n => n.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => !n.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Take(8).ToList();

    private void OnAddItemTyped()
    {
        var typed = (_addItemBox.Text ?? "").Trim();
        if (typed.Length < 2) { _suggestList.IsVisible = false; return; }
        var suggestions = Suggestions(typed);
        _suggestList.ItemsSource = suggestions;
        _suggestList.IsVisible = suggestions.Count > 0;
    }

    private void OnSuggestPicked()
    {
        if (_suggestList.SelectedItem is not string picked) return;
        _addItemBox.Text = picked;
        _suggestList.IsVisible = false;
        _addQtyBox.Focus();
    }

    private void OnAddItem()
    {
        var item = (_addItemBox.Text ?? "").Trim();
        if (item.Length == 0) return;
        if (!int.TryParse((_addQtyBox.Text ?? "").Trim(), out var qty) || qty < 1) qty = 1;
        AdjustManual(item, qty);
        _addItemBox.Text = "";
        _addQtyBox.Text = "1";
        _suggestList.IsVisible = false;
    }

    // ---- shared bits ----

    /// <summary>WPF's ToolTip.Opened lazy fetch: the tip shows the cached stat block (or
    /// "Looking up…") instantly, and the first actual hover fetches once and rewrites the
    /// text in place.</summary>
    private void AttachWikiTip(Control target, string itemName, string suffix = "")
    {
        var cached = _main.CachedItemStats(itemName);
        var tipText = new TextBlock
        {
            Text = (cached ?? "Looking up on eqlwiki…") + suffix,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 340,
            FontFamily = new FontFamily("monospace"),
            Foreground = AppTheme.TextBrush,
        };
        ToolTip.SetTip(target, tipText);
        var fetched = false;
        target.AddHandler(ToolTip.ToolTipOpeningEvent, async (_, _) =>
        {
            if (fetched) return;
            fetched = true;
            try
            {
                var text = await _main.FetchItemTooltip(itemName);
                tipText.Text = (text ?? cached ?? "Not on the wiki.") + suffix;
            }
            catch (Exception ex) { App.LogError(ex); }
        });
    }

    /// <summary>Left-click affordance for a TextBlock: hand cursor, and the press is
    /// handled so the window's move-drag never eats the click.</summary>
    private static void OnClick(TextBlock block, Action action)
    {
        block.Cursor = new Cursor(StandardCursorType.Hand);
        block.PointerPressed += (_, e) => e.Handled = true;
        block.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            e.Handled = true;
            action();
        };
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { CoreLog.Error(ex); }
    }

    // WPF's InputBox / ActionButton / SectionLabel styles, built inline (Theme.xaml
    // equivalents; same recipe as SpawnsWindow's DarkBox).
    private static TextBox InputBox(string tip)
    {
        var box = new TextBox
        {
            FontSize = 12,
            Padding = new Thickness(5, 3),
            Background = AppTheme.ComboBoxBrush,
            Foreground = AppTheme.TextBrush,
            CaretBrush = AppTheme.TextBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(box, tip);
        return box;
    }

    private static Button ActionButton(string text) => new()
    {
        Content = text,
        FontSize = 12,
        Padding = new Thickness(10, 3),
        Background = AppTheme.PanelBrush,
        Foreground = AppTheme.TextBrush,
        BorderThickness = new Thickness(0),
        Cursor = new Cursor(StandardCursorType.Hand),
    };

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text, FontSize = 10.5, FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 6, 0, 2), Foreground = AppTheme.DimBrush,
    };

    private void UpdateHeightLimit()
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen is null) return;
        // WPF caps at 85% of the work area; the body scroll absorbs the rest.
        var available = Math.Max(260, screen.WorkingArea.Height / screen.Scaling * 0.85);
        MaxHeight = available;
        _bodyScroll.MaxHeight = Math.Max(120, available - 250);
    }

    private void OnDrag(object? sender, PointerPressedEventArgs e)
    {
        // Template children (ComboBox arrows, ListBox items…) raise the press, not the
        // control itself — same ancestor walk as SpawnsWindow so popups survive.
        if (e.Source is Visual source && source.GetSelfAndVisualAncestors().Any(IsInteractiveControl))
            return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private static bool IsInteractiveControl(Visual visual) =>
        visual is TextBox or Button or ComboBox or CheckBox or ListBox or ScrollBar or ToggleButton;
}
