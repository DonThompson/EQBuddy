using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Avalonia;

/// <summary>
/// Named-mob respawn countdowns for one zone. The shared view model owns matching,
/// persistence, and edits; this class only renders rows and forwards user actions.
/// Tracking stays armed while this window is hidden. New timers pop it open and the
/// final timer expiring closes it; disabling tracking lives in Options/the main menu.
/// </summary>
public sealed class SpawnsWindow : Window
{
    private readonly MainWindow _main;
    private readonly SpawnsViewModel _vm;
    private readonly AppSettings _settings;
    private readonly TextBlock _title = new();
    private readonly ComboBox _zoneCombo = new() { FontSize = 11 };
    private readonly CheckBox _followCheck = new() { Content = "Follow", FontSize = 11 };
    private readonly StackPanel _rowsPanel = new();
    private readonly ScrollViewer _bodyScroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };
    private readonly TextBox _addName = DarkBox("", "Add a named the catalog doesn't know");
    private readonly TextBox _addDuration = DarkBox("", "Respawn time: 22 (minutes), 90s, 12h, 3d, 6:40");
    private readonly DispatcherTimer _tick;
    private readonly List<TextBlock> _countdowns = [];
    private List<SpawnRow> _rows = [];
    private string _signature = "";
    private bool _syncingZone;
    private string? _lastFollowedZone;
    private bool _restoredSaved;
    private PixelPoint _placed;

    public SpawnsWindow(MainWindow main, SpawnsViewModel vm, string? initialZone = null)
    {
        _main = main;
        _vm = vm;
        _settings = main.Settings;
        Title = "EQBuddy Spawns";
        // Rows can carry start, bell, clear, and delete actions. Give those controls a
        // permanent lane so starting a timer (which adds Clear) never reflows the inputs.
        Width = 740;
        SizeToContent = SizeToContent.Height;
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;

        _vm.RefreshZoneList();
        foreach (var zone in _vm.ZoneNames) _zoneCombo.Items.Add(zone);
        _followCheck.IsChecked = _settings.SpawnFollowZone;

        Content = BuildContent();
        WindowZoom.Attach(this, "spawns", _settings);
        SelectZone(initialZone
            ?? (_settings.SpawnFollowZone ? _vm.CurrentZoneName : null)
            ?? FirstNonEmpty(_settings.SpawnZone, _vm.ZoneNames.FirstOrDefault() ?? ""));
        _lastFollowedZone = _vm.CurrentZoneName;

        _zoneCombo.SelectionChanged += (_, _) => OnZonePicked();
        _followCheck.IsCheckedChanged += (_, _) => OnFollowChanged();
        PointerPressed += OnDrag;
        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => RefreshRows();
        _tick.Start();
        Opened += (_, _) =>
        {
            UpdateHeightLimit();
            _restoredSaved = ScreenGuard.OnScreen(this, _settings.SpawnLeft, _settings.SpawnTop, Width, Height);
            if (_restoredSaved)
                Position = new PixelPoint((int)_settings.SpawnLeft, (int)_settings.SpawnTop);
            _placed = Position;
        };
        // Follow the window between monitors; primary-only/open-time caps waste most of
        // a portrait secondary's height.
        PositionChanged += (_, _) => UpdateHeightLimit();
        Closed += (_, _) =>
        {
            _tick.Stop();
            // Never let an unmoved fallback overwrite a real saved spot (#117).
            (_settings.SpawnLeft, _settings.SpawnTop) = WindowPlacement.PositionToPersist(
                _restoredSaved, _placed.X, _placed.Y, Position.X, Position.Y,
                _settings.SpawnLeft, _settings.SpawnTop);
            _settings.Save();
        };
        RefreshRows();
    }

    private Control BuildContent()
    {
        _title.FontSize = 14;
        _title.FontWeight = FontWeight.Bold;
        _title.Foreground = AppTheme.AccentBrush;
        var close = AppTheme.IconButton("x",
            "Hide - the tracker returns on the next named kill; disable tracking in Options or the main menu");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Click += (_, _) => Close();
        var header = new Grid { Margin = new Thickness(16, 14, 16, 6) };
        header.Children.Add(_title);
        header.Children.Add(close);

        var zoneRow = new Grid { Margin = new Thickness(16, 0, 16, 8) };
        zoneRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        zoneRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        ToolTip.SetTip(_zoneCombo, "Which zone's named to show");
        zoneRow.Children.Add(_zoneCombo);
        _followCheck.Margin = new Thickness(10, 2, 0, 0);
        ToolTip.SetTip(_followCheck, "Switch to whatever zone the log says you are in");
        Grid.SetColumn(_followCheck, 1);
        zoneRow.Children.Add(_followCheck);

        _bodyScroll.Content = _rowsPanel;
        _rowsPanel.Margin = new Thickness(16, 0, 16, 4);

        var addRow = new Grid();
        addRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        addRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(76)));
        addRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        addRow.Children.Add(_addName);
        _addDuration.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(_addDuration, 1);
        addRow.Children.Add(_addDuration);
        var add = AppTheme.IconButton("+", "Add named");
        add.Margin = new Thickness(6, 0, 0, 0);
        add.Click += (_, _) =>
        {
            if (_vm.AddCustom(SelectedZone, _addName.Text ?? "", _addDuration.Text ?? ""))
            {
                _addName.Text = "";
                _addDuration.Text = "";
                Kick();
            }
        };
        Grid.SetColumn(add, 2);
        addRow.Children.Add(add);

        var footer = new StackPanel { Margin = new Thickness(16, 4, 16, 14) };
        footer.Children.Add(AppTheme.DimText(
            "Kill a named (or its placeholder) and its countdown starts from the log. ▶ starts one by hand. Community times are a starting point: edit any discrepancy you see in game; your value wins and survives updates.",
            new Thickness(0, 0, 0, 6)));
        footer.Children.Add(addRow);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        layout.Children.Add(header);
        Grid.SetRow(zoneRow, 1);
        layout.Children.Add(zoneRow);
        Grid.SetRow(_bodyScroll, 2);
        layout.Children.Add(_bodyScroll);
        Grid.SetRow(footer, 3);
        layout.Children.Add(footer);
        return new Border
        {
            Background = AppTheme.BgBrush,
            BorderBrush = AppTheme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = layout,
        };
    }

    private static string FirstNonEmpty(string first, string second) => first.Length > 0 ? first : second;
    private string SelectedZone => _zoneCombo.SelectedItem as string ?? "";

    private void SelectZone(string zone)
    {
        _syncingZone = true;
        _zoneCombo.SelectedItem = _vm.ZoneNames.FirstOrDefault(z =>
            string.Equals(z, zone, StringComparison.OrdinalIgnoreCase));
        _syncingZone = false;
    }

    internal void RefreshRows()
    {
        // Follow zone CHANGES, not every tick. This lets the user browse another zone's
        // list mid-camp without the dropdown immediately snapping back.
        if (_settings.SpawnFollowZone && _vm.CurrentZoneName is { } current
            && current != _lastFollowedZone)
        {
            _lastFollowedZone = current;
            if (current != SelectedZone) SelectZone(current);
        }
        var zone = SelectedZone;
        _title.Text = zone.Length > 0 ? $"🕒 Spawns - {zone}" : "🕒 Spawns";
        if (zone.Length == 0) return;
        _rows = _vm.RowsFor(zone, DateTime.Now);
        var signature = zone + "\u0001" + string.Join("\u0001", _rows.Select(r =>
            $"{r.DisplayName}|{r.HasActiveTimer}|{r.IsDue}|{r.DurationText}|{r.Alert}|{r.SoundName}|{r.IsCustom}"));
        if (signature != _signature)
        {
            if (_rowsPanel.GetVisualDescendants().OfType<TextBox>().Any(b => b.IsFocused)) return;
            _signature = signature;
            Rebuild();
        }
        else
            for (var i = 0; i < _rows.Count && i < _countdowns.Count; i++)
                _countdowns[i].Text = _rows[i].CountdownText;
    }

    private void Rebuild()
    {
        _rowsPanel.Children.Clear();
        _countdowns.Clear();
        if (_rows.Count == 0)
        {
            _rowsPanel.Children.Add(AppTheme.DimText("No named catalogued for this zone yet - add one below."));
            return;
        }
        foreach (var row in _rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(72)));   // countdown
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(70)));   // duration
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(60)));   // died ago
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(232)));  // actions + sound
            var name = new TextBlock
            {
                Text = row.DisplayName,
                FontSize = 11,
                Foreground = AppTheme.TextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (row.Detail.Length > 0) ToolTip.SetTip(name, row.Detail);
            grid.Children.Add(name);
            var countdown = new TextBlock
            {
                Text = row.CountdownText,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = row.IsDue ? AppTheme.WarnBrush : AppTheme.AccentBrush,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            Grid.SetColumn(countdown, 1);
            grid.Children.Add(countdown);
            _countdowns.Add(countdown);
            var duration = DarkBox(row.DurationText, "Respawn time: 22 (minutes), 90s, 12h, 3d, 6:40");
            duration.Margin = new Thickness(0, 0, 6, 0);
            duration.LostFocus += (_, _) =>
            {
                if ((duration.Text ?? "").Trim() == row.DurationText) return;
                _vm.SetDuration(row.Zone, row.Name, duration.Text ?? "");
                Kick();
            };
            Grid.SetColumn(duration, 2);
            grid.Children.Add(duration);
            var ago = DarkBox("", "Died how long ago? 5m, 90s; empty means now");
            ago.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(ago, 3);
            grid.Children.Add(ago);
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            buttons.Children.Add(RowButton("▶", "Start countdown", () =>
            {
                _vm.StartNow(row.Zone, row.Name, ago.Text ?? "");
                Kick();
            }));
            var bell = RowButton(row.Alert ? "🔔" : "🔕", "Toggle due alert", () =>
            {
                _vm.ToggleAlert(row.Zone, row.Name);
                Kick();
            });
            bell.Opacity = row.Alert ? 1 : 0.45;
            buttons.Children.Add(bell);
            buttons.Children.Add(BuildSoundPicker(row));
            if (row.HasActiveTimer)
                buttons.Children.Add(RowButton("x", "Forget countdown", () =>
                {
                    _vm.ClearTimer(row.Zone, row.Name);
                    Kick();
                }));
            if (row.IsCustom)
                buttons.Children.Add(RowButton("🗑", "Remove custom named", () =>
                {
                    _vm.RemoveCustom(row.Zone, row.Name);
                    Kick();
                }));
            Grid.SetColumn(buttons, 4);
            grid.Children.Add(buttons);
            _rowsPanel.Children.Add(grid);
        }
    }

    private static TextBox DarkBox(string text, string tip)
    {
        var box = new TextBox
        {
            Text = text,
            FontSize = 11,
            Padding = new Thickness(3, 1),
            Background = AppTheme.ComboBoxBrush,
            Foreground = AppTheme.TextBrush,
            BorderBrush = AppTheme.BorderBrush,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(box, tip);
        return box;
    }

    private ComboBox BuildSoundPicker(SpawnRow row)
    {
        var picker = new ComboBox
        {
            FontSize = 10,
            Width = 78,
            Margin = new Thickness(4, 0, 0, 0),
        };
        foreach (var choice in (string[])["Default", "Off", .. AlertSoundCatalog.Names, "Custom…"])
            picker.Items.Add(choice);
        var custom = row.SoundName.Length > 0
            && !string.Equals(row.SoundName, "Off", StringComparison.OrdinalIgnoreCase)
            && !AlertSoundCatalog.Names.Contains(row.SoundName, StringComparer.OrdinalIgnoreCase);
        picker.SelectedItem = row.SoundName.Length == 0 ? "Default"
            : custom ? "Custom…"
            : picker.Items.Cast<string>().First(choice =>
                string.Equals(choice, row.SoundName, StringComparison.OrdinalIgnoreCase));
        ToolTip.SetTip(picker, custom
            ? $"Custom: {row.SoundName}"
            : "Sound for this named; Default is Alarm, and choosing a concrete sound enables its bell");

        var ready = true;
        picker.SelectionChanged += async (_, _) =>
        {
            if (!ready || picker.SelectedItem is not string choice) return;
            switch (choice)
            {
                case "Default":
                    _vm.SetSound(row.Zone, row.Name, "");
                    break;
                case "Off":
                    _vm.SetSound(row.Zone, row.Name, "Off");
                    break;
                case "Custom…":
                    var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = $"Choose a sound for \"{row.Name}\"",
                        AllowMultiple = false,
                        FileTypeFilter =
                        [
                            new FilePickerFileType("Sound files")
                            {
                                Patterns = ["*.wav", "*.mp3", "*.ogg"],
                            },
                        ],
                    });
                    if (picked.FirstOrDefault()?.TryGetLocalPath() is not { } path)
                    {
                        ready = false;
                        picker.SelectedItem = row.SoundName.Length == 0 ? "Default"
                            : custom ? "Custom…" : row.SoundName;
                        ready = true;
                        return;
                    }
                    _vm.SetSound(row.Zone, row.Name, path);
                    _main.PlayAlertSound(path);
                    break;
                default:
                    _vm.SetSound(row.Zone, row.Name, choice);
                    _main.PlayAlertSound(choice);
                    break;
            }
            Kick();
        };
        return picker;
    }

    private static Button RowButton(string text, string tip, Action click)
    {
        var button = AppTheme.IconButton(text, tip);
        button.FontSize = 11;
        button.Margin = new Thickness(4, 0, 0, 0);
        button.Click += (_, _) => click();
        return button;
    }

    private void Kick()
    {
        _signature = "";
        RefreshRows();
    }

    private void OnZonePicked()
    {
        if (_syncingZone || SelectedZone.Length == 0) return;
        _settings.SpawnZone = SelectedZone;
        _settings.Save();
        Kick();
    }

    private void OnFollowChanged()
    {
        if (_syncingZone) return;
        _settings.SpawnFollowZone = _followCheck.IsChecked == true;
        _settings.Save();
        RefreshRows();
    }

    private void UpdateHeightLimit()
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen is null) return;
        var available = Math.Max(260, screen.WorkingArea.Height / screen.Scaling - 40);
        MaxHeight = available;
        _bodyScroll.MaxHeight = Math.Max(120, available - 190);
    }

    private void OnDrag(object? sender, PointerPressedEventArgs e)
    {
        // ComboBox clicks originate from template children (Border, ContentPresenter,
        // arrow Path), not from the ComboBox itself. Checking only e.Source made those
        // presses begin a window drag and immediately dismiss the popup.
        if (e.Source is Visual source && source.GetSelfAndVisualAncestors().Any(IsInteractiveControl))
            return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private static bool IsInteractiveControl(Visual visual) =>
        visual is TextBox or Button or ComboBox or CheckBox or ScrollBar;
}
