using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy;

/// <summary>
/// The Spawns window (SPAWN-004): named-mob respawn countdowns for one zone at a time,
/// fed by <see cref="SpawnsViewModel"/>. While "Track spawns" is armed the window stays
/// HIDDEN until a countdown exists — it pops when a named (or placeholder) death starts
/// one, including timers recovered from the log at startup — and MainWindow closes it
/// again when the last timer runs out. ✕ only hides it; the next kill brings it back.
/// David's call (2026-08-02): a tracker parked on screen all session is noise, a tracker
/// that appears because something died is information. Alerts fire from MainWindow's
/// shared tick, not here, so they don't depend on this window's lifetime.
/// </summary>
public partial class SpawnsWindow : Window
{
    private readonly MainWindow _main;
    private readonly SpawnsViewModel _vm;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _tick;

    // Rebuilds are keyed on a signature of everything except the countdown text, so a
    // ticking clock updates labels in place and never yanks focus out of an edit box.
    private string _signature = "";
    private readonly List<TextBlock> _countdowns = [];
    private List<SpawnRow> _rows = [];
    private bool _syncingZone;

    /// <summary><paramref name="initialZone"/>: the zone whose kill popped the window,
    /// so it opens showing the timer that summoned it.</summary>
    public SpawnsWindow(MainWindow main, SpawnsViewModel vm, string? initialZone = null)
    {
        InitializeComponent();
        _main = main;
        _vm = vm;
        _settings = main.Settings;

        MaxHeight = SystemParameters.WorkArea.Height - 40;
        BodyScroll.MaxHeight = SystemParameters.WorkArea.Height - 220;

        if (!double.IsNaN(_settings.SpawnLeft)) { Left = _settings.SpawnLeft; Top = _settings.SpawnTop; }
        else { Left = SystemParameters.WorkArea.Left + 40; Top = 80; }

        _vm.RefreshZoneList();
        foreach (var z in _vm.ZoneNames) ZoneCombo.Items.Add(z);
        FollowCheck.IsChecked = _settings.SpawnFollowZone;

        foreach (var s in (string[])["Off", .. AlertSoundCatalog.Names]) SoundCombo.Items.Add(s);
        _syncingSound = true;
        SoundCombo.SelectedIndex = Math.Max(0,
            SoundCombo.Items.IndexOf(AlertSoundCatalog.Normalize(_settings.SpawnSound)));
        _syncingSound = false;

        SelectZone(initialZone
            ?? (_settings.SpawnFollowZone ? _vm.CurrentZoneName : null)
            ?? FirstNonEmpty(_settings.SpawnZone, _vm.ZoneNames.FirstOrDefault() ?? ""));
        _lastFollowedZone = _vm.CurrentZoneName;

        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => RefreshRows();
        _tick.Start();
        RefreshRows();

        Closed += (_, _) =>
        {
            _tick.Stop();
            _settings.SpawnLeft = Left;
            _settings.SpawnTop = Top;
            _settings.Save();
        };
    }

    private static string FirstNonEmpty(string a, string b) => a.Length > 0 ? a : b;

    private string SelectedZone => ZoneCombo.SelectedItem as string ?? "";

    private void SelectZone(string zone)
    {
        _syncingZone = true;
        ZoneCombo.SelectedItem = _vm.ZoneNames.FirstOrDefault(z =>
            string.Equals(z, zone, StringComparison.OrdinalIgnoreCase)) ?? ZoneCombo.SelectedItem;
        _syncingZone = false;
    }

    /// <summary>The zone Follow last snapped to. Following reacts to zone CHANGES, not to
    /// every tick — so browsing another zone's list mid-camp survives until you actually
    /// zone, instead of the dropdown yanking itself back every second.</summary>
    private string? _lastFollowedZone;

    private void RefreshRows()
    {
        // Follow the player: the log's zone lines drive the dropdown while Follow is on.
        if (_settings.SpawnFollowZone && _vm.CurrentZoneName is { } here && here != _lastFollowedZone)
        {
            _lastFollowedZone = here;
            if (here != SelectedZone) SelectZone(here);
        }

        var zone = SelectedZone;
        TitleText.Text = zone.Length > 0 ? $"🕒 Spawns — {zone}" : "🕒 Spawns";
        if (zone.Length == 0) return;

        var now = DateTime.Now;
        _rows = _vm.RowsFor(zone, now);
        var signature = zone + "" + string.Join("",
            _rows.Select(r => $"{r.DisplayName}|{r.HasActiveTimer}|{r.IsDue}|{r.DurationText}|{r.Alert}|{r.IsCustom}"));
        if (signature != _signature)
        {
            // Never rebuild under someone's cursor — committing the edit refreshes anyway.
            if (RowsPanel.IsKeyboardFocusWithin) return;
            _signature = signature;
            Rebuild();
        }
        else
        {
            for (var i = 0; i < _rows.Count && i < _countdowns.Count; i++)
                _countdowns[i].Text = _rows[i].CountdownText;
        }
    }

