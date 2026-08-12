using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy;

/// <summary>
/// The fight timeline: the whole pull on one canvas, a lane per skill, a DPS graph on
/// top. Marks are drawn immediate-mode (one visual, DrawingContext) — a long raid fight
/// is thousands of events, and a WPF shape per event would crawl. Scroll zooms around
/// the cursor, dragging the plot pans, Fit snaps back; while the fight is live the
/// window follows it, until the user zooms — their viewport is then theirs.
/// </summary>
public partial class FightTimelineWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Func<(LastFightInfo? Fight, List<GameEvent> Events, string Pet)> _source;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TimelineViewport _view = new();
    private string _signature = "";

    public FightTimelineWindow(AppSettings settings,
        Func<(LastFightInfo?, List<GameEvent>, string)> source)
    {
        InitializeComponent();
        _settings = settings;
        _source = source;

        Chrome.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "BgBrush");
        Chrome.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "HairlineBrush");
        TipChrome.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "BgBrush");
        TipChrome.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "AccentBrush");
        TitleText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextBrush");
        foreach (var dim in new[] { SubTitle, PeakText, TipText })
            dim.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "DimBrush");

        Graph.View = _view;
        Lanes.View = _view;
        Lanes.HoverChanged += OnHover;

        if (ScreenGuard.OnScreen(_settings.TimelineLeft, _settings.TimelineTop,
                Math.Max(420, _settings.TimelineWidth), 200))
        {
            Left = _settings.TimelineLeft; Top = _settings.TimelineTop;
            if (_settings.TimelineWidth >= MinWidth) Width = _settings.TimelineWidth;
            if (_settings.TimelineHeight >= MinHeight) Height = _settings.TimelineHeight;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
        }

        Closed += (_, _) =>
        {
            _tick.Stop();
            _settings.TimelineLeft = Left; _settings.TimelineTop = Top;
            _settings.TimelineWidth = Width; _settings.TimelineHeight = Height;
            _settings.Save();
        };
        SizeChanged += (_, _) => { if (_view.Fit) ApplyFit(); Redraw(); };
        MouseWheel += OnZoom;
        _tick.Tick += (_, _) => Refresh();
        _tick.Start();
        Loaded += (_, _) => Refresh();
    }

    /// <summary>Re-pull the fight; rebuild only when it moved (new events or a new
    /// pull). Fit-mode keeps refitting as a live fight grows; a user viewport holds.</summary>
    private void Refresh()
    {
        var (fight, events, pet) = _source();
        if (fight is null || fight.Start == DateTime.MinValue)
        {
            TitleText.Text = "Fight timeline";
            SubTitle.Text = "no fight yet — pull something";
            _view.Timeline = null;
            Redraw();
            return;
        }

        var signature = $"{fight.Name}|{fight.Start.Ticks}|{events.Count}|{(int)fight.DurationSeconds}";
        if (signature == _signature) return;
        _signature = signature;

        _view.Timeline = TimelineBuilder.Build(events, fight.Start, fight.DurationSeconds, pet);
        TitleText.Text = fight.Name;
        SubTitle.Text = $"{Clock(fight.DurationSeconds)} · {_view.Timeline.EventCount:N0} events"
            + (fight.InProgress ? " · live" : "");
        PeakText.Text = $"peak {_view.Timeline.PeakDps:N0} dps @ {Clock(_view.Timeline.PeakSec)}";
        if (_view.Fit) ApplyFit();
        Redraw();
    }

    private void ApplyFit()
    {
        _view.Fit = true;
        _view.OffsetSec = 0;
        _view.PixelsPerSec = _view.Timeline is { } t
            ? Math.Max(0.5, (Lanes.ActualWidth - LanesPanel.LabelWidth) / t.DurationSeconds)
            : 1;
    }

    private void Redraw() { Graph.InvalidateVisual(); Lanes.Refit(); }

    private void OnZoom(object sender, MouseWheelEventArgs e)
    {
        if (_view.Timeline is not { } t) return;
        var pos = e.GetPosition(Lanes).X - LanesPanel.LabelWidth;
        if (pos < 0) return;
        var anchor = _view.OffsetSec + pos / _view.PixelsPerSec;
        var factor = e.Delta > 0 ? 1.25 : 1 / 1.25;
        var fitPps = Math.Max(0.5, (Lanes.ActualWidth - LanesPanel.LabelWidth) / t.DurationSeconds);
        _view.PixelsPerSec = Math.Clamp(_view.PixelsPerSec * factor, fitPps, 120);
        _view.Fit = Math.Abs(_view.PixelsPerSec - fitPps) < 0.001;
        _view.OffsetSec = _view.Fit ? 0
            : Math.Clamp(anchor - pos / _view.PixelsPerSec, 0, t.DurationSeconds);
        Redraw();
        e.Handled = true;
    }

    internal void Pan(double deltaPixels)
    {
        if (_view.Timeline is not { } t || _view.Fit) return;
        _view.OffsetSec = Math.Clamp(_view.OffsetSec - deltaPixels / _view.PixelsPerSec,
            0, t.DurationSeconds);
        Redraw();
    }

    private void OnHover(TimelineMark? mark, TimelineLane? lane, Point at)
    {
        if (mark is null || lane is null) { MarkTip.IsOpen = false; return; }
        var what = mark.Hollow ? mark.Label
            : $"{mark.Amount:N0}{(mark.Crit ? " · Critical" : "")}"
              + (mark.Label.Length > 0 && !mark.Crit ? $" · {mark.Label}" : "");
        TipText.Text = $"{lane.Name} · {what} · {Clock(mark.Sec)}";
        MarkTip.HorizontalOffset = at.X + 14;
        MarkTip.VerticalOffset = at.Y + 10;
        MarkTip.IsOpen = true;
    }

    internal static string Clock(double seconds) =>
        $"{(int)seconds / 60}:{(int)seconds % 60:00}";

    private void OnDragWindow(object sender, MouseButtonEventArgs e) { try { DragMove(); } catch { } }
    private void OnFit(object sender, MouseButtonEventArgs e) { ApplyFit(); Redraw(); }
    private void OnClose(object sender, MouseButtonEventArgs e) => Close();

    private void OnGripDrag(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }
}

