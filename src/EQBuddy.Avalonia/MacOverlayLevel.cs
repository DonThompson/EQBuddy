using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace EQBuddy.Avalonia;

/// <summary>
/// Window-level control for every EQBuddy window on macOS. Avalonia maps <c>Topmost</c> to
/// NSFloatingWindowLevel (3), but winemac.drv — the driver behind CrossOver — hands a
/// fullscreen game a level above that (26, one past the status level, on the bottle this
/// was measured against). Every EQBuddy window loses that comparison, which is why the
/// overlay, the alerts, and the Options window all vanish behind the game.
///
/// The raise therefore covers all open windows rather than the overlay alone — a settings
/// window you cannot see is as broken as a hidden overlay — and it outranks
/// CGShieldingWindowLevel so it still holds if a bottle picks the shield level instead.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacOverlayLevel
{
    private const string Objc = "/usr/lib/libobjc.A.dylib";
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    /// <summary>kCGFloatingWindowLevel — what Avalonia already assigns a Topmost window,
    /// and what such a window drops back to when there is nothing to outrank.</summary>
    private const nint FloatingLevel = 3;

    /// <summary>NSNormalWindowLevel, the resting level for a window that never asked to
    /// float — History, for one. Restoring this rather than the floating level matters:
    /// leaving it at 3 would park an ordinary dialog over every other app.</summary>
    private const nint NormalLevel = 0;

    /// <summary>NSPopUpMenuWindowLevel — where Avalonia rests its menus and tooltips. Above
    /// an ordinary window normally, which is exactly why raising windows without raising
    /// popups inverts the two.</summary>
    private const nint PopupLevel = 101;

    /// <summary>One above what a fullscreen Wine window claims. The shielding level sits
    /// a little below the maximum window level, so the increments cannot overflow.</summary>
    private static readonly Lazy<nint> AboveShield = new(() => CGShieldingWindowLevel() + 1);

    /// <summary>Popups keep their place above their own windows while raised — the whole
    /// stack shifts up together rather than collapsing onto one level, where ordering would
    /// be left to whichever window was fronted last.</summary>
    private static readonly Lazy<nint> AboveShieldPopup = new(() => CGShieldingWindowLevel() + 2);

    /// <summary>Fallback identification for a Wine host whose executable is not itself a
    /// .exe — a bare wineloader, or CrossOver fronting the bottle on the game's behalf.
    /// Matched against both the bundle identifier and the executable path.</summary>
    private static readonly string[] WineMarkers = ["crossover", "codeweavers", "wine", "whisky"];

    /// <summary>Ensure runs on a timer, so a permanent failure must not append to the log
    /// once a second.</summary>
    private static bool _loggedLevelFailure;

    private static readonly HashSet<string> LoggedFrontmost = [];

    private static bool _raised;

    /// <summary>
    /// Re-evaluates whether EQBuddy should be outranking the game and applies the answer to
    /// every open window. Called on a tick, because a window opened since the last pass —
    /// Options, History, an alert — starts at whatever level Avalonia gave it.
    /// </summary>
    public static void Update()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        // Reaching for one of our own windows makes EQBuddy frontmost, which must not be
        // read as "the game is gone": the game holds its raised level while merely
        // deactivated, so lowering here would drop the Options window the user just opened
        // straight back behind it. Any window counts, not just the overlay — editing
        // settings leaves the main window inactive. An unidentifiable frontmost app holds
        // the current state too.
        if (IsWineHostFrontmost() is { } wineFront)
            _raised = wineFront || (_raised && desktop.Windows.Any(w => w.IsActive));

        HashSet<IntPtr> handled = [];
        foreach (var window in desktop.Windows)
        {
            Ensure(window, _raised);
            if (NativeWindow(window) is { } nsWindow) handled.Add(nsWindow);
        }

        EnsurePopups(_raised, handled);
    }

    /// <summary>
    /// Puts one window above a fullscreen CrossOver game, or back at its resting level.
    /// Safe to call repeatedly — the current level is read back rather than assumed, so a
    /// window Avalonia has re-levelled behind our back is repaired on the next pass.
    /// </summary>
    public static bool Ensure(Window window, bool aboveShield)
    {
        if (NativeWindow(window) is not { } nsWindow) return false;
        var resting = window.Topmost ? FloatingLevel : NormalLevel;
        return ApplyLevel(nsWindow, aboveShield ? AboveShield.Value : resting);
    }

    /// <summary>
    /// Raises the popup windows — context menus, tooltips, dropdowns — that Avalonia opens
    /// outside the <c>Window</c> list. Each is a standalone NSWindow at NSPopUpMenuWindowLevel
    /// with no parent to inherit from, so raising a window without raising its popups leaves
    /// the gear menu opening *behind* the window that owns it.
    /// </summary>
    /// <param name="known">NSWindows already handled as ordinary windows.</param>
    private static void EnsurePopups(bool aboveShield, HashSet<IntPtr> known)
    {
        try
        {
            var app = objc_msgSend_Ptr(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            if (app == IntPtr.Zero) return;
            var windows = objc_msgSend_Ptr(app, sel_registerName("windows"));
            if (windows == IntPtr.Zero) return;

            var count = objc_msgSend_GetLong(windows, sel_registerName("count"));
            var itemAt = sel_registerName("objectAtIndex:");
            for (nint i = 0; i < count; i++)
            {
                var nsWindow = objc_msgSend_PtrLong(windows, itemAt, i);
                if (nsWindow == IntPtr.Zero || known.Contains(nsWindow)) continue;
                // The process also owns windows that are none of our business — an offscreen
                // TUINSWindow for text input, for one. Only Avalonia's own subclass is ours
                // to re-level; KVO swizzling shows up as NSKVONotifying_AvnWindow.
                if (!IsAvaloniaWindow(nsWindow)) continue;

                ApplyLevel(nsWindow, aboveShield ? AboveShieldPopup.Value : PopupLevel);
            }
        }
        catch (Exception ex)
        {
            LogLevelFailureOnce(ex);
        }
    }

    private static bool IsAvaloniaWindow(IntPtr nsWindow)
    {
        var name = class_getName(object_getClass(nsWindow));
        return name != IntPtr.Zero
            && Marshal.PtrToStringUTF8(name) is { } text
            && text.Contains("AvnWindow", StringComparison.Ordinal);
    }

    /// <summary>Reads the level back before writing: cheaper than an unconditional set on a
    /// once-a-second sweep, and it repairs a window something else has re-levelled.</summary>
    private static bool ApplyLevel(IntPtr nsWindow, nint desired)
    {
        try
        {
            var readLevel = sel_registerName("level");
            var writeLevel = sel_registerName("setLevel:");
            // A wrong selector on the wrong object is an unrecognized-selector abort, which
            // kills the process rather than throwing — so ask before sending.
            var cls = object_getClass(nsWindow);
            if (!class_respondsToSelector(cls, readLevel) || !class_respondsToSelector(cls, writeLevel))
            {
                LogLevelFailureOnce("NSWindow does not respond to level/setLevel:.");
                return false;
            }

            if (objc_msgSend_GetLong(nsWindow, readLevel) == desired) return true;
            objc_msgSend_SetLong(nsWindow, writeLevel, desired);
            return true;
        }
        catch (Exception ex)
        {
            LogLevelFailureOnce(ex);
            return false;
        }
    }

    /// <summary>
    /// Whether the frontmost application is hosting a Windows game — the only situation
    /// worth outranking the shield for. Raising the level unconditionally would also park
    /// EQBuddy over every other app's fullscreen.
    /// </summary>
    /// <returns><c>null</c> when the frontmost application cannot be identified, so the
    /// caller can hold its current state rather than drop the overlay on a momentary nil.</returns>
    public static bool? IsWineHostFrontmost()
    {
        try
        {
            var workspace = objc_msgSend_Ptr(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            if (workspace == IntPtr.Zero) return null;

            // nil during app switching, and for anything without a UI process.
            var app = objc_msgSend_Ptr(workspace, sel_registerName("frontmostApplication"));
            if (app == IntPtr.Zero) return null;

            var bundleId = NSStringToString(objc_msgSend_Ptr(app, sel_registerName("bundleIdentifier")));
            var executable = NSStringToString(objc_msgSend_Ptr(
                objc_msgSend_Ptr(app, sel_registerName("executableURL")), sel_registerName("path")));
            if (bundleId is null && executable is null) return null;

            // A frontmost app running a .exe is under a translation layer by definition, and
            // it is the only signal that survives reality: CrossOver fronts the game with a
            // nil bundle identifier and an executable copied to a temp path
            // (/var/folders/.../winetemp-.../eqgame.exe), so neither the identifier nor the
            // bottle path can be relied on to name Wine at all.
            var matched = IsWindowsExecutable(executable) || Matches(bundleId) || Matches(executable);
            if (!matched) LogFrontmostOnce(bundleId, executable);
            return matched;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return null;
        }
    }

    private static bool IsWindowsExecutable(string? path) =>
        path is { Length: > 0 } && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    private static bool Matches(string? identity) => identity is { Length: > 0 }
        && WineMarkers.Any(marker => identity.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static IntPtr? NativeWindow(Window window)
    {
        // No handle before the window is shown, which is ordinary rather than a fault —
        // the caller's next tick finds one.
        if (window.TryGetPlatformHandle() is not { } handle || handle.Handle == IntPtr.Zero)
            return null;

        // Avalonia reports "NSWindow" for its Cocoa windows and hands back the NSWindow*
        // itself (not the content NSView), which is what owns the window level.
        if (handle.HandleDescriptor is not { Length: > 0 } descriptor ||
            !descriptor.Contains("NSWindow", StringComparison.OrdinalIgnoreCase))
        {
            LogLevelFailureOnce($"native handle '{handle.HandleDescriptor}' is not an NSWindow.");
            return null;
        }

        return handle.Handle;
    }

    private static string? NSStringToString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero) return null;
        var utf8 = objc_msgSend_Ptr(nsString, sel_registerName("UTF8String"));
        return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
    }

    private static void LogLevelFailureOnce(object? detail)
    {
        if (_loggedLevelFailure) return;
        _loggedLevelFailure = true;
        App.LogError($"Overlay window level unavailable: {detail}");
    }

    /// <summary>Records each unrecognised frontmost app once, so a CrossOver build this
    /// matcher does not know about is diagnosable from the log instead of just failing
    /// to raise. Bounded, because the set of apps a user switches to is not.</summary>
    private static void LogFrontmostOnce(string? bundleId, string? executable)
    {
        var identity = $"{bundleId ?? "(no bundle id)"} | {executable ?? "(no executable)"}";
        if (LoggedFrontmost.Count >= 20 || !LoggedFrontmost.Add(identity)) return;
        App.LogError($"Overlay level: frontmost app not treated as a Wine host: {identity}");
    }

    [DllImport(CoreGraphics)]
    private static extern int CGShieldingWindowLevel();

    [DllImport(Objc)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(Objc)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(Objc)]
    private static extern IntPtr object_getClass(IntPtr obj);

    [DllImport(Objc)]
    private static extern IntPtr class_getName(IntPtr cls);

    [DllImport(Objc)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_respondsToSelector(IntPtr cls, IntPtr selector);

    /// <summary>Every objc_msgSend signature needs its own declaration: the call is not
    /// variadic on arm64, so the argument and return shapes have to be exact.</summary>
    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_Ptr(IntPtr receiver, IntPtr selector);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_PtrLong(IntPtr receiver, IntPtr selector, nint index);

    /// <summary>NSWindow's level is an NSInteger — pointer-width, not int.</summary>
    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_GetLong(IntPtr receiver, IntPtr selector);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_SetLong(IntPtr receiver, IntPtr selector, nint value);
}
