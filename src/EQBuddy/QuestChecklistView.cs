using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy;

/// <summary>
/// The Epic 1.0 and Sky quest checklists: two tabbed surfaces, their search view, the
/// achievement import that ticks Sky rows for you, and the auto-checkers that watch
/// loot for both.
///
/// This lived inside <see cref="MainWindow"/> until 2026-08-15 — 992 lines of it, a
/// fifth of the file — even though it only ever touched settings, its own state, and
/// eleven named controls. Lifting it out is what bought MainWindow back its working
/// room under the ArchitectureTests ratchet, and the point of doing it as a class
/// rather than a partial file is that the ratchet counts partials together: hiding
/// lines in a second file would have been a dodge, moving a component out is not.
///
/// The window is still reached for the two things only it can give — resource lookup
/// for themed brushes, and the controls themselves — so the coupling is one-way and
/// listed in full in the fields below. Everything else here is self-contained.
///
/// It has no unit tests, because the WPF layer has none (docs/TestPlan.md §5). What
/// holds it is <c>TheQuestChecklistRendersATabPerClassAndTheSelectedClassesRows</c> in
/// tests/EQBuddy.E2E, which asserts tab and row counts out of the EQBUDDY_EXPAND dump
/// — written before this move, and green on both sides of it.
/// </summary>
internal sealed class QuestChecklistView
{
    private readonly MainWindow _w;
    private readonly AppSettings _settings;
    // Fetched on use, not on construction: this view must exist before MainWindow
    // restores any control, and the raid ledger is built later in that constructor.
    // Only the achievement import touches it, long after everything is in place.
    private readonly Func<RaidKillLedger> _raidLedger;

    // The whole of this surface's reach into MainWindow's XAML, named once here.
    private readonly TabControl _epicTabs, _skyTabs;
    private readonly TextBlock _epicHeader, _skyHeader;
    private readonly Expander _epicSection;
    private readonly CheckBox _classicOnlyCheck;
    private readonly TextBox _skySearchBox;
    private readonly StackPanel _skySearchResults;
    private readonly ScrollViewer _skySearchScroll, _sectionScroll;
    private readonly ComboBox _skyStateCombo;

    public QuestChecklistView(MainWindow w, AppSettings settings, Func<RaidKillLedger> raidLedger)
    {
        _w = w;
        _settings = settings;
        _raidLedger = raidLedger;
        _epicTabs = w.EpicTabs;
        _skyTabs = w.SkyQuestTabs;
        _epicHeader = w.EpicHeader;
        _skyHeader = w.SkyQuestHeader;
        _epicSection = w.EpicSection;
        _classicOnlyCheck = w.EpicClassicOnlyCheck;
        _skySearchBox = w.SkySearchBox;
        _skySearchResults = w.SkySearchResults;
        _skySearchScroll = w.SkySearchScroll;
        _sectionScroll = w.SectionScroll;
        _skyStateCombo = w.SkyStateCombo;
    }

    /// <summary>Both surfaces re-render only when something changed and their card is
    /// open. MainWindow owns the "card is open" half and asks these.</summary>
    public bool EpicDirty { get; private set; } = true;
    public bool SkyDirty { get; private set; } = true;

    public void MarkEpicDirty() => EpicDirty = true;
    public void MarkSkyDirty() => SkyDirty = true;
    public void ClearEpicDirty() => EpicDirty = false;
    public void ClearSkyDirty() => SkyDirty = false;

    // High-water marks for the loot auto-checkers: only the newly-looted delta ticks a
    // step, so a re-render can never double-count.
    private readonly Dictionary<string, int> _skyQuestLootSeen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _epicQuestLootSeen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A new session (or a character switch) must forget what it had already
    /// counted, or the next snapshot reads as a burst of fresh loot.</summary>
    public void ResetLootSeen()
    {
        _skyQuestLootSeen.Clear();
        _epicQuestLootSeen.Clear();
    }

