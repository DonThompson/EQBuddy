using System.Text.Json;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// Every buff-duration entry must carry a NAME, not just repeat its own log message.
///
/// EQBuddy identifies buffs by the text the game prints, so an entry's label is what a
/// player sees wherever the spell itself can't be pinned down — the buff timers, the set
/// editors, the missing-buff line. 37 of 163 entries had no label at all and fell back
/// to the raw message, which is why DeusSilvam's haste timer read "You feel much faster"
/// while a properly-labelled neighbour read "Selo's Accelerating Chorus" (#162).
///
/// That is a data gap rather than a missing feature, and this is the guard that keeps it
/// closed: a new entry added without a label fails the build instead of shipping a row
/// that talks to the player in the game's voice rather than its own.
/// </summary>
public class BuffCatalogLabelTests
{
    private static readonly string Repo =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed record Spell(string Name, double DurationSeconds);
    private sealed record Entry(string Message, string Label, Spell[] Spells);

    private static Entry[] Load()
    {
        var json = File.ReadAllText(Path.Combine(Repo,
            "src", "EQBuddy.Core", "Data", "BuffDurations.json"));
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("messages").EnumerateArray()
            .Select(e => new Entry(
                e.GetProperty("message").GetString() ?? "",
                e.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "",
                e.TryGetProperty("spells", out var sp)
                    ? sp.EnumerateArray().Select(s => new Spell(
                        s.GetProperty("name").GetString() ?? "",
                        s.GetProperty("durationSeconds").GetDouble())).ToArray()
                    : []))
            .ToArray();
    }

    private static string Bare(string s) => s.TrimEnd('.').Trim();

    [Fact]
    public void EveryEntryHasALabelThatIsNotJustItsMessage()
    {
        var lazy = Load()
            .Where(e => e.Label.Length == 0
                || Bare(e.Label).Equals(Bare(e.Message), StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Message)
            .ToList();

        Assert.True(lazy.Count == 0,
            "These buff entries would show the game's own message where a name belongs — "
            + "give each one a label, in the style of the entries around it "
            + "(\"Haste line\", \"Cleric AC line\"):\n  " + string.Join("\n  ", lazy));
    }

    /// <summary>
    /// An unresolved landing is stored under its entry's label, so two entries sharing
    /// one would collide in the active-buff table and the second would evict the first.
    /// </summary>
    [Fact]
    public void LabelsAreUnique()
    {
        var dupes = Load()
            .GroupBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} ({g.Count()}x)")
            .ToList();

        Assert.True(dupes.Count == 0, "Duplicate buff labels: " + string.Join(", ", dupes));
    }

    [Fact]
    public void EveryEntryHasAMessageAndAtLeastOneSpellWithARealDuration()
    {
        foreach (var e in Load())
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Message), "an entry has no message");
            Assert.True(e.Spells.Length > 0, $"'{e.Label}' lists no spells");
            Assert.All(e.Spells, s =>
            {
                Assert.False(string.IsNullOrWhiteSpace(s.Name), $"'{e.Label}' has an unnamed spell");
                Assert.True(s.DurationSeconds > 0,
                    $"'{e.Label}' gives {s.Name} a duration of {s.DurationSeconds}s — a "
                    + "countdown from a zero or negative duration is worse than none");
            });
        }
    }
}
