using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EQBuddy.Core;

/// <summary>Parsed eqlwiki mob page: what the creature can drop, per the wiki.</summary>
public sealed class MobInfo
{
    public string Name { get; set; } = "";
    /// <summary>The wiki page title that actually answered (may differ from Name via
    /// redirects or "(Zone)" disambiguation) — edit links must target this.</summary>
    public string PageTitle { get; set; } = "";
    public string Zone { get; set; } = "";
    public string Level { get; set; } = "";
    /// <summary>Wiki-listed drops in page order. Rarity is the wiki's own word ("Common",
    /// "Uncommon"…) or "" when the page doesn't annotate the entry.</summary>
    public List<(string Item, string Rarity)> Drops { get; set; } = [];
    public string WikiUrl { get; set; } = "";
    /// <summary>The page's location field, as written ("-500, -200 by the tower").</summary>
    public string Location { get; set; } = "";
    /// <summary>First coordinate pair from the location field, in /loc's (Y, X)
    /// order — editors paste /loc output. Null when the field is prose-only.</summary>
    public (double Y, double X)? LocYX { get; set; }
}

public sealed record MobLookupResult(MobInfo? Mob, ItemLookupState State, DateTime? FetchedAt);

/// <summary>A page as the wiki actually served it: the title AFTER redirect and
/// normalization, plus its wikitext. The title is the point — the API's redirects=1
/// means a request for one name can land on another, and the page's own title is the
/// only one safe to print in a [[link]] or an edit URL.</summary>
public sealed record WikiPageText(string Title, string Wikitext);