    internal void RenderEpicQuestChecklist()
    {
        var selectedClass = (_epicTabs.SelectedItem as TabItem)?.Tag as string
            ?? (_settings.EpicQuestClass.Length > 0 ? _settings.EpicQuestClass : null);
        _epicTabs.Items.Clear();

        foreach (var className in QuestClassFilter.Classes)
        {
            var allClassItems = _settings.EpicQuestChecklist
                .Where(i => string.Equals(i.ClassName, className, StringComparison.Ordinal))
                .OrderBy(i => i.Order)
                .ThenBy(i => i.QuestItem, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var classItems = FilterEpicQuestRows(allClassItems).ToList();
            var done = classItems.Count(i => i.Acquired);
            var total = classItems.Count;
            var quest = EpicQuestDefaults.FindQuest(_w.QuestCatalog, className);
            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

            if (quest is null || allClassItems.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "No Epic 1.0 quest found in the catalog for this class yet.",
                    FontSize = 11,
                    Foreground = (Brush)_w.FindResource("DimBrush"),
                    TextWrapping = TextWrapping.Wrap,
                });
            }
            else
            {
                panel.Children.Add(new TextBlock
                {
                    Text = quest.Name,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)_w.FindResource("AccentBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                if (classItems[0].Reward.Length > 0)
                    panel.Children.Add(new TextBlock
                    {
                        Text = "Reward: " + classItems[0].Reward,
                        FontSize = 10,
                        Foreground = (Brush)_w.FindResource("DimBrush"),
                        TextWrapping = TextWrapping.Wrap,
                    });
                var source = EpicQuestDefaults.SourceLine(quest);
                if (source.Length > 0)
                    panel.Children.Add(new TextBlock
                    {
                        Text = source,
                        FontSize = 10,
                        Foreground = (Brush)_w.FindResource("DimBrush"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 1, 0, 4),
                    });

                if (classItems.Count == 0)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = "No classic-doable steps are tagged for this class yet.",
                        FontSize = 11,
                        Foreground = (Brush)_w.FindResource("DimBrush"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 8, 0, 4),
                    });
                }

                var completed = IsEpicQuestCompleted(className);
                var completeCheck = new CheckBox
                {
                    IsChecked = completed,
                    Margin = new Thickness(0, 8, 0, 4),
                    ToolTip = "Check when the final epic turn-in is finished.",
                    Content = new TextBlock
                    {
                        Text = completed ? "Complete" : "Epic complete",
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)_w.FindResource("AccentBrush"),
                    },
                };
                completeCheck.Checked += (_, _) => OnEpicQuestCompletedToggled(className, allClassItems, true, completeCheck);
                completeCheck.Unchecked += (_, _) => OnEpicQuestCompletedToggled(className, allClassItems, false, completeCheck);
                panel.Children.Add(completeCheck);

                foreach (var sectionGroup in classItems.GroupBy(i => i.Section.Length > 0 ? i.Section : "Checklist"))
                {
                    if (!sectionGroup.Key.Equals("Checklist", StringComparison.OrdinalIgnoreCase) || classItems.Select(i => i.Section).Distinct().Count() > 1)
                        panel.Children.Add(new TextBlock
                        {
                            Text = sectionGroup.Key,
                            FontSize = 12,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = (Brush)_w.FindResource("AccentBrush"),
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 8, 0, 2),
                        });

