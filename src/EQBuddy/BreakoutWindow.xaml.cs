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

    /// <summary>Raised when the user ✕-dismisses the window — the owner suppresses this
    /// kind until the widget is next minimized.</summary>
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
        Chrome.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        ScopeBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        TitleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        SubText.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
        EmptyText.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");

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
        Closed += (_, _) => SavePosition();
        if (_kind is BreakoutKind.Watch or BreakoutKind.Loot)
            ScopeBorder.Visibility = Visibility.Collapsed;
        ApplyScopeVisual();
    }

    private string ScopeSetting() => _kind switch
    {
        BreakoutKind.Damage => _settings.BreakoutDamageScope,
        BreakoutKind.Healing => _settings.BreakoutHealingScope,
        _ => _settings.BreakoutPetScope,
    };

    private void SetScopeSetting(string v)
    {
        switch (_kind)
        {
            case BreakoutKind.Damage: _settings.BreakoutDamageScope = v; break;
            case BreakoutKind.Healing: _settings.BreakoutHealingScope = v; break;
            case BreakoutKind.Pet: _settings.BreakoutPetScope = v; break;
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
    public void Update(StatsSnapshot s)
    {
        if (_kind == BreakoutKind.Watch) { UpdateWatch(s); return; }
        if (_kind == BreakoutKind.Loot) { UpdateLoot(s); return; }
        var f = s.LastFight;
        var (title, rows, secs, rateLabel) = _kind switch
        {
            BreakoutKind.Damage => ("⚔ Your damage",
                _fightScope ? f?.ByAbility ?? [] : s.DamageBySource,
                _fightScope ? f?.DurationSeconds ?? 0 : s.CombatSeconds, "dps"),
            BreakoutKind.Healing => ("⚕ Your healing",
                _fightScope ? f?.HealsBySpell ?? [] : s.HealsBySpell,
                _fightScope ? f?.DurationSeconds ?? 0 : s.CombatSeconds, "hps"),
            _ => (s.PetName.Length > 0 ? $"🐾 Pet damage — {s.PetName}" : "🐾 Pet damage",
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
        // fights — only re-render when a number moved or the scope/fight changed.
        var sig = $"{_fightScope}|{f?.Name}|{secs:0}|{string.Join(",", rows.Select(r => $"{r.Name}:{r.Total}"))}";
        if (sig == _signature) return;
        _signature = sig;
        BreakdownRows.FillAbilityRows(this, Rows, rows, Math.Max(1, secs), rateLabel, max: 10);
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

    /// <summary>The Loot breakout: while a fight is on (or just ended), the shared
    /// target-drops content — the very thing the minimized player couldn't see (the 🎯
    /// block lives in a card that never renders while minimized); between fights, the
    /// session's loot. Item rows click through to Item info and hover their stats,
    /// same as the card.</summary>
    private void UpdateLoot(StatsSnapshot s)
    {
        var (header, targetRows) = Main?.TargetDropsContent(s) ?? ("", []);
        var fighting = header.Length > 0;
        TitleText.Text = "🎒 Loot";
        SubText.Text = fighting
            ? header.Replace("🎯 Fighting: ", "🎯 ")
            : $"Session · {s.LootTotal} item{(s.LootTotal == 1 ? "" : "s")} looted";

        List<(string Name, string Value)> rows;
        if (fighting)
        {
            rows = targetRows;
        }
        else
        {
            var loot = _settings.LootSort == "name"
                ? s.Loot.OrderBy(l => l.Item, StringComparer.OrdinalIgnoreCase).AsEnumerable()
                : s.Loot;
            rows = loot.Take(12).Select(l => (l.Item, $"×{l.Count}")).ToList();
        }

        var empty = rows.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
        {
            EmptyText.Text = fighting ? "Nothing known for this creature yet." : "No loot seen yet.";
            Rows.Items.Clear();
            _signature = "";
            return;
        }

        var sig = $"loot|{fighting}|{SubText.Text}|{string.Join(",", rows.Select(r => r.Name + r.Value))}";
        if (sig == _signature) return;
        _signature = sig;

        Rows.Items.Clear();
        var barBrush = BreakdownRows.BarBrush(this);
        foreach (var (name, value) in rows)
        {
            var row = BreakdownRows.Row(this, name, value, 0, barBrush,
                Main?.ItemHoverStats(name) ?? "Click for item info (eqlwiki)");
            if (Main is { } main)
            {
                var clickName = name;
                row.Cursor = System.Windows.Input.Cursors.Hand;
                row.MouseLeftButtonDown += (_, e) => e.Handled = true;   // don't start a drag
                row.MouseLeftButtonUp += (_, _) => main.ShowItemInfo(clickName);
            }
            Rows.Items.Add(row);
        }
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