/// <summary>Shared viewport: where in the fight we're looking and how magnified.
/// Both render panels read it; the window writes it.</summary>
internal sealed class TimelineViewport
{
    public FightTimeline? Timeline;
    public double OffsetSec;
    public double PixelsPerSec = 1;
    /// <summary>True until the user zooms: the view tracks the whole (possibly still
    /// growing) fight. Fit is a mode, not a moment — a live fight keeps refitting.</summary>
    public bool Fit = true;

    public Brush Text = Brushes.White, Dim = Brushes.Gray, Accent = Brushes.CornflowerBlue,
        Good = Brushes.SeaGreen, Bad = Brushes.IndianRed, Warn = Brushes.Orange;

    public void CaptureTheme(FrameworkElement from)
    {
        Text = Find(from, "TextBrush", Text); Dim = Find(from, "DimBrush", Dim);
        Accent = Find(from, "AccentBrush", Accent); Good = Find(from, "GoodBrush", Good);
        Bad = Find(from, "BadBrush", Bad); Warn = Find(from, "WarnBrush", Warn);
    }

    private static Brush Find(FrameworkElement e, string key, Brush fallback) =>
        e.TryFindResource(key) as Brush ?? fallback;
}

/// <summary>The three-line DPS graph: you+pet (accent), pet alone (dim), incoming (bad),
/// with the peak flagged. Scales to the visible window so zooming the lanes zooms this too.</summary>
internal sealed class DpsGraphPanel : FrameworkElement
{
    internal TimelineViewport? View;

