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
    private int _trailStamp = -1;

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
        follow.Click += (_, _) => { _userPicked = false; MaybeRefresh(force: true); };
        var chooseFolder = Theming.Button("Maps folder…");
        chooseFolder.Margin = new Thickness(6, 0, 0, 0);
        chooseFolder.Click += (_, _) => ChooseFolder();
        DockPanel.SetDock(zoneLabel, Dock.Left);
        DockPanel.SetDock(chooseFolder, Dock.Right);
        DockPanel.SetDock(follow, Dock.Right);
        bar.Children.Add(zoneLabel);
        bar.Children.Add(chooseFolder);
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
                "beside Logs — install a map pack (e.g. Brewall's) there, or point me at one with Maps folder…";
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

    /// <summary>The breadcrumb trail: your /locs in this zone, drawn as a path that
    /// fades toward the past — tap a /loc hotbutton while traveling and the map
    /// shows the route you took. Geometry lives in map space; rebuilt only when a
    /// new /loc arrives.</summary>
    private void UpdateTrail()
    {
        var trail = _main.CurrentSnapshot().LocationTrail;
        var showing = _map is not null && !_userPicked;
        var stamp = showing ? trail.Count : 0;
        if (stamp == _trailStamp) return;
        _trailStamp = stamp;
        foreach (var p in _trailPaths) _mapLayer.Children.Remove(p);
        _trailPaths.Clear();
        if (!showing || trail.Count < 2) { AfterViewChanged(); return; }

        for (var i = 1; i < trail.Count; i++)
        {
            var (x1, y1) = ZoneMap.FromLoc(trail[i - 1].LocY, trail[i - 1].LocX);
            var (x2, y2) = ZoneMap.FromLoc(trail[i].LocY, trail[i].LocX);
            var age = (double)(trail.Count - i) / trail.Count;   // 0 = newest
            var brush = new SolidColorBrush(Color.FromArgb(
                (byte)(200 - 150 * age), 255, 200, 60));
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
        var timers = _main.SpawnTimers.Snapshot(now)
            .Where(t => string.Equals(t.Zone, zone, StringComparison.OrdinalIgnoreCase))
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
            var countdown = due is null ? "?"
                : t.IsDue(now) ? "DUE"
                : EQBuddy.UI.Shared.Countdown.Format(due.Value - now);
            var row = new TextBlock
            {
                Text = $"{(camp is null ? "" : "📍 ")}{t.Name} · {countdown}",
                FontSize = 11, Margin = new Thickness(0, 1, 0, 1),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = camp is null
                    ? $"{t.Name} — no camp location yet: type /loc during the fight and the next kill pins it"
                    : $"{t.Name} — camp {(fromWiki ? "from the wiki (~)" : "from your /loc at kill time")}",
            };
            row.SetResourceReference(TextBlock.ForegroundProperty,
                t.IsDue(now) ? "WarnBrush" : "TextBrush");
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
        "Even better, if the game's keybinds allow the overlap: bind that hotbar slot\n" +
        "to a MOVEMENT key (W). Then simply walking forward drops breadcrumbs, and the\n" +
        "trail draws itself. Either way it's a plain in-game social — the game runs it,\n" +
        "EQBuddy just reads the log (and doesn't mind however many /locs you produce).";

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
