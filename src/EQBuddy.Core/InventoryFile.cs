using System.IO;

namespace EQBuddy.Core;

/// <summary>
/// The game's `/outputfile inventory` dump (David's ask, 2026-08-11): tab-separated
/// Location / Name / ID / Count / Slots, one row per slot, "Empty" rows for the
/// gaps, written as `<Character>_<server>-Inventory.txt` beside eqgame.exe. Parsed
/// into base-name → count so the quest tracker can answer "what could I turn in
/// with what I'm already carrying" — upgrade tiers fold ("Leather Whip +9" counts
/// as Leather Whip, the name every quest and wiki page uses), a trailing `*`
/// (the dump's attuned/no-trade marker) is cosmetic, and stack counts are honored
/// (134 Bone Chips is 134 toward a 4-chip turn-in, not 1).
/// </summary>
public static class InventoryFile
{
    public sealed record Snapshot(string Path, DateTime WrittenAt, Dictionary<string, int> Counts)
    {
        public List<Entry> Entries { get; init; } = [];

        public int CountOf(string itemName) =>
            Counts.TryGetValue(QuestCatalog.BaseItemName(itemName), out var n) ? n : 0;
    }

    /// <summary>One occupied row, structure preserved: "General 1-Slot2" is inside the
    /// container occupying top-level "General 1"; a location without "-Slot" IS a
    /// top-level slot (worn gear, the General bag row itself, bank slots).</summary>
    public sealed record Entry(string Location, string Name, int Count)
    {
        public bool InContainer => Location.Contains("-Slot", StringComparison.Ordinal);
        public string ContainerSlot => InContainer
            ? Location[..Location.IndexOf("-Slot", StringComparison.Ordinal)] : Location;
    }

    public static List<Entry> ParseEntries(IEnumerable<string> lines)
    {
        var entries = new List<Entry>();
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length < 4) continue;
            var name = parts[1].Trim().TrimEnd('*').Trim();
            if (name.Length == 0 || name == "Empty" || name == "Name") continue;
            if (!int.TryParse(parts[3], out var count) || count < 1) continue;
            entries.Add(new Entry(parts[0].Trim(), name, count));
        }
        return entries;
    }

    public static Dictionary<string, int> Parse(IEnumerable<string> lines)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in ParseEntries(lines))
        {
            var baseName = QuestCatalog.BaseItemName(e.Name);
            counts[baseName] = counts.GetValueOrDefault(baseName) + e.Count;
        }
        return counts;
    }

    /// <summary>The newest dump for the followed character in the game folder (the
    /// Logs folder's parent — where /outputfile writes). Character match is on the
    /// filename prefix; server tags vary in case. Null when none exists yet.</summary>
    public static Snapshot? FindLatest(string? logFolder, string character)
    {
        if (logFolder is null || character.Length == 0) return null;
        var root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(logFolder));
        if (root is null || !Directory.Exists(root)) return null;
        try
        {
            var file = Directory.EnumerateFiles(root, $"{character}_*-Inventory.txt")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();
            if (file is null) return null;
            var entries = ParseEntries(File.ReadLines(file.FullName));
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                var baseName = QuestCatalog.BaseItemName(e.Name);
                counts[baseName] = counts.GetValueOrDefault(baseName) + e.Count;
            }
            return new Snapshot(file.FullName, file.LastWriteTime, counts) { Entries = entries };
        }
        catch { return null; }
    }
}
