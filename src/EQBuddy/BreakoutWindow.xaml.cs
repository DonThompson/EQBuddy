using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EQBuddy.Core;

namespace EQBuddy;

/// <summary>Which stat a breakout window tracks. Each kind is one singleton window with its
/// own remembered position and Fight/Session scope (Watch and Loot have no scope — their
/// content is session/target shaped, so the toggle is hidden).</summary>
public enum BreakoutKind { Damage, Healing, Pet, Watch, Loot }

/// <summary>
/// A small floating bar-chart window for one stat — your damage, your healing, or the pet's
/// damage — by ability/spell, scoped to the current pull or the whole session (BREAKOUT-*,
/// David 2026-08-06). Opens automatically while the widget is minimized when the matching
/// section star is set: the stars already mean "this is what I watch when minimized", and
/// the breakout is the full-size version of that promise. ✕ hides it until the next
/// minimize, so an unwanted window never needs its star removed to go away.
///
/// Same chrome family as the spawn/mez chips: frameless, topmost, drag anywhere,
/// ScreenGuard-checked position persisted per kind, theme via resource references so a
/// live theme swap repaints it.
/// </summary>
public partial class BreakoutWindow : Window
{
    private readonly AppSettings _settings;
    private readonly BreakoutKind _kind;

    /// <summary>The owning widget — the Loot kind reads target-drops content and item
    /// click/hover behavior through it (same shared builder the Loot card uses).</summary>
    public MainWindow? Main { get; set; }

    /// <summary>Raised when the user ✕-dismisses the window — the owner disables this
    /// kind persistently (re-enabled under Options → Breakout windows, discussion #45).</summary>
    public event Action<BreakoutKind>? Dismissed;

    private bool _fightScope;
    private string _signature = "";

    public BreakoutWindow(AppSettings settings, BreakoutKind kind)
    {
        InitializeComponent();
        _settings = settings;
        _kind = kind;
        Title = $"EQBuddy {kind} breakout";
        _fightScope = ScopeSetting() != "session";

        Chrome.SetResourceReference(Border.BackgroundProperty, "BgBrush");
        // Hairline chrome (2026-08-11 modernization): the accent at a whisper, same
        // treatment as the main widget's cards.
        Chrome.SetResourceReference(Border.BorderBrushProperty, "HairlineBrush");
        ScopeBorder.SetResourceReference(Border.BorderBrushProperty, "HairlineBrush");
        TitleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        SubText.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
        EmptyText.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");

        // Sort links only make sense for ability-stat rows.
        SortBar.Visibility = _kind is BreakoutKind.Watch or BreakoutKind.Loot
            ? Visibility.Collapsed : Visibility.Visible;
        if (_kind == BreakoutKind.Healing) SortRate.Text = "hps";
        _sort = ParseSort(SortSetting());
        ApplySortVisual();

        var (left, top) = PositionSetting();
        if (ScreenGuard.OnScreen(left, top, Width, 120)) { Left = left; Top = top; }
        else
        {
            // Default column on the work area's right edge, staggered per kind so three
            // fresh windows never open on top of each other.
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 40;
            Top = area.Top + 80 + 150 * (int)kind;
        }

        // Auto-size mode still caps the list so a 40-item session can't run the window
        // off the screen; a manual size (grip-dragged, persisted) takes over entirely.
        RowsScroll.MaxHeight = SystemParameters.WorkArea.Height * 0.6;
        var (savedW, savedH) = SizeSetting();
        if (savedW is >= MinManualWidth and <= 900 && savedH is >= MinManualHeight)
        {
            SizeToContent = SizeToContent.Manual;
            Width = savedW;
            Height = Math.Min(savedH, SystemParameters.WorkArea.Height);
            RowsScroll.MaxHeight = double.PositiveInfinity;
        }

        Closed += (_, _) => SavePosition();
        WindowZoom.Attach(this, $"breakout:{kind}", settings);
        if (_kind == BreakoutKind.Damage)
        {
            CopyFight.Visibility = Visibility.Visible;
            OpenTimeline.Visibility = Visibility.Visible;
        }
        if (_kind == BreakoutKind.Watch) ScopeBorder.Visibility = Visibility.Collapsed;
        if (_kind == BreakoutKind.Loot)
        {
            // Same toggle chrome, different axis: what the TARGET can drop vs what the
            // SESSION has yielded (David, 2026-08-06).
            ScopeFight.Text = "Target";
            ScopeSession.Text = "Session";
        }
        ApplyScopeVisual();
    }

