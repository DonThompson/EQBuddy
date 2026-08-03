using System.Reflection;
using System.Text.Json;

namespace EQBuddy.Core;

/// <summary>One release's notable changes, for the post-update popup.</summary>
public sealed class WhatsNewEntry
{
    public string Version { get; set; } = "";
    public string Date { get; set; } = "";
    public string[] Highlights { get; set; } = [];
}

/// <summary>
/// The "What's new" notes shown once after an update (NOTES-001, David's call: the
/// family gets updates near-silently via auto-update, so features were landing that
/// nobody knew existed). Content is embedded — no network — and release.ps1 refuses
/// to release a version with no entry here, so the popup can't silently rot.
/// </summary>
public static class WhatsNewCatalog
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<WhatsNewEntry> Load()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("EQBuddy.Core.Data.WhatsNew.json")
            ?? throw new InvalidOperationException("WhatsNew.json missing from resources");
        return JsonSerializer.Deserialize<List<WhatsNewEntry>>(stream, JsonOpts) ?? [];
    }

    /// <summary>Entries newer than what the user last saw, up to and including the
    /// running version, newest first — an update that skipped three releases shows all
    /// three. Unparseable versions are skipped rather than trusted.</summary>
    public static List<WhatsNewEntry> EntriesBetween(string lastSeen, string current)
    {
        if (!Version.TryParse(Normalize(current), out var cur)) return [];
        Version.TryParse(Normalize(lastSeen), out var seen);
        return Load()
            .Where(e => Version.TryParse(Normalize(e.Version), out var v)
                        && v <= cur && (seen is null || v > seen))
            .OrderByDescending(e => Version.Parse(Normalize(e.Version)))
            .ToList();
    }

    /// <summary>"1.25.0.0" and "v1.25.0" both mean 1.25.0.</summary>
    private static string Normalize(string version)
    {
        version = version.Trim().TrimStart('v', 'V');
        var parts = version.Split('.');
        return parts.Length > 3 ? string.Join('.', parts[..3]) : version;
    }
}
