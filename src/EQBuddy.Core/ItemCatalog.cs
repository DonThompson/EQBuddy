using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace EQBuddy.Core;

/// <summary>
/// The embedded item catalog: every eqlwiki item page (Category:Items, ~11k),
/// harvested weekly and parsed at build time through the SAME parsers the live
/// lookup uses (items-harvest.py fetches, itemcatalog-build emits — the catalog
/// can never disagree with a live fetch of the same revision). This is the
/// instant/offline layer under the wiki cache: a live page, when one has been
/// fetched, still outranks it — the catalog answers when nothing fresher exists.
///
/// Closes the item-knowledge gap (2026-08-13): stats on hover, "what is this
/// item for" (quests, recipes, drop zones), and a Gear Locker that fills in
/// instantly instead of fetch-on-first-open.
/// </summary>
public sealed class ItemCatalog
{
    public sealed class Record
    {
        public string Name { get; set; } = "";
        /// <summary>The in-game stats block, newline-joined — display verbatim,
        /// same as a live page's tooltip. "" for knowledge-only entries.</summary>
        public string StatsText { get; set; } = "";
        public List<string> Slots { get; set; } = [];
        public int? Ac { get; set; }
        public int? Dmg { get; set; }
        public int? Delay { get; set; }
        public int? Hp { get; set; }
        public int? Mana { get; set; }
        public Dictionary<string, int>? Attributes { get; set; }
        public List<string>? Classes { get; set; }
        public string Skill { get; set; } = "";
        public bool QuestFlagged { get; set; }
        public List<string>? Quests { get; set; }
        public List<string>? Recipes { get; set; }
        public List<string>? DropZones { get; set; }

        /// <summary>The structured stats in the shape the Gear Locker compares.</summary>
        public ItemStatsBlock ToStatsBlock() => new()
        {
            Slots = Slots,
            Ac = Ac, Dmg = Dmg, Delay = Delay, Hp = Hp, Mana = Mana,
            Attributes = Attributes is null
                ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(Attributes, StringComparer.OrdinalIgnoreCase),
            Classes = Classes ?? [],
            Skill = Skill,
        };
    }

    public sealed class Root { public List<Record> Items { get; set; } = []; }

    private readonly Dictionary<string, Record> _byName;

    public int Count => _byName.Count;

    /// <summary>Every record, in no particular order. Read-only, and here for the sweeps
    /// that have to hold against the SHIPPED catalog rather than a sample — the reward
    /// silhouette mapper (IconPaths.ForItem) is checked against all 11k.</summary>
    public IEnumerable<Record> All => _byName.Values;

    public ItemCatalog(IEnumerable<Record> records)
    {
        _byName = new Dictionary<string, Record>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records)
            _byName[Fold(r.Name)] = r;
    }

    /// <summary>Lookup by in-game name: "+N" upgrade suffixes fold to the base the
    /// wiki titles, and backticks fold both ways (wikis are inconsistent about
    /// EQ's backtick names — the same tolerance every catalog here applies).</summary>
    public Record? Find(string inGameName)
    {
        var title = EqlWikiItemService.NormalizeTitle(inGameName);
        return _byName.TryGetValue(Fold(title), out var r) ? r : null;
    }

    private static string Fold(string name) => name.Replace("`", "'").Trim();

    public static ItemCatalog LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("EQBuddy.Core.Data.ItemCatalog.json.gz")
            ?? throw new InvalidOperationException("ItemCatalog.json.gz missing from resources");
        using var gz = new GZipStream(stream, CompressionMode.Decompress);
        var root = JsonSerializer.Deserialize<Root>(gz)
            ?? throw new InvalidOperationException("ItemCatalog.json.gz unreadable");
        return new ItemCatalog(root.Items);
    }

    private static ItemCatalog? _default;
    private static readonly object DefaultLock = new();

    /// <summary>Lazy: ~1 MB gunzip + parse happens on first use, not app start.</summary>
    public static ItemCatalog Default
    {
        get
        {
            if (_default is { } d) return d;
            lock (DefaultLock) return _default ??= LoadEmbedded();
        }
    }
}