    private string ScopeSetting() => _kind switch
    {
        BreakoutKind.Damage => _settings.BreakoutDamageScope,
        BreakoutKind.Healing => _settings.BreakoutHealingScope,
        BreakoutKind.Loot => _settings.BreakoutLootScope == "session" ? "session" : "fight",
        _ => _settings.BreakoutPetScope,
    };

    private void SetScopeSetting(string v)
    {
        switch (_kind)
        {
            case BreakoutKind.Damage: _settings.BreakoutDamageScope = v; break;
            case BreakoutKind.Healing: _settings.BreakoutHealingScope = v; break;
            case BreakoutKind.Pet: _settings.BreakoutPetScope = v; break;
            case BreakoutKind.Loot:
                _settings.BreakoutLootScope = v == "session" ? "session" : "target"; break;
        }
        _settings.Save();
    }

    private (double Left, double Top) PositionSetting() => _kind switch
    {
        BreakoutKind.Damage => (_settings.BreakoutDamageLeft, _settings.BreakoutDamageTop),
        BreakoutKind.Healing => (_settings.BreakoutHealingLeft, _settings.BreakoutHealingTop),
        BreakoutKind.Pet => (_settings.BreakoutPetLeft, _settings.BreakoutPetTop),
        BreakoutKind.Watch => (_settings.BreakoutWatchLeft, _settings.BreakoutWatchTop),
        _ => (_settings.BreakoutLootLeft, _settings.BreakoutLootTop),
    };

    private StatSort _sort = StatSort.Total;

    private static StatSort ParseSort(string v) => v switch
    {
        "hits" => StatSort.Hits, "avg" => StatSort.Avg, "rate" => StatSort.Rate,
        _ => StatSort.Total,
    };

    private string SortSetting() => _kind switch
    {
        BreakoutKind.Healing => _settings.BreakoutHealingSort,
        BreakoutKind.Pet => _settings.BreakoutPetSort,
        _ => _settings.BreakoutDamageSort,
    };

    private void OnSortClick(object sender, MouseButtonEventArgs e)
    {
        var key = (string)((FrameworkElement)sender).Tag;
        _sort = ParseSort(key);
        switch (_kind)
        {
            case BreakoutKind.Healing: _settings.BreakoutHealingSort = key; break;
            case BreakoutKind.Pet: _settings.BreakoutPetSort = key; break;
            default: _settings.BreakoutDamageSort = key; break;
        }
        _settings.Save();
        ApplySortVisual();
        e.Handled = true;
    }

    private void ApplySortVisual()
    {
        foreach (var (tb, key) in new[]
            { (SortTotal, "total"), (SortHits, "hits"), (SortAvg, "avg"), (SortRate, "rate") })
            tb.SetResourceReference(TextBlock.ForegroundProperty,
                ParseSort(key) == _sort ? "AccentBrush" : "DimBrush");
        _signature = "";   // force a repaint in the new order on the next tick
    }

    private const double MinManualWidth = 200;
    private const double MinManualHeight = 120;

    private (double W, double H) SizeSetting() => _kind switch
    {
        BreakoutKind.Damage => (_settings.BreakoutDamageWidth, _settings.BreakoutDamageHeight),
        BreakoutKind.Healing => (_settings.BreakoutHealingWidth, _settings.BreakoutHealingHeight),
        BreakoutKind.Pet => (_settings.BreakoutPetWidth, _settings.BreakoutPetHeight),
        BreakoutKind.Watch => (_settings.BreakoutWatchWidth, _settings.BreakoutWatchHeight),
        _ => (_settings.BreakoutLootWidth, _settings.BreakoutLootHeight),
    };

