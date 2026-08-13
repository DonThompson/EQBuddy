using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Avalonia;

/// <summary>
/// One compact chip per believed-active mez. This is deliberately a separate movable
/// stack from spawn timers: mez wake-ups are combat-urgent and are normally parked near
/// the fight, while spawn timers are ambient camp information.
/// </summary>
public sealed class MezChipsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Func<IReadOnlyList<MezState>, DateTime, List<SpawnChip>> _mezSource;
    private readonly Func<DateTime, List<SpawnChip>>? _clockSource;
    private readonly StackPanel _panel = new();
    private readonly List<TextBlock> _countdowns = [];
    private readonly List<(Grid Track, Border Fill)> _gauges = [];
    private List<SpawnChip> _chips = [];
    private string _signature = "";
    private PixelPoint _lastVisiblePosition;
    private bool _haveVisiblePosition;
    // Fallback placements must never persist (#117): where the window was placed at
    // open and whether that was the player's saved spot.
    private bool _restoredSaved;
    private bool _openedOnce;
    private bool _userMoved;
    /// <summary>Tests can't drag a headless window; this is the drag signal's test seam.</summary>
    internal void MarkUserMovedForTests() => _userMoved = true;

    public MezChipsWindow(AppSettings settings,
        Func<IReadOnlyList<MezState>, DateTime, List<SpawnChip>>? source = null)
        : this(settings, null, source, null) { }

    /// <summary>WPF's current shape: one clock-driven source for everything the
    /// fight-side stack shows — mez chips, slow chips, the Options placement preview —
    /// built by MainWindow (its FightChips), sharing this window and saved position.</summary>
    public MezChipsWindow(AppSettings settings, Func<DateTime, List<SpawnChip>> source,
        Action<double>? setChipScale = null)
        : this(settings, source, null, setChipScale) { }

    private MezChipsWindow(AppSettings settings,
        Func<DateTime, List<SpawnChip>>? clockSource,
        Func<IReadOnlyList<MezState>, DateTime, List<SpawnChip>>? mezSource,
        Action<double>? setChipScale)
    {
        _settings = settings;
        _clockSource = clockSource;
        _mezSource = mezSource ?? BuildChips;
        Title = "EQBuddy Mez Targets";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Content = ChipScale.Host(_panel);
        ChipScale.Apply(this, _settings.ChipScale);
        if (setChipScale is not null)
            ChipScale.RouteWheel(this, () => _settings.ChipScale, setChipScale);
        ChipAnchor.Attach(this, () => _settings.MezChipsGrowUp);

        Opened += (_, _) =>
        {
            _restoredSaved = ScreenGuard.OnScreen(this, _settings.MezChipsLeft,
                _settings.MezChipsTop, Width, Height);
            if (_restoredSaved)
                Position = new PixelPoint((int)_settings.MezChipsLeft, (int)_settings.MezChipsTop);
            else if (Screens.Primary is { } primary)
                Position = new PixelPoint(primary.WorkingArea.X + 40, primary.WorkingArea.Y + 120);
            _openedOnce = true;
            _lastVisiblePosition = Position;
            _haveVisiblePosition = true;
        };
        PositionChanged += (_, _) =>
        {
            // Programmatic placement is not a choice — only persist once the player
            // has actually STARTED A DRAG (#117; coordinate deltas can't tell a drag
            // from the WM's or anchor's own writes — 2026-08-13 review).
            if (!IsVisible || !_openedOnce || !_userMoved) return;
            _lastVisiblePosition = Position;
            _haveVisiblePosition = true;
            _settings.MezChipsLeft = Position.X;
            _settings.MezChipsTop = Position.Y;
        };
        Closed += (_, _) =>
        {
            var current = _haveVisiblePosition ? _lastVisiblePosition : Position;
            (_settings.MezChipsLeft, _settings.MezChipsTop) = WindowPlacement.PositionToPersist(
                _restoredSaved, _userMoved, current.X, current.Y,
                _settings.MezChipsLeft, _settings.MezChipsTop);
            _settings.Save();
        };
    }

    /// <summary>Same-named targets remain separate and are numbered in snapshot order.
    /// The log cannot identify which physical creature is which, but collapsing them
    /// would hide an active mez and make one break appear to clear both.</summary>
    internal static List<SpawnChip> BuildChips(IReadOnlyList<MezState> mezzes, DateTime now)
    {
        var totals = mezzes.GroupBy(mez => mez.Target, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return mezzes.Select(mez =>
        {
            var number = seen[mez.Target] = seen.GetValueOrDefault(mez.Target) + 1;
            var name = totals[mez.Target] > 1 ? $"{mez.Target} ({number})" : mez.Target;
            var remaining = mez.RemainingSeconds(now);
            var countdown = remaining is { } seconds
                ? $"{(int)seconds / 60}:{(int)seconds % 60:00}"
                : "?";
            return new SpawnChip("", name, countdown, remaining is <= 6,
                $"{mez.Spell} by {mez.Caster} · landed {mez.LandedAt:h:mm:ss tt}", "💤")
            {
                // Elapsed share for the gauge; the mez view draws the REMAINING side
                // (a draining bar, like a buff), so 1 - this.
                Fraction = mez.ExpiresAt is { } exp && (exp - mez.LandedAt).TotalSeconds is > 0 and var dur
                    ? Math.Clamp((now - mez.LandedAt).TotalSeconds / dur, 0, 1)
                    : null,
            };
        }).ToList();
    }

    /// <summary>Called from the main window's one-second refresh while mezzes exist.</summary>
    internal void RefreshChips(IReadOnlyList<MezState> mezzes, DateTime now) =>
        ApplyChips(_mezSource(mezzes, now));

    /// <summary>WPF's tick entry point for the clock-driven (FightChips) wiring: mez
    /// chips, slow chips, and the Options placement preview arrive already built.</summary>
    internal void RefreshChips(DateTime now)
    {
        if (_clockSource is null) return;
        ApplyChips(_clockSource(now));
    }

    private void ApplyChips(List<SpawnChip> chips)
    {
        _chips = chips;
        var signature = string.Join("\u0001", _chips.Select(chip =>
            $"{chip.Name}|{chip.IsDue}|{chip.Icon}"));
        if (signature != _signature)
        {
            _signature = signature;
            Rebuild();
            return;
        }

        for (var i = 0; i < _chips.Count && i < _countdowns.Count; i++)
        {
            _countdowns[i].Text = _chips[i].CountdownText;
            // The draining gauge ticks with the countdown, no rebuild needed.
            if (i < _gauges.Count && _gauges[i].Fill is { } fill && _chips[i].Fraction is { } frac)
                fill.Width = Math.Max(0, _gauges[i].Track.Bounds.Width * (1 - frac));
        }
    }

    private void Rebuild()
    {
        _panel.Children.Clear();
        _countdowns.Clear();
        _gauges.Clear();
        foreach (var chip in _chips)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(new TextBlock
            {
                Text = $"{chip.Icon} {chip.Name}",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = AppTheme.TextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 190,
                Margin = new Thickness(0, 0, 9, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var countdown = new TextBlock
            {
                Text = chip.CountdownText,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = chip.IsDue ? AppTheme.WarnBrush : AppTheme.AccentBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(countdown, 1);
            row.Children.Add(countdown);
            _countdowns.Add(countdown);

            // The mez gauge DRAINS (2026-08-11): remaining share, shrinking — a buff
            // bar for the sleep. Same track idiom as the spawn chips' filling gauge.
            var host = new StackPanel();
            host.Children.Add(row);
            if (chip.Fraction is { } frac0)
            {
                var track = new Grid { Height = 2.5, Margin = new Thickness(0, 3, 0, 0) };
                track.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(1.25),
                    Background = TrackBrush(),
                });
                var fill = new Border
                {
                    CornerRadius = new CornerRadius(1.25),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = 0,
                    Background = chip.IsDue ? AppTheme.WarnBrush : AppTheme.AccentBrush,
                };
                track.Children.Add(fill);
                track.SizeChanged += (_, se) => fill.Width = Math.Max(0, se.NewSize.Width * (1 - frac0));
                host.Children.Add(track);
                _gauges.Add((track, fill));
            }
            else
            {
                _gauges.Add(default);
            }
            var border = new Border
            {
                Child = host,
                Background = AppTheme.BgBrush,
                BorderBrush = chip.IsDue ? AppTheme.WarnBrush : AppTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 3, 8, 4),
                Margin = new Thickness(0, 0, 0, 3),
                Cursor = new Cursor(StandardCursorType.SizeAll),
            };
            ToolTip.SetTip(border, chip.Detail);
            border.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    _userMoved = true;   // a real drag — the one signal that persists
                    BeginMoveDrag(e);
                }
            };
            _panel.Children.Add(border);
        }
    }

    /// <summary>WPF derives this in ThemeManager (accent at 12% alpha); AppTheme has no
    /// TrackBrush yet, so it's derived here per rebuild — flagged for consolidation.
    /// Rebuilt each time so a theme switch repaints the track on the next chip change.</summary>
    private static IBrush TrackBrush()
    {
        var accent = AppTheme.AccentBrush.Color;
        return new SolidColorBrush(Color.FromArgb(0x1E, accent.R, accent.G, accent.B));
    }
}
