using System.Text.RegularExpressions;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>Wine boxes any glyph missing from the bundled EQBuddy Sans font — its
/// DirectWrite reads only the primary font for WPF apps, no fallback of any kind,
/// so under Wine every icon must live in the app's own font (WineFonts.cs). Icons
/// get added as string literals far from that font; this scans src for every
/// symbol/emoji codepoint and pins the font manifest as a superset, so a new 🗿
/// anywhere in the UI fails here until scripts/build-icon-font.py is re-run.</summary>
public class IconFontCoverageTests
{
    // The projects the WPF app renders text from. Avalonia has its own font stack
    // and never runs under Wine, so it is deliberately not scanned.
    private static readonly string[] Roots = ["EQBuddy", "EQBuddy.UI.Shared", "EQBuddy.Core"];

    [Fact]
    public void BundledIconFontCoversEverySymbolInSource()
    {
        var src = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src");
        var manifest = File.ReadAllLines(
                Path.Combine(src, "EQBuddy", "Fonts", "EQBuddySans.codepoints.txt"))
            .Where(l => l.Length > 0)
            .Select(l => Convert.ToInt32(l, 16))
            .ToHashSet();

        var missing = Roots
            .SelectMany(r => Directory.EnumerateFiles(Path.Combine(src, r), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => IconCodepoints(File.ReadAllText(f))
                .Select(cp => (cp, file: Path.GetFileName(f))))
            .Where(x => !manifest.Contains(x.cp))
            .Distinct()
            .OrderBy(x => x.cp)
            .ToList();

        Assert.True(missing.Count == 0,
            "Icon codepoints missing from Fonts/EQBuddySans.ttf — re-run scripts/build-icon-font.py:\n" +
            string.Join("\n", missing.Select(x =>
                $"  U+{x.cp:X5} '{char.ConvertFromUtf32(x.cp)}' ({x.file})")));
    }

    /// <summary>Every code point ≥ U+2190 a file can put on screen: literal text
    /// (surrogate pairs combined), C# \uXXXX and \UXXXXXXXX escapes, and XAML
    /// &#x…; entities. FE0E/FE0F (variation selectors) map to zero-width glyphs in
    /// the font; FEFF/FFFF are parser sentinels that never render. These rules are
    /// mirrored exactly by the scan in scripts/build-icon-font.py.</summary>
    private static IEnumerable<int> IconCodepoints(string text)
    {
        var found = new List<int>();
        foreach (Match m in Regex.Matches(text, @"\\U([0-9A-Fa-f]{8})"))
            found.Add(Convert.ToInt32(m.Groups[1].Value, 16));
        var rest = Regex.Replace(text, @"\\U[0-9A-Fa-f]{8}", "");
        rest = Regex.Replace(rest, @"\\u([Dd][89ABab][0-9A-Fa-f]{2})\\u([Dd][C-Fc-f][0-9A-Fa-f]{2})", m =>
        {
            found.Add(char.ConvertToUtf32(
                (char)Convert.ToInt32(m.Groups[1].Value, 16),
                (char)Convert.ToInt32(m.Groups[2].Value, 16)));
            return "";
        });
        foreach (Match m in Regex.Matches(rest, @"\\u([0-9A-Fa-f]{4})"))
        {
            var v = Convert.ToInt32(m.Groups[1].Value, 16);
            if (v is < 0xD800 or > 0xDFFF) found.Add(v);
        }
        foreach (Match m in Regex.Matches(text, @"&#x([0-9A-Fa-f]+);"))
            found.Add(Convert.ToInt32(m.Groups[1].Value, 16));
        foreach (Match m in Regex.Matches(text, @"&#([0-9]+);"))
            found.Add(int.Parse(m.Groups[1].Value));
        for (var i = 0; i < text.Length; i++)
        {
            int cp;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(text[i])) continue;
            else cp = text[i];
            found.Add(cp);
        }
        return found.Where(cp => cp >= 0x2190 && cp is not (0xFE0E or 0xFE0F or 0xFEFF or 0xFFFF));
    }
}