    private void SetSizeSetting(double w, double h)
    {
        switch (_kind)
        {
            case BreakoutKind.Damage:
                _settings.BreakoutDamageWidth = w; _settings.BreakoutDamageHeight = h; break;
            case BreakoutKind.Healing:
                _settings.BreakoutHealingWidth = w; _settings.BreakoutHealingHeight = h; break;
            case BreakoutKind.Pet:
                _settings.BreakoutPetWidth = w; _settings.BreakoutPetHeight = h; break;
            case BreakoutKind.Watch:
                _settings.BreakoutWatchWidth = w; _settings.BreakoutWatchHeight = h; break;
            default:
                _settings.BreakoutLootWidth = w; _settings.BreakoutLootHeight = h; break;
        }
    }

    /// <summary>First resize gesture of any kind: freeze the current auto size and take
    /// manual control, so the resize isn't immediately undone by SizeToContent.</summary>
    private void EnterManualSize()
    {
        if (SizeToContent == SizeToContent.Manual) return;
        var w = ActualWidth; var h = ActualHeight;
        SizeToContent = SizeToContent.Manual;
        Width = w; Height = h;
        RowsScroll.MaxHeight = double.PositiveInfinity;
    }

    private void OnGripDrag(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        EnterManualSize();
        Width = Math.Clamp(Width + e.HorizontalChange, MinManualWidth, 900);
        Height = Math.Clamp(Height + e.VerticalChange, MinManualHeight,
            SystemParameters.WorkArea.Height);
    }

    // ---- native edge-resize (discussion feedback via David, 2026-08-06: "I still can't
    // resize the loot window" — a frameless window has no resize borders, and a corner
    // glyph nobody finds isn't an affordance). WM_NCHITTEST maps the right/bottom edges
    // to resize zones so the window behaves like windows do; the grip stays as the
    // visible hint. ----

