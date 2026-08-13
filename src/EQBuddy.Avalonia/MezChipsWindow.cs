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
    private readonly Func<IReadOnlyList<MezState>, DateTime, List<SpawnChip>> _source;
    private readonly StackPanel _panel = new();
    private readonly List<TextBlock> _countdowns = [];
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
    {
        _settings = settings;
        _source = source ?? BuildChips;
        Title = "EQBuddy Mez Targets";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Content = _panel;

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
                $"{mez.Spell} by {mez.Caster} · landed {mez.LandedAt:h:mm:ss tt}", "💤");
        }).ToList();
    }

    /// <summary>Called from the main window's one-second refresh while mezzes exist.</summary>
    internal void RefreshChips(IReadOnlyList<MezState> mezzes, DateTime now)
    {
        _chips = _source(mezzes, now);
        var signature = string.Join("\u0001", _chips.Select(chip =>
            $"{chip.Name}|{chip.IsDue}|{chip.Icon}"));
        if (signature != _signature)
        {
            _signature = signature;
            Rebuild();
            return;
        }

        for (var i = 0; i < _chips.Count && i < _countdowns.Count; i++)
            _countdowns[i].Text = _chips[i].CountdownText;
    }

    private void Rebuild()
    {
        _panel.Children.Clear();
        _countdowns.Clear();
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

            var border = new Border
            {
                Child = row,
                Background = AppTheme.BgBrush,
                BorderBrush = chip.IsDue ? AppTheme.WarnBrush : AppTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3),
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
}