/// <summary>
/// On-demand mob lookups against eqlwiki.com for the Loot card's target-drops block
/// (David, 2026-08-06): the log names what you're fighting, the wiki knows what it can
/// drop. Same discipline as <see cref="EqlWikiItemService"/> — one bounded fetch per
/// request, 7-day disk cache, honest LIVE/CACHED/STALE labels, nothing background.
///
/// Page shapes (2026-08-06 survey): named and regular mobs BOTH use {{Namedmobpage}};
/// regular mobs live at article-titled pages ("A Spite Golem"), named at bare names
/// ("Lockjaw"); redirects=1 resolves letter case. known_loot lists drops as {{:Item}}
/// transclusions with an optional rarity span after each.
/// </summary>
public sealed partial class EqlWikiMobService
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

    private readonly string _cacheDir;
    private readonly Func<string, Task<WikiPageText?>> _fetch;
    private readonly Func<string, Task<List<string>>> _search;
    private static readonly HttpClient Http = EqlWikiText.CreateClient();

    public EqlWikiMobService(string cacheDir, Func<string, Task<WikiPageText?>>? fetchOverride = null,
        Func<string, Task<List<string>>>? searchOverride = null)
    {
        _cacheDir = cacheDir;
        _fetch = fetchOverride ?? FetchFromApi;
        _search = searchOverride ?? SearchFromApi;
    }

    public async Task<MobLookupResult> LookupAsync(string creatureName, string currentZone = "")
    {
        var title = creatureName.Trim();
        if (title.Length == 0) return new MobLookupResult(null, ItemLookupState.NotFound, null);

        var cached = ReadCache(title);
        if (cached is { } fresh && DateTime.UtcNow - fresh.FetchedAt < CacheLifetime)
            return new MobLookupResult(Parse(fresh.Wikitext, fresh.Title), ItemLookupState.Cached, fresh.FetchedAt);

        foreach (var candidate in Candidates(title))
        {
            WikiPageText? page;
            try { page = await _fetch(candidate).ConfigureAwait(false); }
            catch
            {
                return cached is { } stale
                    ? new MobLookupResult(Parse(stale.Wikitext, stale.Title), ItemLookupState.StaleCache, stale.FetchedAt)
                    : new MobLookupResult(null, ItemLookupState.Offline, null);
            }
            if (page is null) continue;
            // page.Title, never `candidate`: the API follows redirects, so asking for the
            // article-stripped "Spiroc Lord" succeeds and hands back "The Spiroc Lord".
            // Recording the request is what put stripped names into contribution packs
            // even though the lookup had resolved perfectly (#65, Frankthetankk).
            WriteCache(title, page.Title, page.Wikitext);
            return new MobLookupResult(Parse(page.Wikitext, page.Title), ItemLookupState.Live, DateTime.UtcNow);
        }

        // Every exact form missed: ask the wiki's own search, and accept its top hits
        // only under the spawn catalog's fuzzy rule (bounded edit distance on folded
        // names, exact-first doctrine) — typos and punctuation drift resolve, but a
        // vaguely-related page can never masquerade as the creature (David, 2026-08-06).
        //
        // The compare runs on the title with any "(Zone)" suffix stripped: common mobs
        // live at zone-disambiguated pages ("Orc Legionnaire (Crushbone)"), and the
        // bare-name page can be a broken redirect left over from the split — David hit
        // exactly that mid-fight, 2026-08-07. When several zone pages match, the
        // player's current zone picks; zoneless pages come before foreign-zone ones.
        try
        {
            var hits = (await _search(title).ConfigureAwait(false)).Take(5)
                .Where(found => SpawnCatalog.NameMatchesFuzzy(StripZoneSuffix(found), title))
                .OrderBy(found => ZoneSuffix(found) is { } zone
                    ? zone.Equals(currentZone.Trim(), StringComparison.OrdinalIgnoreCase) ? 0 : 2
                    : 1);
            foreach (var found in hits)
            {
                var page = await _fetch(found).ConfigureAwait(false);
                if (page is null) continue;
                WriteCache(title, page.Title, page.Wikitext);
                return new MobLookupResult(Parse(page.Wikitext, page.Title), ItemLookupState.Live, DateTime.UtcNow);
            }
        }
        catch
        {
            return cached is { } stale
                ? new MobLookupResult(Parse(stale.Wikitext, stale.Title), ItemLookupState.StaleCache, stale.FetchedAt)
                : new MobLookupResult(null, ItemLookupState.Offline, null);
        }
        return new MobLookupResult(null, ItemLookupState.NotFound, null);
    }

    private static string StripZoneSuffix(string title) =>
        System.Text.RegularExpressions.Regex.Replace(title, @"\s*\([^)]*\)$", "");

    /// <summary>The "(Zone)" disambiguation suffix, or null when the title has none.</summary>
    private static string? ZoneSuffix(string title)
    {
        var m = System.Text.RegularExpressions.Regex.Match(title, @"\(([^)]*)\)$");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static async Task<List<string>> SearchFromApi(string query)
    {
        var url = "https://eqlwiki.com/api.php?action=query&list=search&srlimit=5&format=json&srsearch="
            + Uri.EscapeDataString(query);
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url).ConfigureAwait(false));
        var results = new List<string>();
        foreach (var hit in doc.RootElement.GetProperty("query").GetProperty("search").EnumerateArray())
            if (hit.GetProperty("title").GetString() is { Length: > 0 } t)
                results.Add(t);
        return results;
    }

    /// <summary>SessionStats mob names arrive article-stripped and first-letter-capitalized
    /// ("Spite golem"), but MediaWiki titles are case-sensitive past their FIRST letter —
    /// "A Spite golem" misses while both "A spite golem" and "A Spite Golem" exist. So:
    /// bare name (named mobs live there), lowercase article form (the wiki's redirect
    /// habit), title-case forms, then backtick-stripped variants (wikis drop EQ backticks —
    /// the Skeleton L`rodd lesson from the spawn catalog). Deduped, order preserved.</summary>
    private static IEnumerable<string> Candidates(string title)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in Raw(title))
            if (t.Length > 0 && seen.Add(t)) yield return t;

        static IEnumerable<string> Raw(string title)
        {
            var lower = title.ToLowerInvariant();
            var titleCase = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower);
            yield return title;
            yield return "A " + lower;
            if ("aeiou".Contains(lower[0])) yield return "An " + lower;
            // Normalize strips EVERY leading article, including "The" — but named mobs
            // often live at "The <Name>" pages ("The Prophet" in Crushbone showed no
            // drops, David 2026-08-06; bare "Prophet" is missing on the wiki).
            yield return "The " + lower;
            yield return titleCase;
            yield return "A " + titleCase;
            yield return "The " + titleCase;
            // "Innoruuk, the Prince of Hate" is filed as "Innoruuk (God)" — EQ gives gods
            // and bosses an epithet the wiki drops. Trying the base name lets the wiki's
            // own redirect do the rest, instead of EQBuddy proposing a duplicate page for
            // a boss it already documents (#65, Frankthetankk).
            var comma = title.IndexOf(", the ", StringComparison.OrdinalIgnoreCase);
            if (comma > 0)
            {
                var baseName = title[..comma].Trim();
                yield return baseName;
                yield return System.Globalization.CultureInfo.InvariantCulture.TextInfo
                    .ToTitleCase(baseName.ToLowerInvariant());
            }
            var noBacktick = title.Replace("`", "");
            if (noBacktick != title)
            {
                yield return noBacktick;
                yield return "A " + noBacktick.ToLowerInvariant();
            }
        }
    }

    private async Task<WikiPageText?> FetchFromApi(string title)
    {
        var url = "https://eqlwiki.com/api.php?action=query&prop=revisions&rvprop=content&format=json&redirects=1&titles="
            + Uri.EscapeDataString(title);
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url).ConfigureAwait(false));
        var pages = doc.RootElement.GetProperty("query").GetProperty("pages");
        foreach (var page in pages.EnumerateObject())
        {
            if (page.Value.TryGetProperty("missing", out _)) return null;
            if (page.Value.TryGetProperty("revisions", out var revs) && revs.GetArrayLength() > 0
                && revs[0].TryGetProperty("*", out var content))
            {
                // redirects=1 means this title is the page we LANDED on, which is not
                // necessarily the one we asked for.
                var served = page.Value.TryGetProperty("title", out var t) ? t.GetString() : null;
                return new WikiPageText(
                    served is { Length: > 0 } ? served : title, content.GetString() ?? "");
            }
        }
        return null;
    }

    // ---- cache (same shape as the item service) ----

    private sealed record CacheEntry(string Title, string Wikitext, DateTime FetchedAt);

    private string CachePath(string title) =>
        Path.Combine(_cacheDir, string.Concat(title.Select(c =>
            char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_')) + ".json");

    private CacheEntry? ReadCache(string title)
    {
        try
        {
            var path = CachePath(title);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(path));
        }
        catch { return null; }
    }

    private void WriteCache(string title, string resolvedTitle, string wikitext)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            File.WriteAllText(CachePath(title),
                JsonSerializer.Serialize(new CacheEntry(resolvedTitle, wikitext, DateTime.UtcNow)));
        }
        catch { /* cache is a convenience */ }
    }

    // ---- parsing ----

    // {{:Lockjaw Hide Vest}} <span class='drare'>(Common)</span>
    [GeneratedRegex(@"\{\{:([^}|]+)\}\}\s*(?:<span[^>]*>\(([^)]+)\)</span>)?")]
    private static partial Regex DropRx();

    // First "-123.4, 567" style pair in a location field.
    [GeneratedRegex(@"(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)")]
    private static partial Regex LocPairRx();

    public static MobInfo Parse(string wikitext, string title)
    {
        var info = new MobInfo
        {
            Name = title,
            PageTitle = title,
            WikiUrl = "https://eqlwiki.com/" + Uri.EscapeDataString(title.Replace(' ', '_')),
        };
        var fields = EqlWikiText.TemplateFields(wikitext, "Namedmobpage");
        if (fields.TryGetValue("name", out var name) && name.Trim().Length > 0)
            info.Name = EqlWikiText.StripLinks(name);
        if (fields.TryGetValue("zone", out var zone))
            info.Zone = EqlWikiText.StripLinks(zone);
        if (fields.TryGetValue("level", out var level))
            info.Level = level.Trim();
        if (fields.TryGetValue("location", out var location))
        {
            info.Location = EqlWikiText.StripLinks(location).Trim();
            // Editors paste /loc output, so the first coordinate pair is (Y, X) in
            // the log's own order — good enough to pin a camp on the map (~).
            var m = LocPairRx().Match(info.Location);
            if (m.Success)
                info.LocYX = (double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                              double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
        }
        // Some pages fill common_loot instead of (or as well as) known_loot — read both,
        // deduped by item (the orc thaumaturgist hunt, David 2026-08-07, found the split).
        var dropSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fieldName in (string[])["known_loot", "common_loot"])
            if (fields.TryGetValue(fieldName, out var loot))
                foreach (Match m in DropRx().Matches(loot))
                    if (dropSeen.Add(m.Groups[1].Value.Trim()))
                        info.Drops.Add((m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        return info;
    }
}
