using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Win32;

namespace EQBuddy.Avalonia;

/// <summary>
/// Global hotkeys, round two (#100, jlcrisp) — designed against the 1.34.0 disaster.
/// RegisterHotKey is system-wide: v1.12's DEFAULT binds ate Ctrl+Shift+T from every
/// browser on the machine, and the whole feature was torn out. The rule this time
/// (David, 2026-08-11): hotkeys exist ONLY when the player binds them — nothing is
/// bound by default, the Options UI says out loud that a bound key is claimed from
/// every app while EQBuddy runs, and unbinding is one click. Gestures are stored as
/// text ("Ctrl+Alt+M") in settings.json, so they're per-machine and hand-editable.
///
/// RegisterHotKey has no cross-platform sibling — X11 grabs fight the window manager
/// and macOS demands accessibility trust — so this is Windows-only for now: elsewhere
/// Apply keeps the bindings but registers nothing, and says so in the log once.
/// </summary>
public sealed class HotkeyManager
{
    /// <summary>The bindable actions, in Options display order.</summary>
    public static readonly (string Key, string Label)[] Actions =
    [
        ("toggleAll", "Show / hide all of EQBuddy"),
        ("toggleMinimize", "Minimize / restore the dashboard"),
        ("toggleMap", "Zone map"),
        ("toggleQuests", "Quest tracker"),
        ("toggleSpawns", "Spawn timers"),
        ("toggleClickThrough", "Click-through"),
    ];

    private const uint WmHotkey = 0x0312;
    private readonly Dictionary<int, string> _registered = new();
    private IntPtr _hwnd;
    private Action<string>? _fire;
    private bool _hooked;
    private bool _reportedUnsupported;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>Parses "Ctrl+Alt+M" / "Ctrl+Shift+F9" / "Win+Z". Null = unparseable.
    /// A modifier is REQUIRED — a bare letter bound globally would eat typing in the
    /// game's chat box, which is the 1.34.0 mistake with a different hat.</summary>
    public static (uint Mods, uint Vk)? Parse(string gesture)
    {
        uint mods = 0;
        var key = Key.None;
        foreach (var raw in gesture.Split('+'))
        {
            var part = raw.Trim();
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": mods |= 0x2; break;
                case "ALT": mods |= 0x1; break;
                case "SHIFT": mods |= 0x4; break;
                case "WIN" or "WINDOWS": mods |= 0x8; break;
                default:
                    if (!Enum.TryParse(part, ignoreCase: true, out key)) return null;
                    break;
            }
        }
        if (key == Key.None || mods == 0) return null;
        return VirtualKeyFor(key) is { } vk ? (mods, vk) : null;
    }

    /// <summary>WPF gets this from KeyInterop; Avalonia's Key enum shares WPF's names,
    /// so the ranges map straight onto Win32 virtual-key codes here.</summary>
    private static uint? VirtualKeyFor(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (uint)(0x41 + (key - Key.A)),
        >= Key.D0 and <= Key.D9 => (uint)(0x30 + (key - Key.D0)),
        >= Key.NumPad0 and <= Key.NumPad9 => (uint)(0x60 + (key - Key.NumPad0)),
        >= Key.F1 and <= Key.F24 => (uint)(0x70 + (key - Key.F1)),
        Key.Space => 0x20,
        Key.Tab => 0x09,
        Key.Insert => 0x2D,
        Key.Delete => 0x2E,
        Key.Home => 0x24,
        Key.End => 0x23,
        Key.PageUp => 0x21,
        Key.PageDown => 0x22,
        Key.Up => 0x26,
        Key.Down => 0x28,
        Key.Left => 0x25,
        Key.Right => 0x27,
        Key.Pause => 0x13,
        Key.Scroll => 0x91,
        Key.PrintScreen => 0x2C,
        Key.Multiply => 0x6A,
        Key.Add => 0x6B,
        Key.Subtract => 0x6D,
        Key.Divide => 0x6F,
        Key.Decimal => 0x6E,
        Key.OemTilde => 0xC0,
        Key.OemMinus => 0xBD,
        Key.OemPlus => 0xBB,
        Key.OemOpenBrackets => 0xDB,
        Key.OemCloseBrackets => 0xDD,
        Key.OemPipe => 0xDC,
        Key.OemSemicolon => 0xBA,
        Key.OemQuotes => 0xDE,
        Key.OemComma => 0xBC,
        Key.OemPeriod => 0xBE,
        Key.OemQuestion => 0xBF,
        _ => null,
    };

    /// <summary>(Re)registers every configured binding; safe to call after Options
    /// edits. Conflicts (another app owns the combo) are skipped silently — the
    /// Options row shows what's bound, and rebinding is the fix. The WndProc hook the
    /// WPF app adds by hand comes via Win32Properties here, added once per window.</summary>
    public void Apply(Window window, IReadOnlyDictionary<string, string> bindings, Action<string> fire)
    {
        _fire = fire;
        if (!OperatingSystem.IsWindows())
        {
            if (!_reportedUnsupported && bindings.Any(b => b.Value.Length > 0))
            {
                _reportedUnsupported = true;
                App.LogError("Global hotkeys are Windows-only for now; " +
                    "bindings are kept but inactive on this platform.");
            }
            return;
        }
        if (window.TryGetPlatformHandle() is not { Handle: not 0 } handle) return;
        _hwnd = handle.Handle;
        if (!_hooked)
        {
            Win32Properties.AddWndProcHookCallback(window, Hook);
            _hooked = true;
        }
        foreach (var id in _registered.Keys) UnregisterHotKey(_hwnd, id);
        _registered.Clear();
        var nextId = 0xB0DD;   // arbitrary app-local id base
        foreach (var (action, gesture) in bindings)
        {
            if (gesture.Length == 0 || Parse(gesture) is not { } parsed) continue;
            var id = nextId++;
            if (RegisterHotKey(_hwnd, id, parsed.Mods, parsed.Vk))
                _registered[id] = action;
        }
    }

    private IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _registered.TryGetValue(wParam.ToInt32(), out var action))
        {
            _fire?.Invoke(action);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Clear()
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var id in _registered.Keys) UnregisterHotKey(_hwnd, id);
        _registered.Clear();
    }
}