    private void Rebuild()
    {
        RowsPanel.Children.Clear();
        _countdowns.Clear();

        if (_rows.Count == 0)
        {
            RowsPanel.Children.Add(new TextBlock
            {
                Text = "No named catalogued for this zone yet — add one below.",
                FontSize = 11, Opacity = 0.65, Margin = new Thickness(0, 4, 0, 4),
            });
            return;
        }

        foreach (var row in _rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });   // countdown
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });   // duration
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });   // ago
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });      // buttons

            var name = new TextBlock
            {
                Text = row.DisplayName, FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = row.Detail.Length > 0 ? row.Detail : null,
            };
            Grid.SetColumn(name, 0);
            grid.Children.Add(name);

            var countdown = new TextBlock
            {
                Text = row.CountdownText, FontSize = 11, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 6, 0),
            };
            countdown.SetResourceReference(TextBlock.ForegroundProperty,
                row.IsDue ? "WarnBrush" : "AccentBrush");
            Grid.SetColumn(countdown, 1);
            grid.Children.Add(countdown);
            _countdowns.Add(countdown);

            var duration = DarkBox(row.DurationText,
                "Respawn time: 22 (minutes), 90s, 12h, 3d, 6:40 — edits persist as yours");
            duration.Tag = row.Name;
            duration.LostFocus += (_, _) => CommitDuration(duration, row);
            duration.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitDuration(duration, row); };
            Grid.SetColumn(duration, 2);
            grid.Children.Add(duration);

            var ago = DarkBox("", "Died how long ago? (5m, 90s) Empty = just now");
            ago.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetColumn(ago, 3);
            grid.Children.Add(ago);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(RowButton("▶", "Start the countdown from a kill you saw yourself",
                () => { _vm.StartNow(row.Zone, row.Name, ago.Text); Kick(); }));
            var bell = RowButton(row.Alert ? "🔔" : "🔕",
                "Alert (banner + sound) when this one comes due",
                () => { _vm.ToggleAlert(row.Zone, row.Name); Kick(); });
            bell.Opacity = row.Alert ? 1.0 : 0.45;
            buttons.Children.Add(bell);
            if (row.HasActiveTimer)
                buttons.Children.Add(RowButton("✕", "Forget this countdown",
                    () => { _vm.ClearTimer(row.Zone, row.Name); Kick(); }));
            if (row.IsCustom)
                buttons.Children.Add(RowButton("🗑", "Remove this named (you added it)",
                    () => { _vm.RemoveCustom(row.Zone, row.Name); Kick(); }));
            Grid.SetColumn(buttons, 4);
            grid.Children.Add(buttons);

            RowsPanel.Children.Add(grid);
        }
    }

    private void CommitDuration(TextBox box, SpawnRow row)
    {
        var before = row.DurationText;
        if (box.Text.Trim() == before) return;
        _vm.SetDuration(row.Zone, row.Name, box.Text);
        Kick();
    }

    /// <summary>Force the next tick to rebuild even while focus sits in the panel.</summary>
    private void Kick()
    {
        _signature = "";
        Keyboard.ClearFocus();
        RefreshRows();
    }

    private static TextBox DarkBox(string text, string tooltip)
    {
        var box = new TextBox
        {
            Text = text, FontSize = 11, ToolTip = tooltip,
            Padding = new Thickness(3, 1, 3, 1),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        // SetResourceReference so an in-place theme switch repaints rebuilt rows too.
        box.SetResourceReference(Control.BackgroundProperty, "ComboBoxBrush");
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        return box;
    }

    private Button RowButton(string glyph, string tooltip, Action onClick)
    {
        var b = new Button
        {
            Content = glyph, ToolTip = tooltip, FontSize = 11,
            Style = (Style)FindResource("IconButton"),
            Margin = new Thickness(4, 0, 0, 0),
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    // ---- chrome ----

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is not TextBox) DragMove();
    }

    // ✕ hides the window; tracking stays armed and the next kill re-opens it. Turning
    // the feature off lives in the menu and Options, deliberately elsewhere.
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnZonePicked(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingZone) return;
        // A null/empty selection can arrive from WPF teardown paths, not from the user —
        // 1.20.0 let one of those persist state and silently killed zone-following.
        if (SelectedZone.Length == 0) return;
        _settings.SpawnZone = SelectedZone;
        _settings.Save();
        _signature = "";
        RefreshRows();
    }

    private void OnFollowChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingZone) return;
        _settings.SpawnFollowZone = FollowCheck.IsChecked == true;
        _settings.Save();
        RefreshRows();
    }

    private void OnAddCustom(object sender, RoutedEventArgs e)
    {
        if (_vm.AddCustom(SelectedZone, AddNameBox.Text, AddDurationBox.Text))
        {
            AddNameBox.Text = "";
            AddDurationBox.Text = "";
            Kick();
        }
    }

    private bool _syncingSound;
    private void OnSoundPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSound || SoundCombo.SelectedItem is not string choice) return;
        _settings.SpawnSound = choice;
        _settings.Save();
        if (!string.Equals(choice, "Off", StringComparison.OrdinalIgnoreCase))
            _main.PlayAlertSound(choice);
    }
}
