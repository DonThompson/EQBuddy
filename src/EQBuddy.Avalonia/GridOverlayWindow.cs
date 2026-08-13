using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EQBuddy.Core;

namespace EQBuddy.Avalonia;

/// <summary>
/// Full-desktop click-through alignment grid (discussion #34 — "a bit OCD with my UI
/// and would love a way to overlay a grid so I can align UI elements"). Covers the
/// whole virtual screen so it works on whichever monitor the game lives on, never
/// takes focus, never eats a click — the right-click menu toggle (or the Options
/// checkbox) is the only way in or out, exactly like the alert tile's click-through.
///
/// WPF tiles two DrawingBrushes; Avalonia has no DrawingBrush, so a custom control
/// draws the lines in one Render pass (minor lines at the chosen spacing, stronger
/// lines every fourth) — spacing changes are one InvalidateVisual, and rendering cost
/// stays flat no matter the desk size.
/// </summary>
public sealed class GridOverlayWindow : Window
{
    private readonly AppSettings _settings;
    private readonly GridLines _grid = new();

    public GridOverlayWindow(AppSettings settings)
    {
        _settings = settings;
        Title = "EQBuddy Grid";
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        IsHitTestVisible = false;
        Focusable = false;
        Content = _grid;
        Opened += (_, _) =>
        {
            CoverVirtualScreen();
            if (OperatingSystem.IsWindows()) WinClickThrough.SetOverlay(this);
            else ClickThrough.Set(this, enabled: true);
        };
        // The accent brush live-mutates on theme switches; the grid repaints with it.
        AppTheme.AccentBrush.PropertyChanged += OnAccentChanged;
        Closed += (_, _) => AppTheme.AccentBrush.PropertyChanged -= OnAccentChanged;
        ApplySpacing();
    }

    /// <summary>(Re)read the current settings — called on open and live from the
    /// Options slider, so dragging it tunes the grid in place.</summary>
    public void ApplySpacing() => _grid.Spacing = Math.Clamp(_settings.GridSpacing, 8, 256);

    private void OnAccentChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SolidColorBrush.ColorProperty) _grid.InvalidateVisual();
    }

    private void CoverVirtualScreen()
    {
        var screens = Screens.All;
        if (screens.Count == 0) return;
        double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
        foreach (var s in screens)
        {
            l = Math.Min(l, s.Bounds.X);
            t = Math.Min(t, s.Bounds.Y);
            r = Math.Max(r, s.Bounds.Right);
            b = Math.Max(b, s.Bounds.Bottom);
        }
        Position = new PixelPoint((int)l, (int)t);
        // Screen bounds are device pixels, Width/Height are DIPs — one window over a
        // mixed-DPI union is converted with this window's own scale, so a secondary at a
        // different factor may see the grid fall short or overshoot there. Cosmetic, and
        // exactly the compromise the rest of the port makes for whole-desk overlays.
        var scale = DesktopScaling > 0 ? DesktopScaling : 1.0;
        Width = (r - l) / scale;
        Height = (b - t) / scale;
    }

    /// <summary>One aliased render pass: crisp 1px lines, no per-line controls.</summary>
    private sealed class GridLines : Control
    {
        private double _spacing = 32;

        public double Spacing
        {
            get => _spacing;
            set { _spacing = value; InvalidateVisual(); }
        }

        public GridLines() => RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        public override void Render(DrawingContext context)
        {
            var accent = AppTheme.AccentBrush.Color;
            var minor = new Pen(new SolidColorBrush(accent, 0.18), 1);
            var major = new Pen(new SolidColorBrush(accent, 0.40), 1);
            var bounds = Bounds;
            for (var (x, i) = (0.0, 0); x <= bounds.Width; x += _spacing, i++)
                context.DrawLine(i % 4 == 0 ? major : minor,
                    new Point(x, 0), new Point(x, bounds.Height));
            for (var (y, i) = (0.0, 0); y <= bounds.Height; y += _spacing, i++)
                context.DrawLine(i % 4 == 0 ? major : minor,
                    new Point(0, y), new Point(bounds.Width, y));
        }
    }
}
