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

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(_status);
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
        foreach (var (el, x, y, dx, dy) in _pois)
        {
            var s = _view.Matrix.Transform(new Point(x, y));
            Canvas.SetLeft(el, s.X + dx);
            Canvas.SetTop(el, s.Y + dy);
        }
        UpdateMarker();
    }

    /// <summary>Map packs assume the game's parchment map window; a pure-black line
    /// vanishes on our dark theme. Lines darker than the floor are lifted to a
    /// readable gray, everything else keeps its pack color.</summary>
    private static Color Readable(byte r, byte g, byte b) =>
        r * 2 + g * 5 + b < 300 ? Color.FromRgb(170, 170, 170) : Color.FromRgb(r, g, b);

    private void UpdateMarker()
    {
        var loc = _main.CurrentSnapshot().LastLocation;
        var following = !_userPicked && _shownFile.Length > 0;
        if (_map is null || loc is null || !following)
        {
            _marker.Visibility = Visibility.Collapsed;
            if (_shownFile.Length > 0)
                _status.Text = $"{Path.GetFileNameWithoutExtension(_shownFile)} — type /loc in game to place your marker.";
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