    protected override void OnRender(DrawingContext dc)
    {
        if (View is not { Timeline: { } t } v) return;
        v.CaptureTheme(this);
        var w = ActualWidth - LanesPanel.LabelWidth;
        var h = ActualHeight - 14;   // room for the peak caption
        if (w <= 10 || h <= 10) return;
        dc.PushTransform(new TranslateTransform(LanesPanel.LabelWidth, 0));

        var max = Math.Max(1, new[] { t.DpsSeries, t.PetDpsSeries, t.IncomingDpsSeries }
            .SelectMany(s => s).DefaultIfEmpty(1).Max());
        Draw(dc, t.IncomingDpsSeries, v, w, h, max, v.Bad, 1);
        Draw(dc, t.PetDpsSeries, v, w, h, max, v.Dim, 1);
        Draw(dc, t.DpsSeries, v, w, h, max, v.Accent, 1.6);

        var peakX = (t.PeakSec - v.OffsetSec) * v.PixelsPerSec;
        if (peakX >= 0 && peakX <= w && t.PeakDps > 0)
        {
            dc.DrawLine(new Pen(v.Dim, 0.6) { DashStyle = DashStyles.Dot },
                new Point(peakX, 2), new Point(peakX, h));
            var label = Caption($"{t.PeakDps:N0} peak", v.Dim);
            dc.DrawText(label, new Point(
                Math.Clamp(peakX - label.Width / 2, 0, Math.Max(0, w - label.Width)), h + 1));
        }
        dc.Pop();
    }

    private static void Draw(DrawingContext dc, double[] series, TimelineViewport v,
        double w, double h, double max, Brush brush, double thickness)
    {
        if (series.Length < 2) return;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var started = false;
            for (var i = 0; i < series.Length; i++)
            {
                var x = (i - v.OffsetSec) * v.PixelsPerSec;
                if (x < -2 || x > w + 2) continue;
                var p = new Point(x, h - series[i] / max * (h - 4));
                if (!started) { ctx.BeginFigure(p, false, false); started = true; }
                else ctx.LineTo(p, true, true);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, new Pen(brush, thickness) { LineJoin = PenLineJoin.Round }, geo);
    }

    private FormattedText Caption(string text, Brush brush) => new(text,
        CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface("Segoe UI"), 10, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}

/// <summary>
/// The lanes: label column left, marks right. Solid bars are damage (height tracks
/// the hit's share of the lane's biggest), hollow outlines are the attempts that
/// produced nothing — miss, resist, fizzle, absorb. Time axis along the bottom.
/// </summary>
internal sealed class LanesPanel : FrameworkElement
{
    public const double LabelWidth = 138;
    private const double LaneHeight = 24;
    private const double AxisHeight = 16;

    internal TimelineViewport? View;
    internal event Action<TimelineMark?, TimelineLane?, Point>? HoverChanged;

    public LanesPanel()
    {
        MouseMove += OnMove;
        MouseLeave += (_, _) => HoverChanged?.Invoke(null, null, default);
        MouseLeftButtonDown += (_, e) => { _panFrom = e.GetPosition(this).X; CaptureMouse(); };
        MouseLeftButtonUp += (_, _) => { _panFrom = null; ReleaseMouseCapture(); };
    }

    private double? _panFrom;

