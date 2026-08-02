using System.IO;
using System.Text.Json;

namespace EQBuddy.Core;

/// <summary>A player's edits to one named-mob entry. Null/empty means "no opinion —
/// use the catalog"; only what the player actually changed is stored.</summary>
public sealed class SpawnOverride
{
    public double? RespawnSeconds { get; set; }
    public string? Placeholder { get; set; }
    /// <summary>Alert when this named's timer hits zero. Default on — the window exists
    /// to tell you the camp is up.</summary>
    public bool Alert { get; set; } = true;
    /// <summary>This named's own due sound: "" follows the window's shared choice,
    /// "Off" stays silent, else a built-in name or a custom file path — the same scheme
    /// watch rules use, and for the same reason: you learn WHICH camp popped without
    /// looking away from the game.</summary>
    public string SoundName { get; set; } = "";
    /// <summary>True for named the player added themselves (not in the catalog) —
    /// zones change, wikis lag, and a family member camping something undocumented
    /// shouldn't have to wait for a release.</summary>
    public bool Custom { get; set; }
}

/// <summary>
/// Player edits to the spawn catalog, persisted separately (spawn-overrides.json) so
/// catalog updates in a release never collide with what the player fixed by observation
/// (SPAWN-002). Keyed "zone|name", case-insensitive.
/// </summary>
public sealed class SpawnOverrides
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly Dictionary<string, SpawnOverride> _byKey =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _path;

    public SpawnOverrides(string? path = null) => _path = path;

    public static string Key(string zone, string name) => $"{zone}|{name}";

    public SpawnOverride? Find(string zone, string name) =>
        _byKey.TryGetValue(Key(zone, name), out var o) ? o : null;

    public SpawnOverride GetOrAdd(string zone, string name)
    {
        var key = Key(zone, name);
        if (!_byKey.TryGetValue(key, out var o)) _byKey[key] = o = new SpawnOverride();
        return o;
    }

    public void Remove(string zone, string name) => _byKey.Remove(Key(zone, name));

    /// <summary>Player-added named for a zone, as (name, override) pairs.</summary>
    public IEnumerable<(string Name, SpawnOverride Override)> CustomFor(string zone)
    {
        var prefix = zone + "|";
        foreach (var (key, o) in _byKey)
            if (o.Custom && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                yield return (key[prefix.Length..], o);
    }

    public static SpawnOverrides Load(string path)
    {
        var result = new SpawnOverrides(path);
        try
        {
            if (File.Exists(path))
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, SpawnOverride>>(
                    File.ReadAllText(path), JsonOpts);
                if (map is not null)
                    foreach (var (k, v) in map) result._byKey[k] = v;
            }
        }
        catch
        {
            // A corrupt overrides file costs the edits, not the feature.
        }
        return result;
    }

    public void Save()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_byKey, JsonOpts));
        }
        catch
        {
            // Read-only disk shouldn't crash the widget; edits just won't persist.
        }
    }
}
