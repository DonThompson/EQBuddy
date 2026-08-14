using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace EQBuddy;

/// <summary>
/// Wine prefixes ship no Segoe fonts, and Wine's DirectWrite consults ONLY the
/// primary font's cmap for WPF apps — no system fallback, and no per-character
/// traversal even within an explicit family list (field-tested 2026-08-14: an
/// icon-only font listed first rendered its own glyphs and boxed every Latin
/// letter). So every glyph the app draws — text AND the 💀/🔮/💰 section icons
/// (issue #8's Wine players; CrossOver on macOS alike) — must live in one font.
/// The app bundles exactly that: an OFL Noto Sans base with the icon glyphs
/// merged in (rebuilt by scripts/build-icon-font.py whenever a new icon appears —
/// a unit test pins the coverage), swapped in only when actually running under
/// Wine. Native Windows never takes this path and keeps Segoe UI Variable and its
/// color emoji untouched.
/// </summary>
internal static class WineFonts
{
    /// <summary>Swaps AppFontFamily to the bundled font under Wine. Called before
    /// any window exists, so the first frame is already correct.</summary>
    public static void ApplyIfNeeded(ResourceDictionary appResources)
    {
        if (!IsWine()) return;
        appResources["AppFontFamily"] = new FontFamily(
            new Uri("pack://application:,,,/"),
            "./Fonts/#EQBuddy Sans, Segoe UI Variable Text, Segoe UI");
    }

    /// <summary>The canonical Wine check: ntdll exports wine_get_version under
    /// Wine and never on real Windows. No environment variables, no guessing.</summary>
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
            // Font cosmetics must never stop startup.
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);
}
