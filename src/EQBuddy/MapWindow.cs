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
        var side = new ScrollViewer
        {
            Content = _namedPanel,
            Width = 190,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
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
        UpdateNamedPanel();
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
            var pill = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 1, 8, 2) };
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
