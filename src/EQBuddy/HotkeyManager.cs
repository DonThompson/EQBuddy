using System.Windows;
using System.Windows.Input;

namespace EQBuddy;

/// <summary>
/// Global hotkeys, round two (#100, jlcrisp) — designed against the 1.34.0 disaster.
/// RegisterHotKey is system-wide: v1.12's DEFAULT binds ate Ctrl+Shift+T from every
/// browser on the machine, and the whole feature was torn out. The rule this time
/// (David, 2026-08-11): hotkeys exist ONLY when the player binds them — nothing is
/// bound by default, the Options UI says out loud that a bound key is claimed from
/// every app while EQBuddy runs, and unbinding is one click. Gestures are stored as
/// text ("Ctrl+Alt+M") in settings.json, so they're per-machine and hand-editable.
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

    private const int WmHotkey = 0x0312;
    private readonly Dictionary<int, string> _registered = new();
    private IntPtr _hwnd;
    private Action<string>? _fire;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>Parses "Ctrl+Alt+M" / "Ctrl+Shift+F9" / "Win+Z". Null = unparseable.
    /// A modifier is REQUIRED — a bare letter bound globally would eat typing in the
    /// game's chat box, which is the 1.34.0 mistake with a different hat.</summary>
    public static (uint Mods, uint Vk)? Parse(string gesture)
    {
        uint mods = 0;
        Key key = Key.None;
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
        return (mods, (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    /// <summary>(Re)registers every configured binding; safe to call after Options
    /// edits. Conflicts (another app owns the combo) are skipped silently — the
    /// Options row shows what's bound, and rebinding is the fix.</summary>
    public void Apply(IntPtr hwnd, IReadOnlyDictionary<string, string> bindings, Action<string> fire)
    {
        _hwnd = hwnd;
        _fire = fire;
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

    /// <summary>WndProc hook (add via HwndSource.AddHook).</summary>
    public IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
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
        foreach (var id in _registered.Keys) UnregisterHotKey(_hwnd, id);
        _registered.Clear();
    }
}
