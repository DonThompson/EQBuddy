using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EQBuddy.Core;

namespace EQBuddy;

/// <summary>
/// The zone map (competitive gap #1, 2026-08-10): classic-format map files —
/// Brewall packs, the game's own /map output — drawn with your last /loc as the
/// player marker. Log-only and honest about it: the marker moves when YOU type
/// /loc, and the window says how old the position is rather than pretending to
/// live-track. Follows the zone the log last saw; pick any map from the dropdown
/// to plan ahead. Wheel zooms around the cursor, drag pans, double-click refits.
/// </summary>
public sealed class MapWindow : Window
{
    /// <summary>Brewall's EverQuest Maps — the canonical home (brewall.com is the
    /// old domain). Linked, never bundled: the pack states no redistribution terms,
    /// so we send players to the source and the credit stays with the cartographer.</summary>
    internal const string MapPackUrl = "https://www.eqmaps.info/eq-map-files/";

    private readonly MainWindow _main;
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly Canvas _mapLayer = new();
    private readonly System.Windows.Shapes.Ellipse _marker = new()
    {
        Width = 10, Height = 10, StrokeThickness = 2.5, Visibility = Visibility.Collapsed,
    };
    private readonly TextBlock _status = new() { FontSize = 11, Margin = new Thickness(8, 4, 8, 6), TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _zonePick = new() { FontSize = 11, MinWidth = 170 };
    private readonly MatrixTransform _view = new();
    private ZoneMap? _map;
    private string _shownFile = "";
    private string _followedZone = "";
    private bool _userPicked;
    private Point _dragStart;
    private bool _dragging;
    private readonly StackPanel _namedPanel = new() { Margin = new Thickness(8, 4, 8, 4) };
    private readonly List<System.Windows.Shapes.Path> _trailPaths = [];
    private readonly List<(FrameworkElement El, double X, double Y, double Dx, double Dy)> _campPins = [];
    private (int Count, long Bucket) _trailStamp = (-1, 0);

    // ---- Spawn-point circles (David's map brief, 2026-08-13) ----------------
    // Every archived spawn point in the shown zone, drawn as a circle: named
    // points wear the theme ACCENT, ordinary points sit dim — visibly of the UI,
    // never of the map pack. Theme-keyed via SetResourceReference throughout
    // (David: "colors should align with our UI Theme", not hard-coded). Hover
    // answers "what lives here": every mob seen at the point with kill counts,
    // the last kill, and the projected respawn. Circles PULSE when a respawn is
    // imminent — within PulseWindowSeconds of the named timer or the ordinary
    // projection.
    private readonly List<(FrameworkElement El, double X, double Y, double Dx, double Dy)> _spawnCircles = [];
    private readonly List<SpawnCircle> _circleMeta = [];
    private (string Zone, int Points, int Kills, int TimerHash) _circleStamp = ("\0", -1, -1, 0);

    /// <summary>"Imminent" = due within this many seconds (David, 2026-08-13).</summary>
    internal const double PulseWindowSeconds = 10;
    /// <summary>Keep pulsing this long past due — the pop is happening about now —
    /// then settle; the named panel's DUE state carries on from there.</summary>
    internal const double PulseLingerSeconds = 30;

    private sealed class SpawnCircle
    {
        public required System.Windows.Shapes.Ellipse Ring;
        public required System.Windows.Shapes.Ellipse Halo;
        public required SpawnPointLedger.SpawnPoint Point;
        public required bool Named;
        public bool Pulsing;
    }

    /// <summary>How often the trail re-renders just because time passed. Every
    /// shared tick: with a one-minute horizon the fade must read as continuous,
    /// and a rebuild is ~80 frozen brushes — nothing.</summary>
    private static readonly TimeSpan FadeTick = TimeSpan.FromSeconds(1);

    public MapWindow(MainWindow main)
    {
        _main = main;
        Title = "Zone map";
        Width = 560;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = main;
        SetResourceReference(BackgroundProperty, "BgBrush");

        _mapLayer.RenderTransform = _view;
        _canvas.Children.Add(_mapLayer);
        _canvas.Children.Add(_marker);
        _canvas.Background = Brushes.Transparent;   // hit-test everywhere for pan/zoom
        _marker.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "WarnBrush");
        _marker.Fill = new SolidColorBrush(Color.FromArgb(120, 255, 200, 60));
        _status.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");

        var bar = new DockPanel { Margin = new Thickness(8, 6, 8, 0) };
        var zoneLabel = new TextBlock { Text = "Map: ", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        zoneLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var follow = Theming.Button("Follow me");
        follow.Margin = new Thickness(6, 0, 0, 0);
        follow.ToolTip = "Snap back to the zone you're actually in, and keep following as you zone.\n" +
            "Picking a map from the dropdown pauses following to let you plan ahead —\n" +
            "your marker, trail, and camp pins hide until you're back on your own map.";
        follow.Click += (_, _) => { _userPicked = false; MaybeRefresh(force: true); };
        var chooseFolder = Theming.Button("Maps folder…");
        chooseFolder.Margin = new Thickness(6, 0, 0, 0);
        chooseFolder.Click += (_, _) => ChooseFolder();
        // Map packs aren't ours to bundle (Brewall's states no redistribution
        // terms, so none are granted) — but the download is one click away and
        // the credit stays where it belongs. Same posture we ask of others.
        var getMaps = Theming.Button("Get maps…");
        getMaps.Margin = new Thickness(6, 0, 0, 0);
        getMaps.ToolTip = "Opens Brewall's EverQuest Maps (eqmaps.info) in your browser.\n" +
            "Download the map pack zip and extract the .txt files into the game's \"maps\"\n" +
            "folder (next to Logs) — EQBuddy picks them up from there. Maps by Brewall.";
        getMaps.Click += (_, _) => System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(MapPackUrl) { UseShellExecute = true });
        DockPanel.SetDock(zoneLabel, Dock.Left);
        DockPanel.SetDock(chooseFolder, Dock.Right);
        DockPanel.SetDock(getMaps, Dock.Right);
        DockPanel.SetDock(follow, Dock.Right);
        bar.Children.Add(zoneLabel);
        bar.Children.Add(chooseFolder);
        bar.Children.Add(getMaps);
        bar.Children.Add(follow);
        bar.Children.Add(_zonePick);
        _zonePick.SelectionChanged += (_, _) =>
        {
            if (_zonePick.SelectedItem is string stem && MapFolder is { } dir)
            {
                var file = Path.Combine(dir, stem + ".txt");
                if (file != _shownFile) { _userPicked = true; ShowFile(file); }
            }
        };

        // The named side panel — "ShowEQ Lite," minus everything bannable (David,
        // 2026-08-10): current-zone named with their respawn countdowns, camps
        // pinned from YOUR /loc at kill time or the wiki's location field. All of
        // it from the log and public pages; nothing reads or touches the game.
        var scroll = new ScrollViewer
        {
            Content = _namedPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        // Zone knowledge sharing lives with the map because the map is where the
        // knowledge shows (the spawn circles). Docked OUTSIDE the rebuilt panel so
        // the button never churns.
        var share = Theming.Button("Share zone knowledge…");
        share.FontSize = 10.5;
        share.Margin = new Thickness(8, 4, 8, 8);
        share.ToolTip = "Export this zone's spawn points and learned timers as one paste-safe string,\n" +
            "import a friend's (with a full preview first — big timer deviations arrive flagged),\n" +
            "or submit yours to EQBuddy for everyone. Nothing is sent unless you click it.";
        share.Click += (_, _) =>
        {
            var timerZone = _main.SpawnTimers.CurrentZone?.Zone
                ?? SpawnCatalog.StripTierVariant(_main.CurrentZoneName);
            if (timerZone.Length == 0)
            {
                _status.Text = "Share zone knowledge needs to know the zone — it unlocks once the log sees you zone in.";
                return;
            }
            new ZoneShareWindow(_main, timerZone) { Owner = this }.ShowDialog();
        };
        var side = new DockPanel { Width = 190 };
        DockPanel.SetDock(share, Dock.Bottom);
        side.Children.Add(share);
        side.Children.Add(scroll);
        side.SetResourceReference(BackgroundProperty, "PanelBrush");

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        DockPanel.SetDock(side, Dock.Right);
        root.Children.Add(bar);
        root.Children.Add(_status);
        root.Children.Add(side);
        root.Children.Add(_canvas);
        Content = root;

        _canvas.MouseWheel += OnWheel;
        _canvas.MouseLeftButtonDown += (_, e) => { _dragging = true; _dragStart = e.GetPosition(_canvas); _canvas.CaptureMouse(); };
        _canvas.MouseLeftButtonUp += (_, _) => { _dragging = false; _canvas.ReleaseMouseCapture(); };
        _canvas.MouseMove += OnDrag;
        _canvas.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) FitToView(); };
        SizeChanged += (_, _) => { if (!_dragging) FitToView(); };

