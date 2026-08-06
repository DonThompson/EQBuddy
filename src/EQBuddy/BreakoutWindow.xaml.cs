using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EQBuddy.Core;

namespace EQBuddy;

/// <summary>Which stat a breakout window tracks. Each kind is one singleton window with its
/// own remembered position and Fight/Session scope.</summary>
public enum BreakoutKind { Damage, Healing, Pet }

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
            default: _settings.BreakoutPetScope = v; break;
        }
        _settings.Save();
    }

    private (double Left, double Top) PositionSetting() => _kind switch
    {
        BreakoutKind.Damage => (_settings.BreakoutDamageLeft, _settings.BreakoutDamageTop),
        BreakoutKind.Healing => (_settings.BreakoutHealingLeft, _settings.BreakoutHealingTop),
        _ => (_settings.BreakoutPetLeft, _settings.BreakoutPetTop),
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
            default:
                _settings.BreakoutPetLeft = Left; _settings.BreakoutPetTop = Top; break;
        }
        _settings.Save();
    }

    /// <summary>Refresh from the 1 s snapshot tick. Rebuilds rows only when the numbers
    /// actually changed (same signature idiom as the chip windows).</summary>
    public void Update(StatsSnapshot s)
    {
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
        SubText.Text = _fightScope
            ? f is null ? "No fights yet"
                : $"{f.Name} · {f.DurationSeconds:0}s · {f.Outcome} · {rate:0.#} {rateLabel}"
            : $"Session · {s.CombatSeconds / 60:0}m in combat · {rate:0.#} {rateLabel}";

        var empty = rows.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
        {
            EmptyText.Text = _kind switch
            {
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