    /// <summary>Height claims the lanes' room so the ScrollViewer can scroll a tall
    /// fight; call after the timeline changes.</summary>
    public void Refit()
    {
        Height = Math.Max(60, (View?.Timeline?.Lanes.Count ?? 0) * LaneHeight + AxisHeight + 2);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Hit-test background: pan-drags on empty plot need the element to BE there.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (View is not { Timeline: { } t } v) return;
        v.CaptureTheme(this);
        var plotW = ActualWidth - LabelWidth;
        if (plotW <= 10) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface("Segoe UI");
        var hairline = new Pen(v.Dim, 0.35);

        for (var i = 0; i < t.Lanes.Count; i++)
        {
            var lane = t.Lanes[i];
            var y = i * LaneHeight;
            dc.DrawLine(hairline, new Point(0, y + LaneHeight), new Point(ActualWidth, y + LaneHeight));

            var name = new FormattedText(lane.Name, CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, face, 11,
                lane.Kind == LaneKind.Incoming ? v.Bad : v.Text, dpi)
            { MaxTextWidth = LabelWidth - 44, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(name, new Point(2, y + 4));
            if (lane.Total > 0)
            {
                var total = new FormattedText(Short(lane.Total), CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, face, 10, v.Dim, dpi);
                dc.DrawText(total, new Point(LabelWidth - total.Width - 8, y + 5));
            }

            var laneMax = Math.Max(1, lane.Marks.Max(m => m.Amount));
            var solid = lane.Kind switch
            {
                LaneKind.Pet => v.Good,
                LaneKind.Incoming => v.Bad,
                _ => v.Accent,
            };
            var hollowPen = new Pen(lane.Kind == LaneKind.Incoming ? v.Bad : v.Warn, 1);
            var markW = Math.Clamp(v.PixelsPerSec * 0.25, 2, 5);
            foreach (var m in lane.Marks)
            {
                var x = LabelWidth + (m.Sec - v.OffsetSec) * v.PixelsPerSec;
                if (x < LabelWidth - 4 || x > ActualWidth + 4) continue;
                if (m.Hollow)
                    dc.DrawRectangle(null, hollowPen, new Rect(x, y + 7, markW + 1, 9));
                else
                {
                    var bar = 5 + 12 * Math.Sqrt((double)m.Amount / laneMax);
                    dc.DrawRectangle(m.Crit ? v.Warn : solid, null,
                        new Rect(x, y + LaneHeight - 3 - bar, markW, bar));
                }
            }
        }

        DrawAxis(dc, v, t, plotW, t.Lanes.Count * LaneHeight, face, dpi);
    }

    private void DrawAxis(DrawingContext dc, TimelineViewport v, FightTimeline t,
        double plotW, double y, Typeface face, double dpi)
    {
        var visible = plotW / v.PixelsPerSec;
        var step = new[] { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 }
            .First(s => visible / s <= 12 || s == 600);
        for (var sec = Math.Ceiling(v.OffsetSec / step) * step;
             sec <= Math.Min(t.DurationSeconds, v.OffsetSec + visible); sec += step)
        {
            var x = LabelWidth + (sec - v.OffsetSec) * v.PixelsPerSec;
            dc.DrawLine(new Pen(v.Dim, 0.35), new Point(x, y), new Point(x, y + 4));
            var label = new FormattedText(FightTimelineWindow.Clock(sec),
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, 9.5, v.Dim, dpi);
            dc.DrawText(label, new Point(x - label.Width / 2, y + 4));
        }
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (_panFrom is { } from && e.LeftButton == MouseButtonState.Pressed)
        {
            ((FightTimelineWindow)Window.GetWindow(this)!).Pan(pos.X - from);
            _panFrom = pos.X;
            return;
        }
        if (View is not { Timeline: { } t } v || pos.X < LabelWidth)
        { HoverChanged?.Invoke(null, null, default); return; }

        var laneIx = (int)(pos.Y / LaneHeight);
        if (laneIx < 0 || laneIx >= t.Lanes.Count)
        { HoverChanged?.Invoke(null, null, default); return; }
        var lane = t.Lanes[laneIx];
        var sec = v.OffsetSec + (pos.X - LabelWidth) / v.PixelsPerSec;
        var slack = 6 / v.PixelsPerSec;   // 6 px of grace either side
        TimelineMark? best = null;
        foreach (var m in lane.Marks)   // sorted by Sec; small enough to walk
        {
            if (m.Sec > sec + slack) break;
            if (m.Sec >= sec - slack &&
                (best is null || Math.Abs(m.Sec - sec) < Math.Abs(best.Sec - sec)))
                best = m;
        }
        HoverChanged?.Invoke(best, best is null ? null : lane, pos);
    }

    internal static string Short(long n) => n switch
    {
        >= 1_000_000 => $"{n / 1_000_000.0:0.#}m",
        >= 10_000 => $"{n / 1000.0:0.#}k",
        >= 1_000 => $"{n / 1000.0:0.##}k",
        _ => n.ToString("N0"),
    };
}
