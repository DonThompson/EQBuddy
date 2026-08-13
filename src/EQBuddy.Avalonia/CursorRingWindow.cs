using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using EQBuddy.Core;

namespace EQBuddy.Avalonia;

/// <summary>
/// A click-through ring riding the mouse cursor (issue #81, Bigmatt500: "I often lose
/// my tiny cursor when playing"). Same overlay recipe as the grid: never focused,
/// never hit-tested, toggled from the right-click menu only. The window is a small
/// square that FOLLOWS the cursor rather than a desk-sized canvas repainting on every
/// move — 30 Hz position updates of a tiny window cost nothing measurable.
/// Accent-colored with a dark under-stroke so it reads on bright and dark ground both.
///
/// Avalonia exposes no global cursor position, so the probe dispatches per-OS like
/// ClickThrough does: Win32 GetCursorPos, X11 XQueryPointer, macOS CGEventGetLocation.
/// A platform where the probe fails (Wayland, headless) logs once and the ring simply
/// stops following — the window stays harmless and closable from the same menu toggle.
/// </summary>
public sealed class CursorRingWindow : Window
{
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _follow;
    private readonly Ellipse _under = new();
    private readonly Ellipse _ring = new();
    // macOS reports the cursor in points; Avalonia positions speak whatever unit the
    // backend chose. Calibrated once per open against the primary display so the two
    // agree regardless of which convention this Avalonia version uses.
    private double _macRatio = 1.0;
    private IntPtr _x11Display;

    public CursorRingWindow(AppSettings settings)
    {
        _settings = settings;
        Title = "EQBuddy Cursor Ring";
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        IsHitTestVisible = false;
        Focusable = false;

        var grid = new Grid();
        grid.Children.Add(_under);
        grid.Children.Add(_ring);
        Content = grid;
        ApplySize();

        _follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _follow.Tick += (_, _) => Follow();
        Opened += (_, _) =>
        {
            ApplyClickThrough();
            CalibrateMac();
            _follow.Start();
        };
        Closed += (_, _) =>
        {
            _follow.Stop();
            if (_x11Display != IntPtr.Zero) { XCloseDisplay(_x11Display); _x11Display = IntPtr.Zero; }
        };
    }

    /// <summary>(Re)build from settings — called on open and live from Options.</summary>
    public void ApplySize()
    {
        var d = Math.Clamp(_settings.CursorRingSize, 20, 160);
        Width = d + 8;
        Height = d + 8;
        foreach (var (e, stroke, thickness) in new (Ellipse, IBrush, double)[]
        {
            (_under, new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 4.5),
            (_ring, AppTheme.AccentBrush, 2.5),
        })
        {
            e.Width = d;
            e.Height = d;
            e.Stroke = stroke;
            e.StrokeThickness = thickness;
            e.HorizontalAlignment = HorizontalAlignment.Center;
            e.VerticalAlignment = VerticalAlignment.Center;
        }
    }

    private void Follow()
    {
        try
        {
            if (OperatingSystem.IsWindows()) { FollowWindows(); return; }
            if (TryProbeCursor(out var x, out var y))
            {
                // Half the window's size in position-space units, via the backend's own
                // client→screen math so mixed conventions can't drift the center.
                var topLeft = this.PointToScreen(new Point(0, 0));
                var bottomRight = this.PointToScreen(new Point(Bounds.Width, Bounds.Height));
                Position = new PixelPoint(x - (bottomRight.X - topLeft.X) / 2,
                                          y - (bottomRight.Y - topLeft.Y) / 2);
            }
        }
        catch (Exception ex)
        {
            // A 30 Hz probe must not become a 30 Hz error log: report once and stop.
            _follow.Stop();
            App.LogError(ex);
        }
    }

    private bool TryProbeCursor(out int x, out int y)
    {
        x = y = 0;
        if (OperatingSystem.IsMacOS())
        {
            // CGEventGetLocation speaks top-left-origin global points — no flip needed.
            var evt = CGEventCreate(IntPtr.Zero);
            if (evt == IntPtr.Zero) return ProbeUnavailable();
            var p = CGEventGetLocation(evt);
            CFRelease(evt);
            x = (int)Math.Round(p.X * _macRatio);
            y = (int)Math.Round(p.Y * _macRatio);
            return true;
        }
        if (OperatingSystem.IsLinux())
        {
            if (_x11Display == IntPtr.Zero) _x11Display = XOpenDisplay(IntPtr.Zero);
            if (_x11Display == IntPtr.Zero) return ProbeUnavailable();
            if (!XQueryPointer(_x11Display, XDefaultRootWindow(_x11Display),
                    out _, out _, out var rootX, out var rootY, out _, out _, out _))
                return false;   // pointer on another screen this tick — just wait
            x = rootX;
            y = rootY;
            return true;
        }
        return ProbeUnavailable();
    }

    private bool ProbeUnavailable()
    {
        _follow.Stop();
        App.LogError("Cursor ring unavailable: no global cursor position on this platform.");
        return false;
    }

    private void CalibrateMac()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var cg = CGDisplayBounds(CGMainDisplayID());
            var primary = Screens.Primary?.Bounds.Width ?? 0;
            _macRatio = cg.Width > 0 && primary > 0 ? primary / cg.Width : 1.0;
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    private void ApplyClickThrough()
    {
        if (OperatingSystem.IsWindows()) WinClickThrough.SetOverlay(this);
        else ClickThrough.Set(this, enabled: true);
    }

    // ---- Windows: raw device pixels end to end. A DIP-based Position converts through
    // ONE monitor's scale factor, so on mixed-DPI desks the ring drifts off the pointer
    // the moment it crosses monitors (the WPF port hit exactly this) — SetWindowPos
    // speaks the cursor's own coordinate system everywhere.

    private void FollowWindows()
    {
        if (TryGetPlatformHandle() is not { Handle: not 0 } handle) return;
        if (!GetCursorPos(out var p) || !GetWindowRect(handle.Handle, out var r)) return;
        SetWindowPos(handle.Handle, IntPtr.Zero,
            p.X - (r.Right - r.Left) / 2, p.Y - (r.Bottom - r.Top) / 2, 0, 0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private const uint SwpNoSize = 0x1, SwpNoZOrder = 0x4, SwpNoActivate = 0x10;

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Win32Point p);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Win32Rect r);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after,
        int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect { public int Left, Top, Right, Bottom; }

    // ---- macOS: CoreGraphics, no accessibility permission needed for a read.

    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint { public double X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect { public double X, Y, Width, Height; }

    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CoreGraphics)] private static extern CGPoint CGEventGetLocation(IntPtr evt);
    [DllImport(CoreGraphics)] private static extern uint CGMainDisplayID();
    [DllImport(CoreGraphics)] private static extern CGRect CGDisplayBounds(uint display);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr obj);

    // ---- X11: root-window coordinates are the same device pixels Avalonia positions in.

    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool XQueryPointer(IntPtr display, IntPtr window,
        out IntPtr root, out IntPtr child, out int rootX, out int rootY,
        out int winX, out int winY, out uint mask);
}
