using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace EQBuddy;

/// <summary>
/// The macOS/Wine overlay setup, opt-in and entirely inert off Wine.
///
/// On Windows this class does nothing: every entry point returns early unless the
/// process is really running under Wine (ntdll exports wine_get_version, the same
/// probe WineFonts uses). Under Wine it is still off until the player opts in with
/// <see cref="Core.AppSettings.WineFloatOverFullscreen"/> — the whole feature exists
/// for one setup, running the Windows build inside CrossOver on a Mac, and is
/// documented in docs/CrossOver-macOS-overlay.md.
///
/// When enabled it does two things. It writes the winemac.drv registry knobs that a
/// patched driver reads (harmless with a stock driver — the keys are simply unknown),
/// so the settings are the single control point. And it adds WS_EX_NOACTIVATE to every
/// EQBuddy window, which winemac maps to a non-activating panel: a click lands on the
/// widget (acceptsFirstMouse) without pulling the whole Wine process to the foreground,
/// so a fullscreen game stays foregrounded and the menu bar stays hidden. Windows keeps
/// ordinary activating windows.
/// </summary>
internal static class WineOverlay
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;

    private static bool _nonActivating;

    /// <summary>Called once at startup with the saved settings. Under Wine, syncs the
    /// driver knobs from the settings and — if the float-over-fullscreen opt-in is on —
    /// arms the non-activating behavior for every window, current and future.</summary>
    public static void Configure(Core.AppSettings settings)
    {
        if (!IsWine()) return;

        // The knobs a patched winemac.drv reads. Written to match the settings so one
        // toggle configures the bottle; a driver reads them at process start, so a
        // change takes effect on the next EQBuddy / game launch. No-op on a stock driver.
        SetMacDriverKey("EQBuddy.exe", "LetTopmostWindowsFloatOverFullscreen", settings.WineFloatOverFullscreen);
        SetMacDriverKey("eqgame.exe", "KeepFullscreenWhenInactive", settings.WineKeepGameFullscreen);

        _nonActivating = settings.WineFloatOverFullscreen;
        if (!_nonActivating) return;

        // Loaded fires for every Window from wherever it is created; winemac honours the
        // WS_EX_NOACTIVATE change dynamically (_setPreventsActivation:).
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window w) MakeNonActivating(w);
            }));
    }

    /// <summary>Adds WS_EX_NOACTIVATE to a window if the opt-in is armed. The main window
    /// calls this from OnSourceInitialized for a clean pre-show apply; the rest come
    /// through the Loaded handler.</summary>
    public static void MakeNonActivating(Window window)
    {
        if (!_nonActivating) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExNoActivate);
    }

    /// <summary>Writes (or clears) one HKCU\Software\Wine\AppDefaults\&lt;exe&gt;\Mac Driver
    /// value. "Y"/"N" is the winemac.drv boolean convention (IS_OPTION_TRUE).</summary>
    private static void SetMacDriverKey(string exe, string name, bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $@"Software\Wine\AppDefaults\{exe}\Mac Driver");
            key?.SetValue(name, on ? "Y" : "N");
        }
        catch
        {
            // Registry cosmetics must never stop startup.
        }
    }

    private static bool IsWine()
    {
        try
        {
            var ntdll = GetModuleHandleW("ntdll.dll");
            return ntdll != IntPtr.Zero &&
                   GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);
}