        PopulateZoneList();
        MaybeRefresh(force: true);
    }

    private string? MapFolder =>
        _main.Settings.MapFolder is { Length: > 0 } custom && Directory.Exists(custom)
            ? custom
            : ZoneMapFiles.DefaultFolder(_main.Settings.LogFolder);

    private void PopulateZoneList()
    {
        _zonePick.Items.Clear();
        if (MapFolder is not { } folder) return;
        foreach (var f in Directory.EnumerateFiles(folder, "*.txt")
                     .Select(Path.GetFileNameWithoutExtension)
                     .Where(stem => stem is { Length: > 0 } && !stem.Contains('_'))
                     .OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            _zonePick.Items.Add(f!);
    }

    /// <summary>Cheap follow tick from RefreshUi: reload only when the zone (or the
    /// marker) actually moved.</summary>
    public void MaybeRefresh(bool force = false)
    {
        var zone = _main.CurrentZoneName;
        if (MapFolder is not { } folder)
        {
            _status.Text = "No maps folder found. EQBuddy looks for the game's own \"maps\" folder " +
                "beside Logs — click \"Get maps…\" for Brewall's pack (unzip it there), or point " +
                "me at an existing folder with Maps folder…";
            return;
        }
        if (!_userPicked && zone.Length > 0 && zone != _followedZone)
        {
            _followedZone = zone;
            var file = ZoneMapFiles.Resolve(folder, zone);
            if (file is not null) ShowFile(file);
            else
            {
                _mapLayer.Children.Clear();
                _map = null;
                _shownFile = "";
                _status.Text = $"No map file matched \"{zone}\" in {folder} — pick one from the dropdown " +
                    "(and tell the discussions board which file it should have been).";
            }
        }
        else if (force && _shownFile.Length > 0)
        {
            ShowFile(_shownFile);
        }
        UpdateMarker();
        UpdateTrail();
        UpdateSpawnCircles();   // before the pins so pins stay on top
        UpdateNamedPanel();
    }

    /// <summary>Rebuild the circles only when the archive actually changed (zone,
    /// point count, or kill total) — a rebuild every tick would restart the pulse
    /// animations mid-beat. The pulse check itself runs every tick regardless,
    /// because imminence is a property of the clock, not the archive.</summary>
    private void UpdateSpawnCircles()
    {
        var now = DateTime.Now;
        var zone = _main.CurrentZoneName;
        var timerZone = _main.SpawnTimers.CurrentZone?.Zone
            ?? SpawnCatalog.StripTierVariant(zone);
        var showing = _map is not null && !_userPicked && timerZone.Length > 0;
        var archive = showing ? _main.SpawnPoints.Snapshot(timerZone) : null;
        // Running timers matter to the rebuild too: a named circle carries its own
        // name label ONLY while no timer pin is labeling that mob (David caught
        // Trainer/Taskmaster going nameless — named points must read as named even
        // with no countdown running).
        var timerNames = archive is null ? []
            : _main.SpawnTimers.Snapshot(now)
                .Where(t => string.Equals(t.Zone, timerZone, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        var timerHash = timerNames.Aggregate(17,
            (h, n) => h * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(n));
        var stamp = archive is null
            ? ("", 0, 0, 0)
            : (timerZone, archive.Points.Count, archive.Points.Sum(p => p.TotalKills()), timerHash);
        if (stamp != _circleStamp)
        {
            _circleStamp = stamp;
            foreach (var (el, _, _, _, _) in _spawnCircles) _canvas.Children.Remove(el);
            _spawnCircles.Clear();
            _circleMeta.Clear();
            if (archive is not null)
                foreach (var p in archive.Points)
                    BuildCircle(timerZone, p, timerNames);
            PlaceSpawnCircles();
        }
        if (_circleMeta.Count > 0) UpdatePulse(now, timerZone);
    }

    private void BuildCircle(string zone, SpawnPointLedger.SpawnPoint p, List<string> timerNames)
    {
        var namedName = _main.SpawnPoints.NamedPointName(zone, p);
        var named = namedName is not null;
        var (mx, my) = ZoneMap.FromLoc(p.LocY, p.LocX);
        var d = named ? 13.0 : 9.0;
        var halo = new System.Windows.Shapes.Ellipse
        {
            Width = d + 8, Height = d + 8,
            Opacity = named ? 0.30 : 0.16, IsHitTestVisible = false,
        };
        halo.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
            named ? "AccentBrush" : "DimBrush");
        var ring = new System.Windows.Shapes.Ellipse
        {
            Width = d, Height = d,
            StrokeThickness = named ? 2.2 : 1.5,
            Fill = Brushes.Transparent,   // hit-test the interior for the hover
            ToolTip = "",
        };
        ring.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,
            named ? "AccentBrush" : "DimBrush");
        ToolTipService.SetInitialShowDelay(ring, 150);
        // Built fresh at open — the countdown must read the clock, not the rebuild.
        ring.ToolTipOpening += (_, _) => ring.ToolTip = CircleTip(zone, p, named);
        _spawnCircles.Add((halo, mx, my, -(d + 8) / 2, -(d + 8) / 2));
        _spawnCircles.Add((ring, mx, my, -d / 2, -d / 2));
        _canvas.Children.Add(halo);
        _canvas.Children.Add(ring);
        // Named points carry their NAME beside the circle — unless a running timer's
        // camp pin is already labeling that mob with name + countdown right there.
        if (namedName is not null
            && !timerNames.Any(t => SpawnCatalog.NameMatches(t, namedName)
                || SpawnCatalog.NameMatches(namedName, t)))
        {
            var label = new TextBlock { Text = namedName, FontSize = 9.5 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            _spawnCircles.Add((label, mx, my, d / 2 + 3, -7));
            _canvas.Children.Add(label);
        }
        _circleMeta.Add(new SpawnCircle { Ring = ring, Halo = halo, Point = p, Named = named });
    }

    private DateTime? CircleDue(string zone, SpawnPointLedger.SpawnPoint p, bool named, DateTime now)
    {
        if (!named) return _main.SpawnPoints.ProjectedRespawn(zone, p);
        return _main.SpawnTimers.Snapshot(now)
            .FirstOrDefault(t => string.Equals(t.Zone, zone, StringComparison.OrdinalIgnoreCase)
                && p.Mobs.Keys.Any(n => SpawnCatalog.NameMatches(t.Name, n)))?.DueAt;
    }

    private string CircleTip(string zone, SpawnPointLedger.SpawnPoint p, bool named)
    {
        var now = DateTime.Now;
        var lines = new List<string>();
        foreach (var kv in p.Mobs.OrderByDescending(kv => kv.Value.Kills)
                     .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            lines.Add($"{kv.Key} ×{kv.Value.Kills}");
        var (lastName, lastSeen) = p.LastKilled();
        lines.Add("");
        lines.Add($"Last kill: {lastName}, {EQBuddy.UI.Shared.Countdown.Format(now - lastSeen.LastKill)} ago");
        var label = named ? "Respawn" : "Projected respawn (~)";
        if (CircleDue(zone, p, named, now) is { } due)
            lines.Add(due <= now ? $"{label}: DUE" : $"{label}: {EQBuddy.UI.Shared.Countdown.Format(due - now)}");
        else
            lines.Add(named
                ? "Respawn: no running timer"
                : "Projected respawn: unknown — this zone documents no clock");
        return string.Join("\n", lines);
    }

    /// <summary>Start or stop the imminence pulse per circle. BeginAnimation(null)
    /// hands Opacity back to the local value each circle was built with.</summary>
    private void UpdatePulse(DateTime now, string zone)
    {
        foreach (var c in _circleMeta)
        {
            var due = CircleDue(zone, c.Point, c.Named, now);
            var secs = due is { } d ? (d - now).TotalSeconds : double.MaxValue;
            var imminent = secs <= PulseWindowSeconds && secs >= -PulseLingerSeconds;
            if (imminent == c.Pulsing) continue;
            c.Pulsing = imminent;
            if (imminent)
            {
                var beat = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.25,
                    TimeSpan.FromMilliseconds(550))
                {
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                };
                c.Ring.BeginAnimation(OpacityProperty, beat);
                c.Halo.BeginAnimation(OpacityProperty, beat);
            }
            else
            {
                c.Ring.BeginAnimation(OpacityProperty, null);
                c.Halo.BeginAnimation(OpacityProperty, null);
            }
        }
    }

    private void PlaceSpawnCircles()
    {
        foreach (var (el, x, y, dx, dy) in _spawnCircles)
        {
            var s = _view.Matrix.Transform(new Point(x, y));
            Canvas.SetLeft(el, s.X + dx);
            Canvas.SetTop(el, s.Y + dy);
        }
    }

    /// <summary>The breadcrumb trail: the last minute of your /locs in this zone,
    /// drawn as a comet tail that fades continuously on the wall clock (TrailFade) —
    /// tap a /loc hotbutton while traveling and the map shows where you just came
    /// from; stop moving and the tail burns down to nothing behind you. Geometry
    /// lives in map space; rebuilt when a new /loc arrives or the fade clock ticks
    /// over — age moves even when the player doesn't (David's field tests,
    /// 2026-08-10).</summary>
    private void UpdateTrail()
    {
        var trail = _main.CurrentSnapshot().LocationTrail;
        var showing = _map is not null && !_userPicked;
        var now = DateTime.Now;
        var stamp = showing ? (trail.Count, now.Ticks / FadeTick.Ticks) : (0, 0L);
        if (stamp == _trailStamp) return;
        _trailStamp = stamp;
        foreach (var p in _trailPaths) _mapLayer.Children.Remove(p);
        _trailPaths.Clear();
        if (!showing || trail.Count < 2) { AfterViewChanged(); return; }

        for (var i = 1; i < trail.Count; i++)
        {
            var alpha = EQBuddy.UI.Shared.TrailFade.Alpha(now - trail[i].Time);
            if (alpha == 0) continue;   // aged out — stays in the list, not on the map
            var (x1, y1) = ZoneMap.FromLoc(trail[i - 1].LocY, trail[i - 1].LocX);
            var (x2, y2) = ZoneMap.FromLoc(trail[i].LocY, trail[i].LocX);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, 255, 200, 60));
            brush.Freeze();
            var seg = new System.Windows.Shapes.Path
            {
                Stroke = brush,
                Data = new LineGeometry(new Point(x1, y1), new Point(x2, y2)),
            };
            _trailPaths.Add(seg);
            _mapLayer.Children.Add(seg);
        }
        AfterViewChanged();
    }

    /// <summary>Side panel + camp pins: every running spawn timer in the shown zone,
    /// its countdown, and a pin when a camp location is known — learned from your
    /// kill-time /loc first, the wiki's location field as fallback.</summary>
    private void UpdateNamedPanel()
    {
        _namedPanel.Children.Clear();
        foreach (var (el, _, _, _, _) in _campPins) _canvas.Children.Remove(el);
        _campPins.Clear();

        var zone = _main.CurrentZoneName;
        var header = new TextBlock
        {
            Text = zone.Length > 0 ? $"⏳ Named — {zone}" : "⏳ Named",
            FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 4),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        _namedPanel.Children.Add(header);

        var now = DateTime.Now;
        // Timers live under the CATALOG zone ("Befallen"); the log names the
        // instance ("Befallen 4 (Refined)"). Resolve before comparing — hopping
        // to another instance of the same zone must not empty the panel (David's
        // field test, 2026-08-10; countdowns already span instances by design).
        var timerZone = _main.SpawnTimers.CurrentZone?.Zone
            ?? SpawnCatalog.StripTierVariant(zone);
        var timers = _main.SpawnTimers.Snapshot(now)
            .Where(t => string.Equals(t.Zone, timerZone, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (timers.Count == 0)
        {
            var none = new TextBlock
            {
                Text = "No running timers here — kill a named (or its placeholder) and its countdown appears, pinned to wherever your last /loc put you.",
                FontSize = 10, TextWrapping = TextWrapping.Wrap,
            };
            none.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
            _namedPanel.Children.Add(none);
            return;
        }

        var pinsAllowed = _map is not null && !_userPicked;
        foreach (var t in timers)
        {
            // Camp: learned kill-time /loc wins; wiki location field is the fallback
            // (fetched through the same cached, polite lookup the Loot card uses).
            (double Y, double X)? camp = t is { CampLocY: { } cy, CampLocX: { } cx } ? (cy, cx) : null;
            var fromWiki = false;
            if (camp is null)
            {
                _main.EnsureMobLookup(t.Name);
                if (_main.WikiMobResult(t.Name)?.Mob?.LocYX is { } wl) { camp = wl; fromWiki = true; }
            }

            var due = t.DueAt;
            var isDue = t.IsDue(now);
            var countdown = due is null ? "?"
                : isDue ? "DUE"
                : EQBuddy.UI.Shared.Countdown.Format(due.Value - now);

            // Named cards (2026-08-11 modernization): name + camp source + a countdown
            // pill with an elapsed track — glanceable from across the room, DUE glows.
            var body = new StackPanel();
            var nameRow = new TextBlock
            {
                Text = $"{(camp is null ? "" : "📍 ")}{t.Name}",
                FontSize = 11.5, FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            nameRow.SetResourceReference(TextBlock.ForegroundProperty, isDue ? "WarnBrush" : "TextBrush");
            body.Children.Add(nameRow);
            var meta = new TextBlock
            {
                Text = camp is null
                    ? "no camp yet — /loc during the fight"
                    : fromWiki ? "camp from the wiki (~)" : "camp from your /loc at kill",
                FontSize = 9.5, Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            meta.SetResourceReference(TextBlock.ForegroundProperty, "DimBrush");
            body.Children.Add(meta);

            var gaugeRow = new Grid { Margin = new Thickness(0, 5, 0, 0) };
            gaugeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            gaugeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var pill = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 1, 7, 2) };
            pill.SetResourceReference(Border.BackgroundProperty, isDue ? "BadBrush" : "TrackBrush");
            var pillText = new TextBlock { Text = countdown, FontSize = 10.5, FontWeight = FontWeights.Bold };
            if (isDue) pillText.Foreground = Brushes.White;
            else pillText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            pill.Child = pillText;
            gaugeRow.Children.Add(pill);
            if (t.DurationSeconds is { } dur && dur > 0 && due is not null)
            {
                var frac = isDue ? 1.0 : Math.Clamp(1 - (due.Value - now).TotalSeconds / dur, 0, 1);
                var track = new Grid { Height = 3, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var trackBg = new Border { CornerRadius = new CornerRadius(1.5) };
                trackBg.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
                track.Children.Add(trackBg);
                var fill = new Border
                {
                    CornerRadius = new CornerRadius(1.5),
                    HorizontalAlignment = HorizontalAlignment.Left, Width = 0,
                };
                fill.SetResourceReference(Border.BackgroundProperty, isDue ? "BadBrush" : "AccentBrush");
                track.Children.Add(fill);
                track.SizeChanged += (_, se) => fill.Width = Math.Max(0, se.NewSize.Width * frac);
                Grid.SetColumn(track, 1);
                gaugeRow.Children.Add(track);
            }
            body.Children.Add(gaugeRow);

            var row = new Border
            {
                Child = body, CornerRadius = new CornerRadius(9),
                Padding = new Thickness(9, 6, 9, 7), Margin = new Thickness(0, 0, 0, 6),
                BorderThickness = new Thickness(1),
                ToolTip = camp is null
                    ? $"{t.Name} — no camp location yet: type /loc during the fight and the next kill pins it"
                    : $"{t.Name} — camp {(fromWiki ? "from the wiki (~)" : "from your /loc at kill time")}",
            };
            row.SetResourceReference(Border.BackgroundProperty, "RaisedBrush");
            row.SetResourceReference(Border.BorderBrushProperty, isDue ? "BadBrush" : "HairlineBrush");
            _namedPanel.Children.Add(row);

            if (camp is { } c && pinsAllowed)
            {
                var (mx, my) = ZoneMap.FromLoc(c.Y, c.X);
                var pin = new System.Windows.Shapes.Polygon
                {
                    Points = [new Point(0, 0), new Point(5, -10), new Point(-5, -10)],
                    StrokeThickness = 1,
                };
                pin.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
                    t.IsDue(now) ? "WarnBrush" : "BadBrush");
                pin.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "BgBrush");
                var label = new TextBlock { Text = $"{t.Name} {countdown}", FontSize = 10 };
                label.SetResourceReference(TextBlock.ForegroundProperty,
                    t.IsDue(now) ? "WarnBrush" : "BadBrush");
                _campPins.Add((pin, mx, my, 0, 0));
                _campPins.Add((label, mx, my, 7, -14));
                _canvas.Children.Add(pin);
                _canvas.Children.Add(label);
            }
        }
        PlaceCampPins();
    }

    private void PlaceCampPins()
    {
        foreach (var (el, x, y, dx, dy) in _campPins)
        {
            var s = _view.Matrix.Transform(new Point(x, y));
            Canvas.SetLeft(el, s.X + dx);
            Canvas.SetTop(el, s.Y + dy);
        }
        PlaceSpawnCircles();
    }

    private void ShowFile(string file)
    {
        try
        {
            var map = new ZoneMap();
            foreach (var layer in ZoneMapFiles.WithLayers(file))
            {
                var part = ZoneMap.Load(layer);
                map.Lines.AddRange(part.Lines);
                map.Points.AddRange(part.Points);
            }
            // Bounds: recompute from merged content.
            _map = ZoneMapFromParts(map);
            _shownFile = file;
            var stem = Path.GetFileNameWithoutExtension(file);
            if (_zonePick.SelectedItem as string != stem && _zonePick.Items.Contains(stem))
                _zonePick.SelectedItem = stem;
            RenderMap();
            FitToView();
            UpdateMarker();
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            _status.Text = $"Couldn't read {Path.GetFileName(file)} — {ex.Message}";
        }
    }

    private static ZoneMap ZoneMapFromParts(ZoneMap merged)
    {
        // ZoneMap tracks bounds during Load; merging lists bypassed that, so re-derive.
        var m = new ZoneMap();
        m.Lines.AddRange(merged.Lines);
        m.Points.AddRange(merged.Points);
        return m;
    }

    private (double MinX, double MinY, double MaxX, double MaxY) Bounds()
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var l in _map!.Lines)
        {
            minX = Math.Min(minX, Math.Min(l.X1, l.X2)); maxX = Math.Max(maxX, Math.Max(l.X1, l.X2));
            minY = Math.Min(minY, Math.Min(l.Y1, l.Y2)); maxY = Math.Max(maxY, Math.Max(l.Y1, l.Y2));
        }
        foreach (var p in _map.Points)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        return (minX, minY, maxX, maxY);
    }

    private readonly List<System.Windows.Shapes.Path> _linePaths = [];
    private readonly List<(FrameworkElement El, double X, double Y, double Dx, double Dy)> _pois = [];

    private void RenderMap()
    {
        _mapLayer.Children.Clear();
        _linePaths.Clear();
        foreach (var (el, _, _, _, _) in _pois) _canvas.Children.Remove(el);
        _pois.Clear();
        if (_map is null || _map.IsEmpty) return;

        // One Path per color: a Brewall file holds thousands of segments, and one
        // frozen StreamGeometry per color batch is what keeps that cheap.
        foreach (var group in _map.Lines.GroupBy(l => (l.R, l.G, l.B)))
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                foreach (var l in group)
                {
                    ctx.BeginFigure(new Point(l.X1, l.Y1), false, false);
                    ctx.LineTo(new Point(l.X2, l.Y2), true, false);
                }
            }
            geo.Freeze();
            var brush = new SolidColorBrush(Readable(group.Key.R, group.Key.G, group.Key.B));
            brush.Freeze();
            var path = new System.Windows.Shapes.Path { Data = geo, Stroke = brush, StrokeThickness = 1 };
            _linePaths.Add(path);
            _mapLayer.Children.Add(path);
        }

        // Points and labels live in SCREEN space, repositioned on every view change —
        // inside the scale transform they zoomed with the geometry (David caught the
        // first cut: half-scale fit made labels unreadably small and lines hairline).
        foreach (var p in _map.Points)
        {
            var color = new SolidColorBrush(Readable(p.R, p.G, p.B));
            var dot = new System.Windows.Shapes.Ellipse { Width = 5, Height = 5, Fill = color };
            var label = new TextBlock { Text = p.Label, FontSize = 11, Foreground = color };
            _pois.Add((dot, p.X, p.Y, -2.5, -2.5));
            _pois.Add((label, p.X, p.Y, 4, 3));
            _canvas.Children.Add(dot);
            _canvas.Children.Add(label);
        }
    }

    /// <summary>Everything screen-sized, after any view change: line strokes divide
    /// out the current scale (constant 1.2 px however far you zoom), POIs and the
    /// player marker re-place at their transformed positions.</summary>
    private void AfterViewChanged()
    {
        var scale = Math.Max(0.0001, Math.Abs(_view.Matrix.M11));
        foreach (var path in _linePaths) path.StrokeThickness = 1.2 / scale;
        foreach (var path in _trailPaths) path.StrokeThickness = 2.2 / scale;
        foreach (var (el, x, y, dx, dy) in _pois)
        {
            var s = _view.Matrix.Transform(new Point(x, y));
            Canvas.SetLeft(el, s.X + dx);
            Canvas.SetTop(el, s.Y + dy);
        }
        PlaceCampPins();
        UpdateMarker();
    }

    /// <summary>Map packs assume the game's parchment map window; a pure-black line
    /// vanishes on our dark theme. Lines darker than the floor are lifted to a
    /// readable gray, everything else keeps its pack color.</summary>
    private static Color Readable(byte r, byte g, byte b) =>
        r * 2 + g * 5 + b < 300 ? Color.FromRgb(170, 170, 170) : Color.FromRgb(r, g, b);

    /// <summary>The old forager's trick, offered wherever the marker is explained:
    /// the game itself makes /loc nearly automatic if you fold it into a social.</summary>
    private const string LocMacroTip =
        "Make /loc automatic-ish — the old forager's trick, no addons involved:\n" +
        "\n" +
        "In game, open Socials and make a macro:\n" +
        "    Line 1:  /loc\n" +
        "    Line 2:  /doability 1   (Forage, Sense Heading, Kick — whatever you already spam)\n" +
        "\n" +
        "Put it on the hotbar key that skill already lives on, and every press drops a\n" +
        "breadcrumb while doing exactly what the key did before.\n" +
        "\n" +
        "Even better: bind that hotbar slot to a movement key you TAP a lot — the turn\n" +
        "keys (A and D) are the sweet spot, because every course adjustment drops a\n" +
        "crumb and the trail draws itself. (W works too, but held keys don't repeat —\n" +
        "it only fires when you start moving.) Either way it's a plain in-game social —\n" +
        "the game runs it, EQBuddy just reads the log (and doesn't mind however many\n" +
        "/locs you produce).";

    private void UpdateMarker()
    {
        var loc = _main.CurrentSnapshot().LastLocation;
        var following = !_userPicked && _shownFile.Length > 0;
        if (_map is null || loc is null || !following)
        {
            _marker.Visibility = Visibility.Collapsed;
            if (_shownFile.Length > 0)
                _status.Text = $"{Path.GetFileNameWithoutExtension(_shownFile)} — type /loc in game to place " +
                    "your marker (hover here: a macro trick makes it near-automatic).";
            _status.ToolTip = LocMacroTip;
            return;
        }
        var (mx, my) = ZoneMap.FromLoc(loc.LocY, loc.LocX);
        var screen = _view.Matrix.Transform(new Point(mx, my));
        Canvas.SetLeft(_marker, screen.X - _marker.Width / 2);
        Canvas.SetTop(_marker, screen.Y - _marker.Height / 2);
        _marker.Visibility = Visibility.Visible;
        var age = DateTime.Now - loc.Time;
        _status.Text = $"{Path.GetFileNameWithoutExtension(_shownFile)} — position from /loc " +
            (age.TotalMinutes < 1 ? "just now" : $"{(int)age.TotalMinutes}m ago") +
            " (type /loc to update; EQBuddy reads only the log)";
        _status.ToolTip = LocMacroTip;
    }

    private void FitToView()
    {
        if (_map is null || _map.IsEmpty || _canvas.ActualWidth < 50) { return; }
        var (minX, minY, maxX, maxY) = Bounds();
        var w = Math.Max(1, maxX - minX);
        var h = Math.Max(1, maxY - minY);
        var scale = Math.Min(_canvas.ActualWidth / w, _canvas.ActualHeight / h) * 0.94;
        var m = Matrix.Identity;
        m.Translate(-minX - w / 2, -minY - h / 2);
        m.Scale(scale, scale);
        m.Translate(_canvas.ActualWidth / 2, _canvas.ActualHeight / 2);
        _view.Matrix = m;
        AfterViewChanged();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.25 : 0.8;
        var at = e.GetPosition(_canvas);
        var m = _view.Matrix;
        m.ScaleAt(factor, factor, at.X, at.Y);
        _view.Matrix = m;
        AfterViewChanged();
        e.Handled = true;
    }

    private void OnDrag(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(_canvas);
        var m = _view.Matrix;
        m.Translate(pos.X - _dragStart.X, pos.Y - _dragStart.Y);
        _view.Matrix = m;
        _dragStart = pos;
        AfterViewChanged();
    }

    private void ChooseFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Pick the folder holding zone map .txt files" };
        if (dlg.ShowDialog(this) != true) return;
        _main.Settings.MapFolder = dlg.FolderName;
        _main.Settings.Save();
        PopulateZoneList();
        _followedZone = "";
        _userPicked = false;
        MaybeRefresh(force: true);
    }
}
