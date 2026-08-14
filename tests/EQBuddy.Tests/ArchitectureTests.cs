using System.Reflection;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The guardrails that keep the codebase shaped the way the docs claim it is:
/// Core and UI.Shared stay UI-toolkit-free (they're the seam the Avalonia app and
/// every port builds on), and the known god-files stop growing.
/// </summary>
public class ArchitectureTests
{
    // ---- layer purity ----

    /// <summary>Assembly names no Core/UI.Shared code may pull in. WPF and
    /// Avalonia types belong to the two UI projects; the moment one leaks into a
    /// shared layer, the Linux build and every downstream port breaks quietly.</summary>
    private static readonly string[] ForbiddenUiAssemblies =
    [
        "PresentationCore", "PresentationFramework", "WindowsBase",
        "System.Xaml", "System.Windows.Forms", "Avalonia",
    ];

    public static TheoryData<Assembly> SharedAssemblies => new()
    {
        typeof(EQBuddy.Core.LogParser).Assembly,        // EQBuddy.Core
        typeof(EQBuddy.UI.Shared.GameCommands).Assembly, // EQBuddy.UI.Shared
        // The companion server must stay hostable from the Avalonia lane too —
        // a WPF type leaking in here would quietly kill that.
        typeof(EQBuddy.Companion.CompanionServer).Assembly, // EQBuddy.Companion
    };

    [Theory]
    [MemberData(nameof(SharedAssemblies))]
    public void SharedLayersReferenceNoUiToolkit(Assembly assembly)
    {
        var offending = assembly.GetReferencedAssemblies()
            .Where(r => ForbiddenUiAssemblies.Any(f =>
                r.Name is { } n &&
                (n.Equals(f, StringComparison.OrdinalIgnoreCase) ||
                 n.StartsWith(f + ".", StringComparison.OrdinalIgnoreCase))))
            .Select(r => r.Name)
            .ToList();

        Assert.True(offending.Count == 0,
            $"{assembly.GetName().Name} must stay UI-toolkit-free but references: " +
            string.Join(", ", offending));
    }

    // ---- file-length ratchet ----

    /// <summary>
    /// THE RATCHET CONTRACT. These are the current line counts of the files that
    /// have historically absorbed every feature (measured 2026-08-14). The test
    /// fails when any of them grows more than 10% past its baseline.
    ///
    /// Shrink freely — and when you do, lower the baseline here in the same PR so
    /// the headroom doesn't quietly refill. Growth past the limit needs a
    /// deliberate baseline bump in the same PR, which makes "this file gets
    /// bigger" a reviewed decision instead of a drift. New logic usually belongs
    /// in Core or UI.Shared anyway, where it's testable without a window.
    /// </summary>
    private static readonly (string RelativePath, int BaselineLines)[] Hotspots =
    [
        (@"EQBuddy/MainWindow.xaml.cs", 4891),
        (@"EQBuddy.Core/SessionStats.cs", 2324),
        (@"EQBuddy/OptionsWindow.xaml.cs", 1547),
        (@"EQBuddy.Core/LogParser.cs", 853),
    ];

    private const double AllowedGrowth = 1.10;

    public static TheoryData<string, int> HotspotData()
    {
        var data = new TheoryData<string, int>();
        foreach (var (path, baseline) in Hotspots) data.Add(path, baseline);
        return data;
    }

    [Theory]
    [MemberData(nameof(HotspotData))]
    public void HotspotFilesDoNotGrowPastTheRatchet(string relativePath, int baselineLines)
    {
        var src = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src");
        var file = Path.GetFullPath(Path.Combine(src, relativePath));
        Assert.True(File.Exists(file), $"Ratchet hotspot moved or vanished: {file} — " +
            "update the path (or drop the entry) in ArchitectureTests.Hotspots.");

        var lines = File.ReadLines(file).Count();
        var limit = (int)(baselineLines * AllowedGrowth);

        Assert.True(lines <= limit,
            $"{relativePath} is {lines} lines — past its ratchet limit of {limit} " +
            $"(baseline {baselineLines} + 10%). Extract the new logic into Core/UI.Shared, " +
            "or bump the baseline in ArchitectureTests.Hotspots as a deliberate, " +
            "reviewed decision in this same PR.");

        // The other direction: a file that shrank well below baseline means someone
        // did the hard work — bank it. Warning-only would be invisible in CI, so
        // this fails too, asking for the baseline to be lowered to match.
        var slack = (int)(baselineLines * 0.85);
        Assert.True(lines >= slack,
            $"{relativePath} is {lines} lines, well under its {baselineLines} baseline. " +
            "Nice. Lower the baseline in ArchitectureTests.Hotspots so the freed " +
            "headroom can't quietly refill.");
    }
}
