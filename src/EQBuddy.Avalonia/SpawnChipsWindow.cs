using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Avalonia;

/// <summary>
/// Compact ambient face of spawn tracking. Every active timer on the current server is
/// one chip; the stack moves as a unit and exists only while timers do. The full spawn
/// browser remains available independently through a double-click or the main menu. A
/// due chip remains for one minute, then Core expires it if it was not clicked sooner.
/// </summary>
public sealed class SpawnChipsWindow : Window
{
    private readonly MainWindow _main;
    private readonly SpawnsViewModel _vm;
    private readonly AppSettings _settings;
    private readonly StackPanel _panel = new();
    private readonly List<TextBlock> _countdowns = [];
    private List<SpawnChip> _chips = [];
    private string _signature = "";
    // Fallback placements must never persist (#117): where the window was placed at
    // open and whether that was the player's saved spot.
    private PixelPoint _placed;
    private bool _restoredSaved;
    private bool _openedOnce;
    private PixelPoint _lastVisiblePosition;
    private bool _haveVisiblePosition;

    public SpawnChipsWindow(MainWindow main, SpawnsViewModel vm)
    {
        _main = main;
        _vm = vm;
        _settings = main.Settings;
        Title = "EQBuddy Spawn Timers";
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
            _restoredSaved = ScreenGuard.OnScreen(this, _settings.SpawnChipsLeft,
                _settings.SpawnChipsTop, Width, Height);
            if (_restoredSaved)
                Position = new PixelPoint((int)_settings.SpawnChipsLeft, (int)_settings.SpawnChipsTop);
            else if (Screens.Primary is { } primary)
                Position = new PixelPoint(primary.WorkingArea.X + 40, primary.WorkingArea.Y + 40);
            _placed = Position;
            _openedOnce = true;
            _lastVisiblePosition = Position;
            _haveVisiblePosition = true;
        };
        // Position can be reset by the native backend while a window is closing. Capture
        // moves only while it is visibly on screen, then persist that stable snapshot.
        PositionChanged += (_, _) =>
        {
            // Programmatic placement is not a choice — only persist once the player
            // has actually dragged the stack somewhere (#117).
            if (!IsVisible || !_openedOnce || Position == _placed) return;
            _lastVisiblePosition = Position;
            _haveVisiblePosition = true;
            // Keep the live settings object current too, so a newly-created stack in the
            // same session restores correctly even before Closed has flushed the file.
            _settings.SpawnChipsLeft = Position.X;
            _settings.SpawnChipsTop = Position.Y;
        };
        Closed += (_, _) =>
        {
            var current = _haveVisiblePosition ? _lastVisiblePosition : Position;
            (_settings.SpawnChipsLeft, _settings.SpawnChipsTop) = WindowPlacement.PositionToPersist(
                _restoredSaved, _placed.X, _placed.Y, current.X, current.Y,
                _settings.SpawnChipsLeft, _settings.SpawnChipsTop);
            _settings.Save();
        };
    }

    internal void RefreshChips(DateTime now)
    {
        _chips = _vm.Chips(now);
        var signature = string.Join("\u0001", _chips.Select(chip =>
            $"{chip.Zone}|{chip.Name}|{chip.IsDue}|{chip.Icon}"));
        if (signature != _signature)
        {
            _signature = signature;
            Rebuild();
            return;
        }

        for (var i = 0; i < _chips.Count && i < _countdowns.Count; i++)
            _countdowns[i].Text = _chips[i].IsDue ? "DUE" : _chips[i].CountdownText;
    }

    private void Rebuild()
    {
        _panel.Children.Clear();
        _countdowns.Clear();
        foreach (var chip in _chips)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var name = new TextBlock
            {
                Text = $"{chip.Icon} {chip.Name}",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = AppTheme.TextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 190,
                Margin = new Thickness(0, 0, 9, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(name);
            var countdown = new TextBlock
            {
                Text = chip.IsDue ? "DUE" : chip.CountdownText,
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
                Tag = chip,
                Background = AppTheme.BgBrush,
                BorderBrush = chip.IsDue ? AppTheme.WarnBrush : AppTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3),
                Margin = new Thickness(0, 0, 0, 3),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(border, chip.Detail + "\nRight-click: dismiss this timer");
            border.PointerPressed += OnChipPressed;
            _panel.Children.Add(border);
        }
    }

    private void OnChipPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: SpawnChip chip }) return;
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            DismissChip(chip);
            e.Handled = true;
            return;
        }
        if (e.ClickCount == 2)
        {
            _main.ShowSpawnsWindow(chip.Zone);
            e.Handled = true;
            return;
        }
        if (chip.IsDue)
        {
            _vm.ClearTimer(chip.Zone, chip.Name);
            _signature = "\uFFFF";
            RefreshChips(DateTime.Now);
            e.Handled = true;
            return;
        }
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    internal void DismissChip(SpawnChip chip)
    {
        if (chip.Zone.Length == 0) return;
        _vm.ClearTimer(chip.Zone, chip.Name);
        // A sentinel is required when dismissing the last chip: its new signature is
        // the empty string, so resetting to "" would incorrectly skip the rebuild.
        _signature = "\uFFFF";
        RefreshChips(DateTime.Now);
    }
}