    private const int WmNcHitTest = 0x84;
    private const int WmNcLButtonDown = 0xA1;
    private const int WmExitSizeMove = 0x232;
    private const int HtLeft = 10, HtRight = 11, HtTop = 12, HtTopLeft = 13,
        HtTopRight = 14, HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource src)
            src.AddHook(ResizeHook);
    }

    private IntPtr ResizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmNcHitTest:
            {
                // lParam: screen coords, low word X, high word Y (signed for multi-monitor).
                var x = (short)((long)lParam & 0xFFFF);
                var y = (short)(((long)lParam >> 16) & 0xFFFF);
                var p = PointFromScreen(new Point(x, y));
                // Any side, any corner (David: resize like a normal window). Zone math is
                // pure and unit-tested in EQBuddy.UI.Shared.ResizeZones.
                var hit = EQBuddy.UI.Shared.ResizeZones.Hit(p.X, p.Y, ActualWidth, ActualHeight);
                if (hit != 0) { handled = true; return hit; }
                break;
            }
            case WmNcLButtonDown when (long)wParam is >= HtLeft and <= HtBottomRight:
                // The native size loop is about to start — leave SizeToContent first or
                // the height snaps back the moment layout runs.
                EnterManualSize();
                break;
            case WmExitSizeMove when SizeToContent == SizeToContent.Manual:
                // ActualWidth/Height, not Width/Height: the native size loop moves the
                // window without writing the dependency properties. SavePosition too — a
                // top/left resize moves the window's origin.
                Width = ActualWidth;
                Height = ActualHeight;
                SetSizeSetting(ActualWidth, ActualHeight);
                SavePosition();
                break;
        }
        return IntPtr.Zero;
    }

    private void OnGripDone(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        SetSizeSetting(Width, Height);
        _settings.Save();
    }

    private void OnGripReset(object sender, MouseButtonEventArgs e)
    {
        // Back to auto-size: forget the manual size and let content drive height again.
        SetSizeSetting(double.NaN, double.NaN);
        _settings.Save();
        Width = 272;
        ClearValue(HeightProperty);
        RowsScroll.MaxHeight = SystemParameters.WorkArea.Height * 0.6;
        SizeToContent = SizeToContent.Height;
        e.Handled = true;
    }

    /// <summary>Persist the spot on hide as well as close — the window is hidden and
    /// re-shown across minimize cycles, and only the last Closed would otherwise count.</summary>
    public void SavePosition()
    {
        switch (_kind)
        {
            case BreakoutKind.Damage:
                _settings.BreakoutDamageLeft = Left; _settings.BreakoutDamageTop = Top; break;
            case BreakoutKind.Healing:
                _settings.BreakoutHealingLeft = Left; _settings.BreakoutHealingTop = Top; break;
            case BreakoutKind.Pet:
                _settings.BreakoutPetLeft = Left; _settings.BreakoutPetTop = Top; break;
            case BreakoutKind.Watch:
                _settings.BreakoutWatchLeft = Left; _settings.BreakoutWatchTop = Top; break;
            default:
                _settings.BreakoutLootLeft = Left; _settings.BreakoutLootTop = Top; break;
        }
        _settings.Save();
    }

    /// <summary>Refresh from the 1 s snapshot tick. Rebuilds rows only when the numbers
    /// actually changed (same signature idiom as the chip windows).</summary>
    // ---- background see-through (#96, badly-developed): breakouts follow the main
    // widget's setting — only the panel fades, text stays sharp, same rule as the
    // widget. Re-checked on the shared tick so Options changes and theme switches
    // reach an already-open breakout without a rebuild.
    private (double Opacity, Color Tint) _appliedBg = (-1, default);

    private void ApplyBackgroundOpacity()
    {
        var opacity = _settings.BackgroundOpacity;
        var tint = ((SolidColorBrush)FindResource("BgBrush")).Color;
        if (_appliedBg == (opacity, tint)) return;
        _appliedBg = (opacity, tint);
        Chrome.Background = new SolidColorBrush(
            Color.FromArgb((byte)(opacity * 255), tint.R, tint.G, tint.B));
    }

    public void Update(StatsSnapshot s)
    {
        ApplyBackgroundOpacity();
        if (_kind == BreakoutKind.Watch) { UpdateWatch(s); return; }
        if (_kind == BreakoutKind.Loot) { UpdateLoot(s); return; }
        _lastFight = s.LastFight;
        _resists = MainWindow.SpellResistLookup(s);
        _blockedBy = Main?.BlockedByLookup(s);
        var f = s.LastFight;
        var (title, rows, secs, rateLabel) = _kind switch
        {
            BreakoutKind.Damage => ("⚔ Your damage",
                _fightScope ? f?.ByAbility ?? [] : s.DamageBySource,
                _fightScope ? f?.DurationSeconds ?? 0 : s.CombatSeconds, "dps"),
            BreakoutKind.Healing => ("⚕ Your healing",
                _fightScope ? f?.HealsBySpell ?? [] : s.HealsBySpell,
                _fightScope ? f?.DurationSeconds ?? 0 : s.CombatSeconds, "hps"),
            _ => (s.PetName.Length > 0
                    ? $"🐾 Pet damage — {s.PetName}" + EQBuddy.UI.Shared.CharmHoldText.Suffix(s.CharmedSince, DateTime.Now)
                    : "🐾 Pet damage",
                _fightScope ? f?.PetAbilities ?? [] : s.PetAbilities,
                _fightScope ? f?.DurationSeconds ?? 0 : s.CombatSeconds, "dps"),
        };
        TitleText.Text = title;

        var total = rows.Sum(r => r.Total);
        var rate = total / Math.Max(1, secs);
        // Hymn/regen ticks carry no amounts in the log, so they can never join the HPS
        // rows — but a bard mid-song staring at "no healing" reads it as broken (David,
        // live test 2026-08-06). Count them where healing lives; estimate when attributed.
        var regen = _kind == BreakoutKind.Healing && s.RegenTicks > 0
            ? s.RegenEstimatedHealed > 0
                ? $" · est. ~{s.RegenEstimatedHealed:N0} regen ({s.RegenTicks} ticks)"
                : $" · {s.RegenTicks} regen ticks"
            : "";
        SubText.Text = (_fightScope
            ? f is null ? "No fights yet"
                : $"{f.Name} · {f.DurationSeconds:0}s · {f.Outcome} · {rate:0.#} {rateLabel}"
            : $"Session · {s.CombatSeconds / 60:0}m in combat · {rate:0.#} {rateLabel}") + regen;

        var empty = rows.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
        {
            EmptyText.Text = _kind switch
            {
                BreakoutKind.Healing when s.RegenEstimatedHealed > 0 =>
                    $"{s.RegenSpell}: est. ~{s.RegenEstimatedHealed:N0} healed over {s.RegenTicks} ticks.\n" +
                    "The game logs no amounts — this is ticks × your Options\nhp/tick (or the wiki base), so it stays labeled est.",
                BreakoutKind.Healing when s.RegenTicks > 0 =>
                    $"{s.RegenTicks} hymn/regen ticks — the game logs no amounts for these,\nso they count but can't join the HPS rows.",
                BreakoutKind.Healing => "No healing seen yet.",
                BreakoutKind.Pet => "No pet damage seen yet.",
                _ => "No damage seen yet.",
            };
            Rows.Items.Clear();
            _signature = "";
            return;
        }

        // Signature: rebuilding ten bar rows every second is cheap but pointless between
        // fights — only re-render when a number moved or the scope/fight/sort changed.
        var sig = $"{_fightScope}|{_sort}|{f?.Name}|{secs:0}|{string.Join(",", rows.Select(r => $"{r.Name}:{r.Total}"))}";
        if (sig == _signature) return;
        _signature = sig;
        // Resist % rides only the session-scope damage rows — the tallies are
        // session-wide, and stamping them on a single fight would misstate it.
        var resists = _kind == BreakoutKind.Damage && !_fightScope ? _resists : null;
        BreakdownRows.FillAbilityRowsSorted(this, Rows, rows, _sort, Math.Max(1, secs), rateLabel,
            max: 10, resists: resists, blockedBy: resists is null ? null : _blockedBy);
    }

    private LastFightInfo? _lastFight;
    private IReadOnlyDictionary<string, (int Casts, int Resists, int Blocked)>? _resists;
    private IReadOnlyDictionary<string, string>? _blockedBy;

    /// <summary>#102 (jeremycranfill): the Combat card's fight export without leaving
    /// the minimized view — same Discord-ready text, same clipboard.</summary>
    private void OnOpenTimeline(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;   // not a window drag
        Main?.OpenFightTimeline();
    }

    private void OnCopyFight(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_lastFight is not { } f) return;
        try
        {
            Clipboard.SetText(EQBuddy.UI.Shared.FightExport.ToText(
                f, Main?.Identity.Character ?? "", $"v{UpdateChecker.CurrentVersion}"));
            CopyFight.Text = "✓";
            var t = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromSeconds(1.5) };
            t.Tick += (_, _) => { CopyFight.Text = "⧉"; t.Stop(); };
            t.Start();
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    /// <summary>The Watch breakout: every 📌-pinned rule as a bar row — count, last match,
    /// per-hour rate. "Search an item and add it to the window" is exactly what adding and
    /// pinning a watch rule already does, so the window rides that instead of inventing a
    /// second tracking system (CrispyPigeon131's mote window, discussion #44).</summary>
    private void UpdateWatch(StatsSnapshot s)
    {
        TitleText.Text = "🎯 Watch list";
        var pinnedIds = _settings.TrackedRules
            .Where(r => r.Enabled && r.Pinned).Select(r => r.Id)
            .ToHashSet(StringComparer.Ordinal);
        var rows = s.Tracked.Where(t => pinnedIds.Contains(t.Id)).ToList();

        var total = rows.Sum(r => r.TotalQuantity);
        SubText.Text = $"Session · {rows.Count} pinned rule{(rows.Count == 1 ? "" : "s")} · {total} total";

        var empty = rows.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
        {
            EmptyText.Text = "Pin 📌 a watch rule in Options to track it here.";
            Rows.Items.Clear();
            _signature = "";
            return;
        }

        var sig = "watch|" + string.Join(",", rows.Select(r => $"{r.Id}:{r.TotalQuantity}:{r.LastItem}"));
        if (sig == _signature) return;
        _signature = sig;

        Rows.Items.Clear();
        var top = Math.Max(1, rows.Max(r => r.TotalQuantity));
        var barBrush = BreakdownRows.BarBrush(this);
        foreach (var r in rows.OrderByDescending(x => x.TotalQuantity))
        {
            var value = $"{r.TotalQuantity} · {r.PerHour:0.#}/hr";
            var tooltip = r.LastItem is { Length: > 0 } li ? $"last: {li}" : null;
            Rows.Items.Add(BreakdownRows.Row(this, r.Name, value,
                (double)r.TotalQuantity / top, barBrush, tooltip));
        }
    }

    /// <summary>The Loot breakout, Target|Session toggled (David's spec, 2026-08-06):
    /// Target = what the creature you're fighting — or last /considered — can drop, your
    /// observed counts and % leading, wiki drops behind, values from your own sales or
    /// the wiki. Session = what you've looted. Hovering a row fetches the eqlwiki item
    /// info on the spot; clicking opens the eqlwiki page in the browser.</summary>
    private void UpdateLoot(StatsSnapshot s)
    {
        TitleText.Text = "🎒 Loot";
        List<(string Name, string Value)> rows;
        List<(string Name, string Value)> crafted = [];
        string emptyText;
        if (_fightScope)   // = Target scope for this kind
        {
            var (header, targetRows) = Main?.TargetDropsContent(s) ?? ("", []);
            var hasTarget = header.Length > 0;
            SubText.Text = hasTarget ? header.Replace("🎯 Fighting: ", "🎯 ") : "No target";
            rows = targetRows;
            emptyText = hasTarget
                ? Main?.TargetEmptyNote(s) ?? "Nothing known for this creature yet."
                : "Swing at something — or /consider it — and its\npossible drops appear here.";
        }
        else
        {
            // "+N made" echoes the card header, so the two views agree at a glance (#131).
            SubText.Text = $"Session · {s.LootTotal} item{(s.LootTotal == 1 ? "" : "s")} looted"
                + (s.CraftedTotal > 0 ? $" · +{s.CraftedTotal} made" : "");
            var loot = _settings.LootSort == "name"
                ? s.Loot.OrderBy(l => l.Item, StringComparer.OrdinalIgnoreCase).AsEnumerable()
                : s.Loot;
            rows = loot.Take(12).Select(l => (l.Item, $"×{l.Count}")).ToList();
            // Made items ride the Session scope only: crafts are session-cumulative, and
            // Target scope is a different axis entirely — what a creature can drop —
            // where an item nobody drops would misstate the view (#131, TropicMike).
            crafted = s.Crafted.Take(12).Select(c => (c.Name, $"×{c.Count}")).ToList();
            emptyText = "No loot seen yet.";
        }

        var empty = rows.Count == 0 && crafted.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
        {
            EmptyText.Text = emptyText;
            Rows.Items.Clear();
            _signature = "";
            return;
        }

        var sig = $"loot|{_fightScope}|{SubText.Text}|{string.Join(",", rows.Select(r => r.Name + r.Value))}"
            + $"|made:{string.Join(",", crafted.Select(c => c.Name + c.Value))}";
        if (sig == _signature) return;
        _signature = sig;

        Rows.Items.Clear();
        var barBrush = BreakdownRows.BarBrush(this);
        foreach (var (name, value) in rows)
            Rows.Items.Add(BuildItemRow(name, value, barBrush));
        if (crafted.Count > 0)
        {
            // Same label text as the Loot card — one vocabulary for merges everywhere.
            var label = new TextBlock { Text = "Created by merging" };
            label.Style = (Style)FindResource("SectionLabel");
            Rows.Items.Add(label);
            // Crafted items are real items with wiki pages, so they get the same
            // hover/click affordances as loot rows.
            foreach (var (name, value) in crafted)
                Rows.Items.Add(BuildItemRow(name, value, barBrush));
        }
    }

    /// <summary>An item row wired the way David specced the breakout: hover = the eqlwiki
    /// item info, fetched on the spot if the cache is empty (the tooltip live-updates
    /// from "Looking up…"); click = the eqlwiki page in the browser.</summary>
    private Grid BuildItemRow(string name, string value, Brush barBrush)
    {
        var cachedTip = Main?.CachedItemStats(name);

        // Quest loot in the minimized Loot window carries the same 🗺 as the Loot card:
        // click → the Quest Tracker filtered to this item's quests (David, 2026-08-07 —
        // "the one we see when minimizing EQBuddy"). The row itself stays "click = the
        // item's wiki page".
        TextBlock? badge = null;
        if (Main is { } m && m.IsActiveQuestItem(name))
        {
            badge = new TextBlock
            {
                Text = "🗺", FontSize = 11, Margin = new Thickness(6, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Part of a quest — click for its quest info",
            };
            badge.SetResourceReference(TextBlock.ForegroundProperty, "GoodBrush");
            var badgeItem = name;
            badge.MouseLeftButtonDown += (_, e) => e.Handled = true;
            badge.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                m.OpenQuestInfoForItem(badgeItem);
            };
        }

        var row = BreakdownRows.Row(this, name, value, 0, barBrush, null, nameBadge: badge);
        var tipText = new TextBlock
        {
            Text = cachedTip ?? "Looking up on eqlwiki…",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 340,
            FontFamily = new FontFamily("Consolas"),
        };
        var tip = new System.Windows.Controls.ToolTip { Content = tipText };
        row.ToolTip = tip;

        var fetched = false;
        tip.Opened += async (_, _) =>
        {
            // Fetch once per row lifetime; a cache hit inside FetchItemTooltip is free.
            if (fetched || Main is not { } main) return;
            fetched = true;
            var text = await main.FetchItemTooltip(name);
            tipText.Text = text ?? (cachedTip ?? "Not on the wiki.");
        };

        row.Cursor = System.Windows.Input.Cursors.Hand;
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;   // don't start a window drag
        row.MouseLeftButtonUp += (_, _) => MainWindow.OpenWikiPage(name);
        return row;
    }

    private void ApplyScopeVisual()
    {
        Highlight(ScopeFight, _fightScope);
        Highlight(ScopeSession, !_fightScope);
        _signature = "";

        void Highlight(TextBlock t, bool on)
        {
            t.SetResourceReference(TextBlock.ForegroundProperty, on ? "AccentBrush" : "DimBrush");
            if (on) t.SetResourceReference(TextBlock.BackgroundProperty, "ToggleHighlightBrush");
            else t.Background = Brushes.Transparent;
        }
    }

    private void OnScopeFight(object sender, MouseButtonEventArgs e)
    {
        _fightScope = true; SetScopeSetting("fight"); ApplyScopeVisual(); e.Handled = true;
    }

    private void OnScopeSession(object sender, MouseButtonEventArgs e)
    {
        _fightScope = false; SetScopeSetting("session"); ApplyScopeVisual(); e.Handled = true;
    }

    private void OnDismiss(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SavePosition();
        Hide();
        Dismissed?.Invoke(_kind);
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
