using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Avalonia;

public enum BreakoutKind { Damage, Healing, Pet }

/// <summary>Floating fight/session bar chart for outgoing damage, healing, or pet damage.</summary>
public sealed class BreakoutWindow : Window
{
    private readonly AppSettings _settings;
    private readonly BreakoutKind _kind;
    private readonly Border _chrome;
    private readonly TextBlock _title = new();
    private readonly TextBlock _subtitle = AppTheme.DimText("");
    private readonly TextBlock _empty = AppTheme.DimText("");
    private readonly StackPanel _rows = new();
    private readonly Button _fight;
    private readonly Button _session;
    private readonly Button? _copyFight;
    private bool _fightScope;
    private string _signature = "";
    private PixelPoint _savedPosition;
    private LastFightInfo? _lastFight;
    private IReadOnlyDictionary<string, (int Casts, int Resists)>? _resists;
    private (double Opacity, Color Tint) _appliedBg = (-1, default);

    /// <summary>Raised when the user ✕-dismisses the window — the owner disables this
    /// kind persistently (re-enabled under Options → Breakout windows, discussion #45).</summary>
    public event Action<BreakoutKind>? Dismissed;

    /// <summary>Damage kind only (#102): ⧗ opens the fight timeline. Set by the owner
    /// (WPF reaches this through its MainWindow reference; here the hook is explicit).</summary>
    public Action? OpenTimeline { get; set; }

    /// <summary>Whose parse the ⧉ fight export is labeled with — the owner supplies the
    /// current character name (WPF: Main.Identity.Character).</summary>
    public Func<string>? CharacterName { get; set; }