                    foreach (var item in sectionGroup)
                    {
                        // * = the auto-tick parked a multi-class item here because no
                        // class lens claimed it (the Sky #106 contract) — the player
                        // decides where it belongs.
                        var text = new TextBlock
                        {
                            Text = item.AcquiredUnassigned ? item.QuestItem + " *" : item.QuestItem,
                            FontSize = 11,
                            Foreground = (Brush)_w.FindResource("TextBrush"),
                            TextWrapping = TextWrapping.Wrap,
                        };

                        var tip = $"{item.QuestName}: {item.QuestItem}";
                        if (item.AcquiredUnassigned)
                        {
                            var others = _settings.EpicQuestChecklist
                                .Where(i => !i.ClassName.Equals(item.ClassName, StringComparison.OrdinalIgnoreCase)
                                         && i.ItemNames.Any(n => item.ItemNames.Contains(n, StringComparer.OrdinalIgnoreCase)))
                                .Select(i => i.ClassName).Distinct().ToList();
                            tip += "\n* Auto-ticked here, but the looted item is also wanted by: "
                                + string.Join(", ", others)
                                + ". Untick it and tick the right class if this guess is wrong.";
                        }

                        var check = new CheckBox
                        {
                            IsChecked = item.Acquired,
                            Content = text,
                            Margin = new Thickness(0, 2, 0, 2),
                            IsEnabled = !completed,
                            Opacity = completed ? 0.55 : 1.0,
                            ToolTip = tip,
                        };
                        check.Checked += (_, _) => OnEpicQuestToggled(item, true);
                        check.Unchecked += (_, _) => OnEpicQuestToggled(item, false);
                        panel.Children.Add(check);
                    }
                }
            }

            var tab = new TabItem
            {
                Header = total > 0 ? $"{ClassAbbrev(className)} {done}/{total}" : $"{ClassAbbrev(className)} -",
                Tag = className,
                Content = new ScrollViewer
                {
                    Content = panel,
                    MaxHeight = SkyQuestListMaxHeight(),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    PanningMode = PanningMode.VerticalOnly,
                    Padding = new Thickness(0, 0, 4, 0),
                },
                ToolTip = className,
            };
            _epicTabs.Items.Add(tab);
            if (string.Equals(selectedClass, className, StringComparison.Ordinal))
                _epicTabs.SelectedItem = tab;
        }

        if (_epicTabs.SelectedIndex < 0 && _epicTabs.Items.Count > 0)
            _epicTabs.SelectedIndex = 0;
    }

    private static string EpicQuestCompletedKey(string className) => className;

    private bool IsEpicQuestCompleted(string className) =>
        _settings.EpicQuestCompleted.Contains(EpicQuestCompletedKey(className), StringComparer.OrdinalIgnoreCase);

    private IEnumerable<EpicQuestChecklistItem> FilterEpicQuestRows(IEnumerable<EpicQuestChecklistItem> items) =>
        _settings.EpicQuestClassicOnly ? items.Where(i => i.AvailableInClassic) : items;

    internal void OnEpicClassicOnlyToggled(object sender, RoutedEventArgs e)
    {
        var value = _classicOnlyCheck.IsChecked == true;
        if (_settings.EpicQuestClassicOnly == value)
            return;

        _settings.EpicQuestClassicOnly = value;
        _settings.Save();
        UpdateEpicQuestHeaderOnly();
        if (_epicSection.IsExpanded)
        {
            RenderEpicQuestChecklist();
            ClearEpicDirty();
        }
        else
        {
            MarkEpicDirty();
        }
    }

    /// <summary>True while a cancelled master check is being flipped back in code —
    /// the resulting Unchecked event is not a toggle and must not restore anything.</summary>
    private bool _epicCompleteReverting;

    private void OnEpicQuestCompletedToggled(string className, List<EpicQuestChecklistItem> items, bool done,
        CheckBox master)
    {
        if (_epicCompleteReverting) return;
        var key = EpicQuestCompletedKey(className);
        if (done)
        {
            // One stray click here flips every unchecked row (#138, aodgizmo) — bulk
            // enough to warrant the one confirmation this card has. All rows already
            // checked by hand means nothing gets overwritten: no dialog.
            var remaining = EQBuddy.UI.Shared.EpicCompleteToggle.CountUnchecked(items);
            if (remaining > 0 && MessageBox.Show(_w,
                    $"Mark all {remaining} remaining {className} steps complete?",
                    "Epic complete", MessageBoxButton.OKCancel, MessageBoxImage.Question)
                != MessageBoxResult.OK)
            {
                _epicCompleteReverting = true;
                master.IsChecked = false;
                _epicCompleteReverting = false;
                return;
            }
            if (!_settings.EpicQuestCompleted.Contains(key, StringComparer.OrdinalIgnoreCase))
                _settings.EpicQuestCompleted.Add(key);
            // Snapshot what the bulk check overwrites, so unchecking can undo it.
            _settings.EpicQuestPreCompleteAcquired[key] = EQBuddy.UI.Shared.EpicCompleteToggle.Snapshot(items);
            EQBuddy.UI.Shared.EpicCompleteToggle.CheckAll(items);
        }
        else
        {
            _settings.EpicQuestCompleted.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            // Restore what the bulk check overwrote. No snapshot (completed before
            // the undo existed) leaves the rows as they are — the old behavior.
            if (_settings.EpicQuestPreCompleteAcquired.Remove(key, out var acquiredIds))
                EQBuddy.UI.Shared.EpicCompleteToggle.Restore(items, acquiredIds);
        }

        _settings.Save();
        UpdateEpicQuestHeaderOnly();
        if (_epicSection.IsExpanded)
        {
            RenderEpicQuestChecklist();
            ClearEpicDirty();
        }
        else
        {
            MarkEpicDirty();
        }
    }

    private void OnEpicQuestToggled(EpicQuestChecklistItem item, bool acquired)
    {
        item.Acquired = acquired;
        // The player deciding IS the resolution of an auto-parked tick (#106) —
        // whichever way they toggled, the * has served its purpose.
        item.AcquiredUnassigned = false;
        _settings.Save();
        UpdateEpicQuestHeaderOnly();
        UpdateEpicQuestTabHeader(item.ClassName);
    }

    internal void OnEpicQuestTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((_epicTabs.SelectedItem as TabItem)?.Tag is string cls &&
            !string.Equals(_settings.EpicQuestClass, cls, StringComparison.Ordinal))
        {
            _settings.EpicQuestClass = cls;
            _settings.Save();
        }
    }

    private void UpdateEpicQuestTabHeader(string className)
    {
        foreach (var tab in _epicTabs.Items.OfType<TabItem>())
            if (string.Equals(tab.Tag as string, className, StringComparison.Ordinal))
            {
                var classItems = FilterEpicQuestRows(_settings.EpicQuestChecklist
                    .Where(i => string.Equals(i.ClassName, className, StringComparison.Ordinal)))
                    .ToList();
                var done = classItems.Count(i => i.Acquired);
                var total = classItems.Count;
                tab.Header = total > 0 ? $"{ClassAbbrev(className)} {done}/{total}" : $"{ClassAbbrev(className)} -";
            }
    }

    internal void UpdateEpicQuestHeaderOnly()
    {
        var items = FilterEpicQuestRows(_settings.EpicQuestChecklist).ToList();
        var total = items.Count;
        var acquired = items.Count(i => i.Acquired);
        _epicHeader.Text = $"{acquired}/{total}";
    }

    internal void UpdateEpicQuestChecklist(StatsSnapshot s)
    {
        var changed = AutoCheckEpicQuestLoot(s);
        UpdateEpicQuestHeaderOnly();
        if (changed)
        {
            MarkEpicDirty();
            _settings.Save();
        }
    }

    /// <summary>Last snapshot version each auto-checker processed (perf audit #13):
    /// loot can only change with an event, and every event bumps the version — so an
    /// unchanged version means the per-tick regroup can be skipped without moving any
    /// high-water mark. Every path that clears the seen-dictionaries (session
    /// identity, review entry, character switch) also moves the version, so the
    /// re-arm pass is never skipped.</summary>
    private long _epicAutoCheckVersion = -1;
    private long _skyAutoCheckVersion = -1;

    private bool AutoCheckEpicQuestLoot(StatsSnapshot s)
    {
        if (s.Version == _epicAutoCheckVersion) return false;   // perf audit #13
        _epicAutoCheckVersion = s.Version;
        var changed = false;
        // The class-scoping rules live in Core (EpicLootAutoCheck) where they are
        // tested — the Sky rules (#98/#106) over prose steps keyed by the catalog
        // items their text mentions (#121). Same high-water diff as Sky: only the
        // newly-looted delta ticks steps, so a re-render never double-counts.
        var myClasses = _w.QuestLedger?.ClassesFor(_w.QuestCharacterKey) ?? [];
        var lootByName = s.Loot
            .GroupBy(l => l.Item, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Count), StringComparer.OrdinalIgnoreCase);

        foreach (var key in _epicQuestLootSeen.Keys.ToList())
            if (!lootByName.ContainsKey(key))
                _epicQuestLootSeen[key] = 0;

        foreach (var (name, count) in lootByName)
        {
            _epicQuestLootSeen.TryGetValue(name, out var seen);
            _epicQuestLootSeen[name] = count;
            if (count <= seen) continue;
            changed |= EpicLootAutoCheck.Apply(_settings.EpicQuestChecklist, name,
                count - seen, myClasses, _settings.EpicQuestClass);
        }

        return changed;
    }

    internal void UpdateSkyQuestChecklist(StatsSnapshot s)
    {
        var changed = AutoCheckSkyQuestLoot(s);
        UpdateSkyQuestHeaderOnly();
        if (changed)
        {
            MarkSkyDirty();
            _settings.Save();
        }
    }

    private bool AutoCheckSkyQuestLoot(StatsSnapshot s)
    {
        if (s.Version == _skyAutoCheckVersion) return false;   // perf audit #13
        _skyAutoCheckVersion = s.Version;
        var changed = false;
        // The class-scoping rules live in Core (SkyLootAutoCheck) where they are
        // tested: shared items tick your selected classes / active tab (#98),
        // single-class items tick their class unconditionally (#106 — a Berserker
        // staff looted on the Druid tab is still Berserker progress).
        var myClasses = _w.QuestLedger?.ClassesFor(_w.QuestCharacterKey) ?? [];
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

    internal void OnSkyStateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_skyStateCombo.SelectedItem is not string s) return;
        _skyState = s;
        MarkSkyDirty();
    }

    internal void OnSkySearchChanged(object sender, TextChangedEventArgs e)
    {
        MarkSkyDirty();
        RenderSkyQuestChecklist();
    }

    internal void RenderSkyQuestChecklist()
    {
        if (_skyStateCombo.Items.Count == 0)
        {
            foreach (var s in new[] { "any state", "open", "ready", "done" }) _skyStateCombo.Items.Add(s);
            _skyStateCombo.SelectedIndex = 0;
        }

        // Search (#108, bjstrange): one box instead of a fourteen-tab tour. Crosses
        // every class and ignores the state lens — filters shape tabs, never search
        // (the tracker's rule since 1.57.4). Clearing the box restores the tabs.
        var query = _skySearchBox.Text.Trim();
        _skySearchScroll.Visibility = query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _skyTabs.Visibility = query.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (query.Length > 0)
        {
            RenderSkySearch(query);
            return;
        }
        // Live selection wins; the persisted class restores the tab across restarts.
        var selectedClass = (_skyTabs.SelectedItem as TabItem)?.Tag as string
            ?? (_settings.SkyQuestClass.Length > 0 ? _settings.SkyQuestClass : null);
        _skyTabs.Items.Clear();
        AddSkyReadyAllTab(selectedClass);

        foreach (var classGroup in _settings.SkyQuestChecklist.GroupBy(i => i.ClassName).OrderBy(g => g.Key))
        {
            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            var turnInNpc = classGroup.Select(i => i.Npc).FirstOrDefault(n => n.Length > 0);
            if (!string.IsNullOrWhiteSpace(turnInNpc))
            {
                var npcRow = new TextBlock
                {
                    Text = "Turn-in NPC: " + turnInNpc,
                    FontSize = 10.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 0, 4),
                    ToolTip = $"{classGroup.Key} Plane of Sky turn-in NPC: {turnInNpc}",
                };
                // SetResourceReference so an in-place theme switch repaints rebuilt
                // rows too (house rule; tiny post-merge polish on #127).
                npcRow.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
                panel.Children.Add(npcRow);
            }
            var visibleRewards = 0;

            // Unfinished quests float to the top (Reddit, 2026-08-11), and within
            // the unfinished, CLOSEST TO DONE leads (the 2026-08-13 pass — the
            // question a tab answers is "which quest is actually in reach"). The ✔
            // rows are trophies and read fine from the bottom of the list.
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
                visibleRewards++;
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
                    ToolTip = $"{rewardGroup.Key} - {rewardGroup.First().Npc}\n" +
                              "Check when you've turned everything in — quest complete.",
                    Content = new TextBlock
                    {
                        Text = (completed ? $"✔ {rewardGroup.Key}" : rewardGroup.Key) + progress,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)_w.FindResource("AccentBrush"),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                };
                rewardCheck.Checked += (_, _) =>
                    OnSkyRewardToggled(classGroup.Key, rewardGroup.Key, rewardItems, true);
                rewardCheck.Unchecked += (_, _) =>
                    OnSkyRewardToggled(classGroup.Key, rewardGroup.Key, rewardItems, false);
                panel.Children.Add(rewardCheck);

                // Within a quest, what's MISSING leads; what's banked follows.
                foreach (var item in rewardGroup.OrderBy(i => i.Acquired).ThenBy(i => i.QuestItem))
                {
                    var text = new StackPanel();
                    // * = the auto-tick parked a multi-class item here because no class
                    // lens claimed it (#106) — the player decides where it belongs.
                    text.Children.Add(new TextBlock
                    {
                        Text = item.AcquiredUnassigned ? item.QuestItem + " *" : item.QuestItem,
                        FontSize = 12,
                        Foreground = (Brush)_w.FindResource("TextBrush"),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });
                    text.Children.Add(new TextBlock
                    {
                        Text = item.Source,
                        FontSize = 10,
                        Foreground = (Brush)_w.FindResource("DimBrush"),
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
                        ToolTip = tip,
                        // A completed quest's items are history, not a to-do list.
                        IsEnabled = !completed,
                        Opacity = completed ? 0.55 : 1.0,
                    };
                    check.Checked += (_, _) => OnSkyQuestToggled(item, true);
                    check.Unchecked += (_, _) => OnSkyQuestToggled(item, false);
                    panel.Children.Add(check);
                }
            }

            if (visibleRewards == 0)
                panel.Children.Add(new TextBlock
                {
                    Text = _skyState == "ready"
                        ? "Nothing fully collected yet — \"open\" shows what's still missing."
                        : $"No {_skyState} quests for this class.",
                    FontSize = 11, TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)_w.FindResource("DimBrush"),
                    Margin = new Thickness(0, 4, 0, 4),
                });

            var tab = new TabItem
            {
                Header = SkyQuestTabHeader(classGroup.Key),
                Tag = classGroup.Key,
                Content = new ScrollViewer
                {
                    Content = panel,
                    MaxHeight = SkyQuestListMaxHeight(),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    PanningMode = PanningMode.VerticalOnly,
                    Padding = new Thickness(0, 0, 4, 0),
                },
                ToolTip = SkyQuestTabToolTip(classGroup.Key),
            };
            _skyTabs.Items.Add(tab);
            if (string.Equals(selectedClass, classGroup.Key, StringComparison.Ordinal))
                _skyTabs.SelectedItem = tab;
        }

        if (_skyTabs.SelectedIndex < 0 && _skyTabs.Items.Count > 0)
            _skyTabs.SelectedIndex = 0;
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
                Foreground = (Brush)_w.FindResource("DimBrush"),
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
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)_w.FindResource("AccentBrush"),
                Margin = new Thickness(0, _skySearchResults.Children.Count == 0 ? 2 : 8, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (first.Source.Length > 0)
                _skySearchResults.Children.Add(new TextBlock
                {
                    Text = first.Source, FontSize = 10,
                    Foreground = (Brush)_w.FindResource("DimBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });

            foreach (var item in itemGroup.OrderBy(i => i.ClassName, StringComparer.OrdinalIgnoreCase))
            {
                var completed = IsSkyRewardCompleted(item.ClassName, item.Reward);
                var check = new CheckBox
                {
                    IsChecked = item.Acquired,
                    Margin = new Thickness(8, 1, 0, 1),
                    ToolTip = $"{item.ClassName} — {item.Reward} ({item.Npc})",
                    IsEnabled = !completed,
                    Opacity = completed ? 0.55 : 1.0,
                    Content = new TextBlock
                    {
                        Text = $"{ClassAbbrev(item.ClassName)} · {item.Reward}{(completed ? " ✔" : "")}",
                        FontSize = 11.5,
                        Foreground = (Brush)_w.FindResource("TextBrush"),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                };
                var captured = item;
                check.Checked += (_, _) => OnSkyQuestToggled(captured, true);
                check.Unchecked += (_, _) => OnSkyQuestToggled(captured, false);
                _skySearchResults.Children.Add(check);
            }
        }
    }

    private double SkyQuestListMaxHeight()
    {
        var available = _sectionScroll.MaxHeight > 0 ? _sectionScroll.MaxHeight - 220 : 260;
        return Math.Clamp(available, 180, 320);
    }

    /// <summary>#88 (typical-usual-chaos): read the game's own `/outputfile achievements`
    /// dump and pre-mark Sky rewards completed before EQBuddy existed. Preview first,
    /// nothing applies until confirmed, and the import only ever adds — the same
    /// never-regress rule the AA ledger lives by. Unmatched names are shown, not
    /// silently dropped (reward names drift from the wiki's).</summary>
    internal void OnImportAchievements(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Pick the game's achievements dump ({EQBuddy.UI.Shared.GameCommands.OutputfileAchievements})",
            Filter = "Achievements dump (*.txt)|*.txt|All files (*.*)|*.*",
        };
        // /outputfile writes beside eqgame.exe — the Logs folder's parent.
        if (_settings.LogFolder is { Length: > 0 } lf
            && System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(lf)) is { } root
            && System.IO.Directory.Exists(root))
            dlg.InitialDirectory = root;
        if (dlg.ShowDialog(_w) != true) return;
        try
        {
            var achievements = AchievementsImport.Parse(System.IO.File.ReadLines(dlg.FileName));
            var (matches, unmatched, autoGranted) =
                AchievementsImport.SkyRewards(achievements, _settings.SkyQuestChecklist);
            // The same dump carries the Conqueror sections — the Raids card's memory
            // of clears from before EQBuddy. Marking is add-only and idempotent, so
            // it needs no preview step of its own.
            _raidLedger().MarkAchievements(achievements);
            ShowAchievementsPreview(matches, unmatched, autoGranted, achievements.Count);
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            MessageBox.Show(_w, $"Couldn't read that file — {ex.Message}", "Import achievements");
        }
    }

    /// <summary>The import needs the dump to exist first, and the Raids card (which
    /// carries the ⧉ button) hides itself on a fresh character — so the menu that
    /// offers the import offers the command too (David, 2026-08-14). A closed menu
    /// can't flip to ✓; the header says exactly what the click does instead.</summary>
    internal void OnCopyAchievementsCommand(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(EQBuddy.UI.Shared.GameCommands.OutputfileAchievements); }
        catch { /* clipboard momentarily held by another app */ }
    }

    private void ShowAchievementsPreview(List<SkyRewardMatch> matches, List<string> unmatched,
        List<string> autoGranted, int total)
    {
        var win = new Window
        {
            Title = "Import achievements — preview",
            Width = 460, Height = 480, Owner = _w,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        win.SetResourceReference(Control.BackgroundProperty, "BgBrush");
        var panel = new StackPanel { Margin = new Thickness(10) };
        void Add(string text, string brush, bool bold = false)
        {
            var tb = new TextBlock
            {
                Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 1),
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, brush);
            panel.Children.Add(tb);
        }

        var fresh = matches.Where(m =>
            !IsSkyRewardCompleted(m.ClassName, m.Reward)).ToList();
        Add($"{total} achievements read · {matches.Count} Sky rewards recognized", "TextBrush", bold: true);
        Add(fresh.Count > 0
            ? $"{fresh.Count} will be marked turned-in (the rest already are):"
            : "Everything recognized is already marked — nothing to apply.", "TextBrush");
        foreach (var m in matches)
        {
            var already = !fresh.Contains(m);
            Add($"  ✓ {m.ClassName} — {m.Reward}" + (already ? "   (already marked)" : ""),
                already ? "DimBrush" : "GoodBrush");
        }
        if (autoGranted.Count > 0)
        {
            Add($"Skipped — auto-granted, not earned ({autoGranted.Count}):", "WarnBrush", bold: true);
            Add("Your primary class unlock is granted at creation, and the game marks its " +
                "reward criteria complete without the items ever existing (#101) — so these " +
                "prove nothing and are never imported. Turn them in for real and the Sky " +
                "card tracks them the normal way.", "DimBrush");
            foreach (var g in autoGranted) Add($"  ⊘ {g}", "DimBrush");
        }
        if (unmatched.Count > 0)
        {
            Add($"Completed in the file but not recognized ({unmatched.Count}) — left untouched; " +
                "tell the discussions board and matching improves:", "WarnBrush", bold: true);
            foreach (var u in unmatched) Add($"  ? {u}", "DimBrush");
        }
        Add("Applying only ADDS: nothing currently tracked gets unchecked.", "DimBrush");

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10),
        };
        var apply = Theming.Button($"Apply ({fresh.Count})");
        apply.IsEnabled = fresh.Count > 0;
        apply.Click += (_, _) =>
        {
            AchievementsImport.Apply(matches, _settings);
            _settings.Save();
            UpdateSkyQuestHeaderOnly();
            MarkSkyDirty();
            win.Close();
        };
        var cancel = Theming.Button("Cancel");
        cancel.Margin = new Thickness(8, 0, 0, 0);
        cancel.Click += (_, _) => win.Close();
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
        win.ShowDialog();
    }

    private static string SkyRewardKey(string className, string reward) => className + "|" + reward;

    private bool IsSkyRewardCompleted(string className, string reward) =>
        _settings.SkyQuestCompleted.Contains(SkyRewardKey(className, reward));

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
        MarkSkyDirty();   // rebuild next tick: ✔ label, dimmed items, counts
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
    internal void OnSkyQuestTabChanged(object sender, SelectionChangedEventArgs e)
    {
        // Items.Clear() during a rebuild fires this with no selection — ignore.
        if ((_skyTabs.SelectedItem as TabItem)?.Tag is string cls &&
            !string.Equals(_settings.SkyQuestClass, cls, StringComparison.Ordinal))
        {
            _settings.SkyQuestClass = cls;
            _settings.Save();
        }
    }

    private void UpdateSkyQuestTabHeader(string className)
    {
        foreach (var tab in _skyTabs.Items.OfType<TabItem>())
            if (string.Equals(tab.Tag as string, className, StringComparison.Ordinal))
            {
                tab.Header = SkyQuestTabHeader(className);
                tab.ToolTip = SkyQuestTabToolTip(className);
            }
    }

    /// <summary>#129 (bjstrange): the ready list spans ALL classes — a "★ Ready"
    /// tab ahead of the class tabs, one line per turn-in doable right now, so a
    /// multi-lens player sees every finished quest without touring fourteen tabs.
    /// Only exists while something is actually ready.</summary>
    private void AddSkyReadyAllTab(string? selectedClass)
    {
        var ready = _settings.SkyQuestChecklist
            .GroupBy(i => (i.ClassName, i.Reward))
            .Where(g => g.All(i => i.Acquired)
                && !IsSkyRewardCompleted(g.Key.ClassName, g.Key.Reward))
            .OrderBy(g => g.Key.ClassName).ThenBy(g => g.Key.Reward)
            .ToList();
        if (ready.Count == 0) return;

        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var quest in ready)
        {
            var npc = quest.Select(i => i.Npc).FirstOrDefault(n => n.Length > 0) ?? "";
            var row = new TextBlock
            {
                Text = $"{ClassAbbrev(quest.Key.ClassName)} — {quest.Key.Reward}"
                    + (npc.Length > 0 ? $"  ({npc})" : ""),
                FontSize = 11, Margin = new Thickness(0, 1, 0, 1),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = $"{quest.Key.ClassName}: all {quest.Count()} item{(quest.Count() == 1 ? "" : "s")} acquired"
                    + (npc.Length > 0 ? $" — turn in to {npc}" : ""),
            };
            row.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            panel.Children.Add(row);
        }

        var headerText = new TextBlock { Text = $"★ Ready {ready.Count}", FontSize = 10, FontWeight = FontWeights.SemiBold };
        headerText.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
        var tab = new TabItem
        {
            Header = headerText,
            Tag = "★ready-all",
            ToolTip = $"{ready.Count} quest{(ready.Count == 1 ? "" : "s")} ready to turn in, across all classes",
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(0, 0, 4, 0),
            },
        };
        _skyTabs.Items.Add(tab);
        if (selectedClass == "★ready-all") tab.IsSelected = true;
    }

    private sealed record SkyQuestTabCounts(int Done, int Ready, int Partial, int Total);

    private SkyQuestTabCounts SkyQuestTabCountsFor(string className)
    {
        var rewards = _settings.SkyQuestChecklist
            .Where(i => string.Equals(i.ClassName, className, StringComparison.Ordinal))
            .GroupBy(i => i.Reward)
            .ToList();
        var done = 0;
        var ready = 0;
        var partial = 0;
        foreach (var reward in rewards)
        {
            if (IsSkyRewardCompleted(className, reward.Key))
                done++;
            else if (reward.All(i => i.Acquired))
                ready++;
            else if (reward.Any(i => i.Acquired))
                partial++;
        }

        return new SkyQuestTabCounts(done, ready, partial, rewards.Count);
    }

    private object SkyQuestTabHeader(string className)
    {
        var counts = SkyQuestTabCountsFor(className);
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var cls = new TextBlock
        {
            Text = ClassAbbrev(className) + " ",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
        };
        cls.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        header.Children.Add(cls);
        AddSkyQuestTabMetric(header, "D", counts.Done, "GoodBrush");
        AddSkyQuestTabMetric(header, "R", counts.Ready, "WarnBrush");
        AddSkyQuestTabMetric(header, "P", counts.Partial, "IncomingBrush");
        // The total, because D+R+P deliberately does NOT sum to it — a quest you have
        // not started sits in no bucket. bjstrange read three numbers that didn't add
        // up and reasonably concluded they were wrong (#136). Showing what they are out
        // of turns a puzzle into a subtraction: "2+1+1 of 5, so one is untouched."
        var total = new TextBlock
        {
            Text = $" /{counts.Total}",
            FontSize = 10,
            Margin = new Thickness(3, 0, 0, 0),
        };
        total.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
        header.Children.Add(total);
        return header;
    }

    private void AddSkyQuestTabMetric(StackPanel header, string label, int count, string brushKey)
    {
        var name = new TextBlock
        {
            Text = label,
            FontSize = 10,
            Margin = new Thickness(header.Children.Count == 1 ? 0 : 3, 0, 0, 0),
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
        var value = new TextBlock
        {
            Text = count.ToString(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
        };
        value.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        header.Children.Add(name);
        header.Children.Add(value);
    }

    private string SkyQuestTabToolTip(string className)
    {
        var counts = SkyQuestTabCountsFor(className);
        return $"{className}: {counts.Done} turned in, {counts.Ready} ready to turn in, " +
               $"{counts.Partial} partially complete, {counts.Total} total quests";
    }

    internal void UpdateSkyQuestHeaderOnly()
    {
        var total = _settings.SkyQuestChecklist.Count;
        var acquired = _settings.SkyQuestChecklist.Count(i => i.Acquired);
        _skyHeader.Text = $"{acquired}/{total}";
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
}
