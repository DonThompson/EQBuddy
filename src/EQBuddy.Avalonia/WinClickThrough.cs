using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace EQBuddy.Avalonia;

/// <summary>
/// Win32 sibling of <see cref="MacClickThrough"/>/<see cref="X11ClickThrough"/>: the
/// extended-style recipe the WPF app uses (WS_EX_TRANSPARENT + WS_EX_LAYERED drops the
/// window out of hit-testing). The <see cref="ClickThrough"/> dispatcher predates a
/// Windows Avalonia build and still logs "unavailable" there — folding this in is a
/// one-line change flagged for the integration pass (that file is outside this package).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WinClickThrough
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExLayered = 0x80000;
    private const long WsExNoActivate = 0x08000000;

    public static bool Set(Window window, bool enabled)
    {
        if (Handle(window) is not { } hwnd) return false;
        try
        {
            var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
            style = enabled
                ? style | WsExTransparent | WsExLayered
                : style & ~WsExTransparent;
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
            return true;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return false;
        }
    }

    /// <summary>The one-way overlay recipe (cursor ring, alignment grid — same styles
    /// WPF's AlertWindow uses): transparent to the mouse, never activated, invisible
    /// to Alt-Tab. These windows only ever close, so there is no undo path.</summary>
    public static bool SetOverlay(Window window)
    {
        if (Handle(window) is not { } hwnd) return false;
        try
        {
            var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64()
                | WsExTransparent | WsExLayered | WsExNoActivate | WsExToolWindow;
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
            return true;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return false;
        }
    }

    private static IntPtr? Handle(Window window)
    {
        if (window.TryGetPlatformHandle() is { Handle: not 0 } handle) return handle.Handle;
        App.LogError("Click-through unavailable: Avalonia did not expose a native window handle.");
        return null;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