    public BreakoutWindow(AppSettings settings, BreakoutKind kind)
    {
        _settings = settings;
        _kind = kind;
        _fightScope = ScopeSetting() != "session";
        Title = $"EQBuddy {kind} breakout";
        Width = 310;
        SizeToContent = SizeToContent.Height;
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;

        _title.FontSize = 13;
        _title.FontWeight = FontWeight.Bold;
        _title.Foreground = AppTheme.TextBrush;
        _fight = ScopeButton("Fight", true);
        _session = ScopeButton("Session", false);
        var close = AppTheme.IconButton("x",
            "Hide this window for good (its ⭐ chip stays; re-enable under ⚙ Options → Breakout windows)");
        close.Click += (_, _) => { HideAndSave(); Dismissed?.Invoke(_kind); };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,Auto") };
        header.Children.Add(_title);
        if (_kind == BreakoutKind.Damage)
        {
            // #102 (jeremycranfill): the Combat card's fight export and timeline,
            // reachable without leaving the minimized view.
            _copyFight = AppTheme.IconButton("⧉",
                "Copy the last fight as Discord-ready text (a monospace block — the official Discord " +
                "blocks images, so the parse travels as text). Your numbers only, from your log.");
            _copyFight.FontSize = 11;
            _copyFight.Click += async (_, _) => await OnCopyFight();
            Grid.SetColumn(_copyFight, 1); header.Children.Add(_copyFight);
            var timeline = AppTheme.IconButton("⧗",
                "Fight timeline: the whole pull, a lane per skill — every hit, miss and resist, " +
                "plus DPS over time.");
            timeline.FontSize = 11;
            timeline.Click += (_, _) => OpenTimeline?.Invoke();
            Grid.SetColumn(timeline, 2); header.Children.Add(timeline);
        }
        Grid.SetColumn(_fight, 3); header.Children.Add(_fight);
        Grid.SetColumn(_session, 4); header.Children.Add(_session);
        Grid.SetColumn(close, 5); header.Children.Add(close);
        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(_subtitle);
        panel.Children.Add(_empty);
        panel.Children.Add(_rows);
        // Hairline chrome (2026-08-11 modernization): the accent at a whisper, same
        // treatment as the main widget's cards.
        _chrome = new Border
        {
            Background = AppTheme.BgBrush,
            BorderBrush = AppTheme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 7, 10, 9),
            Child = panel,
        };
        Content = _chrome;
        WindowZoom.Attach(this, $"breakout:{kind}", settings);
        PointerPressed += (_, e) =>
        {
            if (e.Source is not Button && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        Opened += (_, _) => RestorePosition();
        PositionChanged += (_, _) => { if (IsVisible) _savedPosition = Position; };
        Closed += (_, _) => SavePosition();
        PaintScope();
    }

    private Button ScopeButton(string text, bool fight)
    {
        var button = AppTheme.IconButton(text, $"Show {text.ToLowerInvariant()} numbers");
        button.FontSize = 11;
        button.Click += (_, _) =>
        {
            _fightScope = fight;
            SetScopeSetting(fight ? "fight" : "session");
            _signature = "";
            PaintScope();
        };
        return button;
    }

    /// <summary>#102: the Combat card's fight export without leaving the minimized
    /// view — same Discord-ready text, same clipboard.</summary>
    private async Task OnCopyFight()
    {
        if (_lastFight is not { } f || _copyFight is null) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(FightExport.ToText(
                    f, CharacterName?.Invoke() ?? "", $"v{UpdateChecker.CurrentVersion}"));
            _copyFight.Content = "✓";
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            t.Tick += (_, _) => { _copyFight.Content = "⧉"; t.Stop(); };
            t.Start();
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    // ---- background see-through (#96, badly-developed): breakouts follow the main
    // widget's setting — only the panel fades, text stays sharp, same rule as the
    // widget. Re-checked on the shared tick so Options changes and theme switches
    // reach an already-open breakout without a rebuild.
    private void ApplyBackgroundOpacity()
    {
        var opacity = _settings.BackgroundOpacity;
        var tint = AppTheme.BgBrush.Color;
        if (_appliedBg == (opacity, tint)) return;
        _appliedBg = (opacity, tint);
        _chrome.Background = new SolidColorBrush(
            Color.FromArgb((byte)(opacity * 255), tint.R, tint.G, tint.B));
    }

    /// <summary>Refresh from the 1 s snapshot tick. Rebuilds rows only when the numbers
    /// actually changed (same signature idiom as the chip windows).</summary>
    public void Update(StatsSnapshot s)
    {
        ApplyBackgroundOpacity();
        _lastFight = s.LastFight;
        _resists = MainWindow.SpellResistLookup(s);
        var fight = s.LastFight;
        var (title, stats, seconds, rateLabel) = _kind switch
        {
            BreakoutKind.Damage => ("⚔ Your damage", _fightScope ? fight?.ByAbility ?? [] : s.DamageBySource,
                _fightScope ? fight?.DurationSeconds ?? 0 : s.CombatSeconds, "dps"),
            BreakoutKind.Healing => ("⚕ Your healing", _fightScope ? fight?.HealsBySpell ?? [] : s.HealsBySpell,
                _fightScope ? fight?.DurationSeconds ?? 0 : s.CombatSeconds, "hps"),
            _ => (s.PetName.Length > 0
                    ? $"🐾 Pet damage — {s.PetName}" + EQBuddy.UI.Shared.CharmHoldText.Suffix(s.CharmedSince, DateTime.Now)
                    : "🐾 Pet damage",
                _fightScope ? fight?.PetAbilities ?? [] : s.PetAbilities,
                _fightScope ? fight?.DurationSeconds ?? 0 : s.CombatSeconds, "dps"),
        };
        _title.Text = title;
        var rate = stats.Sum(row => row.Total) / Math.Max(1, seconds);
        // Hymn/regen ticks carry no amounts in the log, so they can never join the HPS
        // rows — but a bard mid-song staring at "no healing" reads it as broken (David,
        // live test 2026-08-06). Count them where healing lives; estimate when attributed.
        var regen = _kind == BreakoutKind.Healing && s.RegenTicks > 0
            ? s.RegenEstimatedHealed > 0
                ? $" · est. ~{s.RegenEstimatedHealed:N0} regen ({s.RegenTicks} ticks)"
                : $" · {s.RegenTicks} regen ticks"
            : "";
        _subtitle.Text = (_fightScope
            ? fight is null ? "No fights yet"
                : $"{fight.Name} · {fight.DurationSeconds:0}s · {fight.Outcome} · {rate:0.#} {rateLabel}"
            : $"Session · {s.CombatSeconds / 60:0}m in combat · {rate:0.#} {rateLabel}") + regen;
        var empty = stats.Count == 0;
        _empty.IsVisible = empty;
        if (empty)
        {
            _empty.Text = _kind switch
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
            _rows.Children.Clear();
            _signature = "";
            return;
        }
        var signature = $"{_fightScope}|{fight?.Name}|{seconds:0}|{string.Join(',', stats.Select(row => $"{row.Name}:{row.Total}"))}";
        if (signature == _signature) return;
        _signature = signature;
        // Resist % rides only the session-scope damage rows — the tallies are
        // session-wide, and stamping them on a single fight would misstate it.
        var resists = _kind == BreakoutKind.Damage && !_fightScope ? _resists : null;
        BreakdownRows.FillAbilityRowsSorted(_rows, stats, StatSort.Total, Math.Max(1, seconds),
            rateLabel, max: 10, resists: resists);
    }

    public void HideAndSave() { SavePosition(); Hide(); }

    private void RestorePosition()
    {
        var (left, top) = PositionSetting();
        if (ScreenGuard.OnScreen(this, left, top, Width, 120)) Position = new PixelPoint((int)left, (int)top);
        else if (Screens.Primary is { } screen)
            Position = new PixelPoint(screen.WorkingArea.Right - (int)(Width * screen.Scaling) - 40,
                screen.WorkingArea.Y + 80 + 150 * (int)_kind);
        _savedPosition = Position;
    }

    private void SavePosition()
    {
        var p = _savedPosition;
        switch (_kind)
        {
            case BreakoutKind.Damage: _settings.BreakoutDamageLeft = p.X; _settings.BreakoutDamageTop = p.Y; break;
            case BreakoutKind.Healing: _settings.BreakoutHealingLeft = p.X; _settings.BreakoutHealingTop = p.Y; break;
            default: _settings.BreakoutPetLeft = p.X; _settings.BreakoutPetTop = p.Y; break;
        }
        _settings.Save();
    }

    private (double, double) PositionSetting() => _kind switch
    {
        BreakoutKind.Damage => (_settings.BreakoutDamageLeft, _settings.BreakoutDamageTop),
        BreakoutKind.Healing => (_settings.BreakoutHealingLeft, _settings.BreakoutHealingTop),
        _ => (_settings.BreakoutPetLeft, _settings.BreakoutPetTop),
    };

    private string ScopeSetting() => _kind switch
    {
        BreakoutKind.Damage => _settings.BreakoutDamageScope,
        BreakoutKind.Healing => _settings.BreakoutHealingScope,
        _ => _settings.BreakoutPetScope,
    };

    private void SetScopeSetting(string value)
    {
        if (_kind == BreakoutKind.Damage) _settings.BreakoutDamageScope = value;
        else if (_kind == BreakoutKind.Healing) _settings.BreakoutHealingScope = value;
        else _settings.BreakoutPetScope = value;
        _settings.Save();
    }

    private void PaintScope()
    {
        _fight.Foreground = _fightScope ? AppTheme.AccentBrush : AppTheme.DimBrush;
        _session.Foreground = _fightScope ? AppTheme.DimBrush : AppTheme.AccentBrush;
    }
}
