using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EQBuddy.Core;

/// <summary>One named mob in the shipped spawn catalog.</summary>
public sealed class SpawnEntry
{
    public string Name { get; set; } = "";
    /// <summary>Documented respawn in seconds, or null when nobody has written it down —
    /// the zone's <see cref="SpawnZone.NamedDefaultSeconds"/> is the fallback, and a null
    /// there too means the timer has to come from the player.</summary>
    public double? RespawnSeconds { get; set; }
    public string Variance { get; set; } = "";
    /// <summary>The placeholder NPC whose death also restarts this named's cycle, or "".
    /// Displayed as "Named — Placeholder (npc)" and matched against kill lines like the
    /// named itself: killing the PH is what a camp mostly does, and in classic spawn
    /// cycles it runs the same clock.</summary>
    public string Placeholder { get; set; } = "";
    public string Source { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>A zone's named mobs plus its zone-wide default named-respawn.</summary>
public sealed class SpawnZone
{
    public string Zone { get; set; } = "";
    /// <summary>The name the game's "You have entered X." line uses, when it differs
    /// from the wiki's title for the zone.</summary>
    public string LogZoneName { get; set; } = "";
    public double? NamedDefaultSeconds { get; set; }
    public List<SpawnEntry> Named { get; set; } = [];

    public bool MatchesZoneName(string zoneName)
    {
        if (zoneName.Length == 0) return false;
        return Eq(Zone, zoneName) || Eq(LogZoneName, zoneName)
            // "You have entered The Estate of Unrest." vs catalog "Estate of Unrest":
            // containment either way covers article and prefix differences.
            || Contains(zoneName, Zone) || Contains(Zone, zoneName)
            || (LogZoneName.Length > 0 && (Contains(zoneName, LogZoneName) || Contains(LogZoneName, zoneName)));

        static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        static bool Contains(string hay, string needle) =>
            needle.Length >= 4 && hay.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The shipped database of named-mob spawn timers (SPAWN-001), embedded so installs need
/// no extra files. Sources: eqlwiki.com (the EQ Legends community wiki, authoritative)
/// with classic-EQ references filling gaps — see the JSON's comment field. Times are
/// community lore, not game data: they ship as *defaults*, and every number is editable
/// in the Spawns window (edits live in spawn-overrides.json via <see cref="SpawnOverrides"/>,
/// never in this file, so an app update refreshing the catalog can't eat anyone's edits).
/// </summary>
public sealed class SpawnCatalog
{
    public List<SpawnZone> Zones { get; init; } = [];

    private sealed class CatalogFile
    {
        public string Comment { get; set; } = "";
        public List<SpawnZone> Zones { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static SpawnCatalog LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("EQBuddy.Core.Data.SpawnCatalog.json")
            ?? throw new InvalidOperationException("SpawnCatalog.json missing from resources");
        var file = JsonSerializer.Deserialize<CatalogFile>(stream, JsonOpts);
        return new SpawnCatalog { Zones = file?.Zones ?? [] };
    }

    /// <summary>The catalog zone the given log zone name refers to, or null.</summary>
    public SpawnZone? FindZone(string zoneName) =>
        // Exact/log-name matches beat containment so "Qeynos" resolves to Qeynos, not
        // Qeynos Hills.
        Zones.FirstOrDefault(z =>
            string.Equals(z.Zone, zoneName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(z.LogZoneName, zoneName, StringComparison.OrdinalIgnoreCase))
        ?? Zones.FirstOrDefault(z => z.MatchesZoneName(zoneName));

    /// <summary>Effective respawn seconds for an entry in a zone: the entry's own timer,
    /// else the zone default, else null (unknown — the player has to supply one).</summary>
    public static double? EffectiveSeconds(SpawnZone zone, SpawnEntry entry) =>
        entry.RespawnSeconds ?? zone.NamedDefaultSeconds;

    /// <summary>Case-insensitive name equality that shrugs off leading articles and a
    /// trailing plural: catalog names are wiki titles ("a froglok ghoul lord"), kill
    /// lines arrive normalized ("froglok ghoul lord"), and placeholder notes are
    /// sometimes plural ("orc centurions").</summary>
    public static bool NameMatches(string catalogName, string killedName)
    {
        if (catalogName.Length == 0 || killedName.Length == 0) return false;
        var a = Fold(catalogName);
        var b = Fold(killedName);
        return a.Equals(b, StringComparison.OrdinalIgnoreCase);

        static string Fold(string s)
        {
            s = s.Trim();
            foreach (var article in (string[])["a ", "an ", "the "])
                if (s.Length > article.Length && s.StartsWith(article, StringComparison.OrdinalIgnoreCase))
                { s = s[article.Length..]; break; }
            if (s.EndsWith('s') && !s.EndsWith("ss", StringComparison.Ordinal)) s = s[..^1];
            return s;
        }
    }
}
