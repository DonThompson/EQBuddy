using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EQBuddy.Core;

namespace EQBuddy;

/// <summary>
/// The standalone Quest Tracker (QUEST-*, David's spec 2026-08-07): every wiki quest
/// whose turn-in items overlap what this character owns — looted since the ledger began,
/// or declared via "+ I have this" for pre-EQBuddy inventory. One card per quest,
/// most-complete first; expanding a card lists each item as have/need; the quest name
/// opens the eqlwiki walkthrough. "all quests" flips from the overlap view to the whole
/// catalog for browsing ahead.
/// </summary>
public partial class QuestsWindow : Window
{
    private readonly MainWindow _main;
    private readonly AppSettings _settings;
    private string _signature = "";
    private DateTime _lastRefresh = DateTime.MinValue;
    private string _mode = "mine";   // mine = items+pins · zone = current zone · all

    public QuestsWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        _settings = main.Settings;
        WindowZoom.Attach(this, "quests", _settings);
        BuildClassChecks();
        EraCombo.Items.Add("Any era");
        foreach (var era in QuestEraLadder.Eras) EraCombo.Items.Add($"≤ {era}");
        var savedEra = Array.IndexOf(QuestEraLadder.Eras, _settings.QuestEraFilter);
        EraCombo.SelectedIndex = savedEra >= 0 ? savedEra + 1 : 0;
        foreach (var s in new[] { "any state", "open", "ready", "done" }) StateCombo.Items.Add(s);
        StateCombo.SelectedIndex = 0;
        ApplyModeVisual();
        ChipScale.Apply(this, 1.0);   // quests read at widget size, not chip size
        if (ScreenGuard.OnScreen(_settings.QuestsLeft, _settings.QuestsTop, Width, 200))
        { Left = _settings.QuestsLeft; Top = _settings.QuestsTop; }
        else
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + 80;
        }
        MaxHeight = SystemParameters.WorkArea.Height * 0.85;
        Closed += (_, _) =>
        {
            _settings.QuestsLeft = Left;
            _settings.QuestsTop = Top;
            _settings.Save();
        };
        Refresh(force: true);
    }

    /// <summary>Jump the window to one item's quests (the 🗺 badge in the Loot views):
    /// browse mode + the item as filter, so the quests appear even before any overlap
    /// and each carries its 📌 as the invitation to track.</summary>
    public void FilterToItem(string item)
    {
        _mode = "all";
        ApplyModeVisual();
        FilterBox.Text = item;
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

    private void OnModeClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _mode = (string)((FrameworkElement)sender).Tag;
        ApplyModeVisual();
        Refresh(force: true);
    }

    private void ApplyModeVisual()
    {
        foreach (var (tb, key) in new[]
            { (ModeMine, "mine"), (ModeZone, "zone"), (ModeHeld, "held"), (ModeDone, "done"), (ModeAll, "all") })
        {
            tb.SetResourceReference(TextBlock.ForegroundProperty, key == _mode ? "AccentBrush" : "DimBrush");
            if (key == _mode) tb.SetResourceReference(TextBlock.BackgroundProperty, "ToggleHighlightBrush");
            else tb.Background = System.Windows.Media.Brushes.Transparent;
        }
    }

    // ---- multiclass filter (Legends: up to three active classes; David 2026-08-07) ----

    private readonly List<CheckBox> _classChecks = [];

    private void BuildClassChecks()
    {
        foreach (var cls in QuestClassFilter.Classes)
        {
            var check = new CheckBox { Margin = new Thickness(0, 1, 0, 1) };
            var label = new TextBlock { Text = cls, FontSize = 12 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            check.Content = label;
            check.Checked += (_, _) => OnClassCheckChanged();
            check.Unchecked += (_, _) => OnClassCheckChanged();
            _classChecks.Add(check);
            ClassChecks.Children.Add(check);
        }
    }

    private List<string> SelectedClasses() =>
        _classChecks.Where(c => c.IsChecked == true)
            .Select(c => ((TextBlock)c.Content).Text).ToList();

    private bool _syncingClasses;

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
        ClassBtn.Content = selected.Count switch
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
            check.IsChecked = saved.Contains(((TextBlock)check.Content).Text,
                StringComparer.OrdinalIgnoreCase);
        _syncingClasses = false;
        UpdateClassButton(saved);
    }

    private void OnClassBtn(object sender, RoutedEventArgs e) =>
        ClassPopup.IsOpen = !ClassPopup.IsOpen;

    // The state filter (Reddit ask, 2026-08-11): cuts across every tab and search —
    // session-scoped on purpose, like the search box; a sticky "done" filter would
    // read as an empty tracker tomorrow.
    private string _state = "any state";

    private void OnStateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StateCombo.SelectedItem is not string s) return;
        _state = s;
        Refresh(force: true);
    }

    private void OnEraChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EraCombo.SelectedIndex < 0) return;
        _settings.QuestEraFilter = EraCombo.SelectedIndex == 0
            ? "" : QuestEraLadder.Eras[EraCombo.SelectedIndex - 1];
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
        TitleText.Text = character.Length > 0
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
        var filter = FilterBox.Text.Trim();
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

        QuestsPanel.Children.Clear();
        if (inferred.Length > 0)
        {
            var note = new TextBlock
            {
                Text = $"🎭 Filtering for {inferred} (inferred from your most-used skills — " +
                    "pick classes above to override; inference follows you if you swap)",
                FontSize = 10.5, Margin = new Thickness(2, 0, 0, 4), TextWrapping = TextWrapping.Wrap,
            };
            note.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
            QuestsPanel.Children.Add(note);
        }

        var era = _settings.QuestEraFilter;
        bool ClassOk(QuestEntry q) =>
            QuestClassFilter.MatchesAny(q.Classes, classes)
            && QuestEraLadder.Allowed(q.Era, era);
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
        void AddCard(QuestMatch m) => QuestsPanel.Children.Add(
            Card(m, hidden.Contains(m.Quest.Name), completed.GetValueOrDefault(m.Quest.Name)));
        void EmptyNote(string text)
        {
            var note = new TextBlock
            {
                Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(6, 8, 0, 8),
            };
            note.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
            QuestsPanel.Children.Add(note);
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
                FontSize = 11, Margin = new Thickness(2, 0, 0, 5), TextWrapping = TextWrapping.Wrap,
            };
            scope.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
            QuestsPanel.Children.Add(scope);
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
                    FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 5),
                };
                zoneLabel.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
                QuestsPanel.Children.Add(zoneLabel);
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
                    var b = new Button
                    {
                        Style = (Style)FindResource("ActionButton"), FontSize = 11,
                        Content = "⧉ copy  /outputfile inventory",
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 4, 0, 6),
                        ToolTip = "Copies the command — paste it into the game's chat and the " +
                            "game writes your inventory file; this tab reads it. Re-run any " +
                            "time your bags change.",
                    };
                    b.Click += (_, _) =>
                    {
                        try { Clipboard.SetText("/outputfile inventory"); b.Content = "✓ copied — paste in game chat"; }
                        catch { /* clipboard momentarily held by another app */ }
                    };
                    return b;
                }
                if (snap is null)
                {
                    EmptyNote("No inventory dump found yet. In game, run this (the game writes " +
                        "<name>_<server>-Inventory.txt beside its own folders and this tab reads " +
                        "it — EQBuddy never scans the game itself):");
                    QuestsPanel.Children.Add(CopyCmd());
                    break;
                }
                var invAge = DateTime.Now - snap.WrittenAt;
                var invLabel = new TextBlock
                {
                    Text = $"📦 {System.IO.Path.GetFileName(snap.Path)} — written " +
                        (invAge.TotalMinutes < 1 ? "just now" : invAge.TotalHours < 1
                            ? $"{(int)invAge.TotalMinutes}m ago" : $"{(int)invAge.TotalHours}h ago") +
                        " (plus everything looted since)",
                    FontSize = 11, Margin = new Thickness(2, 0, 0, 2), TextWrapping = TextWrapping.Wrap,
                };
                invLabel.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
                QuestsPanel.Children.Add(invLabel);
                QuestsPanel.Children.Add(CopyCmd());

                var overlapping = _main.QuestCatalog.Quests
                    .Where(q => q.Items.Count > 0 && !q.Collection && ClassOk(q) && !hidden.Contains(q.Name))
                    .Select(q => new QuestMatch(q,
                        q.Items.Count(i => snap.CountOf(i.Name) > 0), q.Items.Count,
                        q.Items.Select(i => new QuestItemProgress(i.Name, i.Qty, snap.CountOf(i.Name))).ToList(),
                        tracked.Contains(q.Name)))
                    .Where(m => m.ItemsHave > 0)
                    .ToList();
                void Section(string text)
                {
                    var tb = new TextBlock { Text = text, Style = (Style)FindResource("SectionLabel") };
                    QuestsPanel.Children.Add(tb);
                }
                var ready = overlapping.Where(m => m.Complete)
                    .OrderBy(m => m.Quest.Name, StringComparer.OrdinalIgnoreCase).ToList();
                var partial = overlapping.Where(m => !m.Complete)
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
                var shown = matches
                    .Where(m => MatchesFilter(m.Quest, filter) && ClassOk(m.Quest) && StateOk(m))
                    .ToList();
                foreach (var m in shown) AddCard(m);
                if (shown.Count == 0)
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

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock
        {
            Text = m.Quest.Name, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Cursor = Cursors.Hand,
            ToolTip = "Open the wiki walkthrough",
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        name.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenUrl(m.Quest.Url);
        };
        Grid.SetColumn(name, 0);
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
            FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(8, 0, 0, 0),
            ToolTip = m.Quest.Collection
                ? "This wiki page documents several quests at once, so per-page progress would " +
                  "mislead — open the page for the individual quests. Your items still show below."
                : null,
        };
        count.SetResourceReference(TextBlock.ForegroundProperty,
            m.Quest.Collection ? "DimBrush"
            : m.Complete ? "GoodBrush" : m.ItemsHave > 0 ? "AccentBrush" : "DimBrush");
        // A ready card's count doubles as the "I handed it in" button: consumes one set
        // of turn-ins and bumps the done counter. Dialogue quests mark done for free.
        if (m.Complete || m.ItemsTotal == 0)
        {
            count.Cursor = Cursors.Hand;
            count.ToolTip = m.ItemsTotal == 0
                ? "Click when you finish this quest to mark it done"
                : "Click when you hand it in — consumes one set of turn-in items and counts a completion";
            count.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                var key = _main.QuestCharacterKey;
                if (_main.QuestLedger is { } ledger && key.Length > 0)
                {
                    ledger.RecordCompletion(key, m.Quest.Name, m.Quest.Items);
                    Refresh(force: true);
                }
            };
        }
        Grid.SetColumn(count, 1);
        header.Children.Add(count);

        // 📌 = "keep this quest in front of me": tracked quests sort first and stay
        // visible even with zero items — the choose-to-track affordance (David,
        // 2026-08-07: "players can choose to track quests or not, easily").
        var pin = new TextBlock
        {
            Text = "📌", FontSize = 12, Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand, Opacity = m.Tracked ? 1.0 : 0.35,
            ToolTip = m.Tracked ? "Stop tracking this quest" : "Track this quest",
        };
        pin.SetResourceReference(TextBlock.ForegroundProperty, m.Tracked ? "AccentBrush" : "DimBrush");
        pin.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var key = _main.QuestCharacterKey;
            if (_main.QuestLedger is { } ledger && key.Length > 0)
            {
                ledger.SetTracked(key, m.Quest.Name, !m.Tracked);
                Refresh(force: true);
            }
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(pin, 2);
        header.Children.Add(pin);

        // ✓ = "I did this before EQBuddy" (David, 2026-08-11): catch-up marking on
        // EVERY card, consuming nothing — RecordCompletion's consume path is for
        // hand-ins happening now. Clicking again unmarks a misclick. Completed
        // non-repeatables leave "mine" and gather under the done tab.
        var doneMark = new TextBlock
        {
            Text = "✓", FontSize = 12, Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand, Opacity = completedCount > 0 ? 1.0 : 0.35,
            ToolTip = completedCount > 0
                ? $"Completed ×{completedCount} — click to unmark"
                : "Did this before EQBuddy? Mark it completed (consumes nothing; click again to undo)",
        };
        doneMark.SetResourceReference(TextBlock.ForegroundProperty,
            completedCount > 0 ? "GoodBrush" : "DimBrush");
        doneMark.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var key = _main.QuestCharacterKey;
            if (_main.QuestLedger is { } ledger && key.Length > 0)
            {
                ledger.SetCompleted(key, m.Quest.Name, completedCount == 0);
                Refresh(force: true);
            }
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(doneMark, header.ColumnDefinitions.Count - 1);
        header.Children.Add(doneMark);

        // ✕ = "not interested": drops the quest from the overlap view AND un-greens
        // loot only it wants (David, 2026-08-07: "there are definitely some I don't
        // want to track"). Hidden quests reappear dimmed under "all quests", where ✕
        // becomes the way back.
        var dismiss = new TextBlock
        {
            Text = "✕", FontSize = 11, Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand, Opacity = isHidden ? 1.0 : 0.35,
            ToolTip = isHidden
                ? "Show this quest again"
                : "Not interested — hide this quest (its items stop showing green unless another quest wants them)",
        };
        dismiss.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
        dismiss.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var key = _main.QuestCharacterKey;
            if (_main.QuestLedger is { } ledger && key.Length > 0)
            {
                ledger.SetHidden(key, m.Quest.Name, !isHidden);
                Refresh(force: true);
            }
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(dismiss, 4);
        header.Children.Add(dismiss);

        // ⚑ = "this data is wrong" (David, 2026-08-11: one wrong quest drops faith in
        // everything). One click opens a prefilled report — the catalog's accuracy
        // loop runs on these, same as every parser fix ran on pasted log lines.
        var flag = new TextBlock
        {
            Text = "⚑", FontSize = 11, Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand, Opacity = 0.35,
            ToolTip = "Something wrong with this quest's data (items, giver, zone)? " +
                "Open a prefilled report — fixes usually ship the same day.",
        };
        flag.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
        flag.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var body =
                $"Quest: {m.Quest.Name}\nWiki page: {m.Quest.Url}\n" +
                $"EQBuddy shows: {m.ItemsTotal} turn-in item(s) — {string.Join(", ", m.Quest.Items.Select(i => i.Qty > 1 ? $"{i.Name} x{i.Qty}" : i.Name))}\n" +
                $"Giver: {m.Quest.QuestGiver} · Zone: {m.Quest.StartZone}\n\nWhat's wrong:\n\n\n" +
                "---\nNote: EQBuddy mirrors eqlwiki.com, so if the wiki page itself is wrong, " +
                "editing the page is the strongest fix — the catalog re-harvests it weekly. " +
                "If the page is right and EQBuddy read it wrong, this report is exactly the right place.\n";
            OpenUrl("https://github.com/DranakCorps-bot/EQBuddy/discussions/new?category=q-a" +
                "&title=" + Uri.EscapeDataString($"Quest data: {m.Quest.Name}") +
                "&body=" + Uri.EscapeDataString(body));
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(flag, 5);
        header.Children.Add(flag);
        body.Children.Add(header);

        if (m.Quest.Rewards.Count > 0)
        {
            // The payoff sits right under the title (David, 2026-08-07: "Crude Stein
            // Quest should show the Crude Stein item"), with the same hover/click as
            // loot: hover pulls the item's wiki stats live, click opens its page.
            var wrap = new WrapPanel { Margin = new Thickness(0, 1, 0, 1) };
            var label = new TextBlock
            {
                Text = "Rewards:", FontSize = 10.5, Margin = new Thickness(0, 0, 6, 0),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
            wrap.Children.Add(label);
            const int shown = 6;
            foreach (var reward in m.Quest.Rewards.Take(shown))
                wrap.Children.Add(RewardLink(reward));
            if (m.Quest.Rewards.Count > shown)
            {
                var more = new TextBlock
                {
                    Text = $"+{m.Quest.Rewards.Count - shown} more", FontSize = 10.5,
                    ToolTip = string.Join("\n", m.Quest.Rewards.Skip(shown)),
                };
                more.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
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
                Margin = new Thickness(0, 1, 0, 2),
                ToolTip = route is { Length: > 0 } ? $"{route}\n{full}" : full,
            };
            metaText.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
            body.Children.Add(metaText);
        }

        foreach (var item in m.Items)
            body.Children.Add(ItemRow(item));
        if (m.ItemsTotal == 0)
        {
            // The item parser found no turn-ins: a dialogue/kill/exploration chain.
            var dialogue = new TextBlock
            {
                Text = "Dialogue or task chain — steps on the wiki page.",
                FontSize = 11, FontStyle = FontStyles.Italic, Margin = new Thickness(8, 0.5, 0, 0.5),
            };
            dialogue.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
            body.Children.Add(dialogue);
        }

        var card = new Border
        {
            Child = body, CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 7, 10, 8), Margin = new Thickness(0, 0, 0, 6),
            BorderThickness = new Thickness(1),
            Opacity = isHidden ? 0.55 : 1.0,
        };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        // Hairline chrome; a READY card keeps the louder green edge — state has a shape.
        card.SetResourceReference(Border.BorderBrushProperty, m.Complete ? "GoodBrush" : "HairlineBrush");
        return card;
    }

    /// <summary>One reward item: hover fetches its eqlwiki stats on the spot (the
    /// tooltip live-updates from "Looking up…", same as the Loot breakout rows), click
    /// opens the wiki page.</summary>
    private TextBlock RewardLink(string name)
    {
        var cached = _main.CachedItemStats(name);
        var link = new TextBlock
        {
            Text = name, FontSize = 10.5, Margin = new Thickness(0, 0, 10, 1),
            Cursor = Cursors.Hand,
        };
        link.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

        var tipText = new TextBlock
        {
            Text = cached ?? "Looking up on eqlwiki…",
            TextWrapping = TextWrapping.Wrap, MaxWidth = 340,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
        };
        var tip = new ToolTip { Content = tipText };
        link.ToolTip = tip;
        var fetched = false;
        tip.Opened += async (_, _) =>
        {
            if (fetched) return;
            fetched = true;
            var text = await _main.FetchItemTooltip(name);
            tipText.Text = text ?? (cached ?? "Not on the wiki.");
        };

        link.MouseLeftButtonDown += (_, e) => e.Handled = true;
        link.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            MainWindow.OpenWikiPage(name);
        };
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
            Cursor = Cursors.Hand,
        };
        // Same live wiki-stats hover the Loot window has (David, 2026-08-07), with the
        // count-adjust hint riding underneath.
        var cached = _main.CachedItemStats(item.Name);
        var tipText = new TextBlock
        {
            Text = (cached ?? "Looking up on eqlwiki…") + "\n\n" + ItemRowHint,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 340,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
        };
        var tip = new ToolTip { Content = tipText };
        row.ToolTip = tip;
        var fetched = false;
        tip.Opened += async (_, _) =>
        {
            if (fetched) return;
            fetched = true;
            var text = await _main.FetchItemTooltip(item.Name);
            tipText.Text = (text ?? cached ?? "Not on the wiki.") + "\n\n" + ItemRowHint;
        };
        row.SetResourceReference(TextBlock.ForegroundProperty,
            met ? "GoodBrush" : item.Have > 0 ? "TextBrush" : "DimBrush");
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            AdjustManual(item.Name, +1);
        };
        row.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            ClearCount(item.Name);
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

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { CoreLog.Error(ex); }
    }

    // ---- "+ I have this" ----

    private List<string> Suggestions(string typed) =>
        _main.QuestCatalog.ByItem().Keys
            .Where(n => n.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => !n.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Take(8).ToList();

    private void OnAddItemTyped(object sender, TextChangedEventArgs e)
    {
        var typed = AddItemBox.Text.Trim();
        if (typed.Length < 2) { SuggestList.Visibility = Visibility.Collapsed; return; }
        var suggestions = Suggestions(typed);
        SuggestList.ItemsSource = suggestions;
        SuggestList.Visibility = suggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSuggestPicked(object sender, MouseButtonEventArgs e)
    {
        if (SuggestList.SelectedItem is not string picked) return;
        AddItemBox.Text = picked;
        SuggestList.Visibility = Visibility.Collapsed;
        AddQtyBox.Focus();
    }

    private void OnAddItemKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OnAddItem(sender, e); e.Handled = true; }
        if (e.Key == Key.Escape) SuggestList.Visibility = Visibility.Collapsed;
    }

    private void OnAddItem(object sender, RoutedEventArgs e)
    {
        var item = AddItemBox.Text.Trim();
        if (item.Length == 0) return;
        if (!int.TryParse(AddQtyBox.Text.Trim(), out var qty) || qty < 1) qty = 1;
        AdjustManual(item, qty);
        AddItemBox.Clear();
        AddQtyBox.Text = "1";
        SuggestList.Visibility = Visibility.Collapsed;
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => Refresh(force: true);
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
