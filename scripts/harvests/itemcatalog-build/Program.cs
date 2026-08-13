// Builds src/EQBuddy.Core/Data/ItemCatalog.json.gz from the items-harvest dump.
//
// The whole point of this being C# (and not more python in items-harvest.py): the
// dump flows through THE APP'S OWN parsers — EqlWikiItemService.Parse for the
// {{Itempage}} template, ItemStatsBlock.Parse for the stats block — so the catalog
// can never disagree with what a live lookup of the same page would show.

using System.IO.Compression;
using System.Text.Json;
using EQBuddy.Core;

var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
var dump = Path.Combine(repo, "scripts", "harvests", "eqlwiki", "cache", "items-wikitext.jsonl");
var outPath = Path.Combine(repo, "src", "EQBuddy.Core", "Data", "ItemCatalog.json.gz");
var reportPath = Path.Combine(repo, "scripts", "harvests", "eqlwiki", "items-catalog-report.md");

var records = new List<ItemCatalog.Record>();
int parsed = 0, noTemplate = 0, statless = 0;
var examples = new List<string>();

foreach (var line in File.ReadLines(dump))
{
    if (line.Trim().Length == 0) continue;
    using var doc = JsonDocument.Parse(line);
    var title = doc.RootElement.GetProperty("title").GetString()!;
    var wikitext = doc.RootElement.GetProperty("wikitext").GetString()!;

    var info = EqlWikiItemService.Parse(wikitext, title);
    if (info.StatsLines.Count == 0 && info.Quests.Count == 0 && info.DropsFrom.Count == 0)
    {
        noTemplate++;
        if (examples.Count < 30) examples.Add(title);
        continue;
    }
    var stats = ItemStatsBlock.Parse(info.StatsLines);
    if (info.StatsLines.Count == 0) statless++;
    parsed++;

    records.Add(new ItemCatalog.Record
    {
        Name = info.Name,
        StatsText = info.StatsLines.Count > 0 ? string.Join("\n", info.StatsLines) : "",
        Slots = stats.Slots,
        Ac = stats.Ac, Dmg = stats.Dmg, Delay = stats.Delay, Hp = stats.Hp, Mana = stats.Mana,
        Attributes = stats.Attributes.Count > 0 ? stats.Attributes : null,
        Classes = stats.Classes.Count > 0 ? stats.Classes : null,
        Skill = stats.Skill,
        QuestFlagged = info.QuestFlagged,
        Quests = info.Quests.Count > 0 ? info.Quests : null,
        Recipes = info.Recipes.Count > 0 ? info.Recipes : null,
        DropZones = info.DropsFrom.Count > 0
            ? info.DropsFrom.Select(d => d.Zone).Where(z => z.Length > 0).Distinct().ToList()
            : null,
    });
}

var payload = JsonSerializer.SerializeToUtf8Bytes(
    new ItemCatalog.Root { Items = records },
    new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
using (var fs = File.Create(outPath))
using (var gz = new GZipStream(fs, CompressionLevel.SmallestSize))
    gz.Write(payload);

File.WriteAllText(reportPath,
    $"# item catalog build report\n\n" +
    $"- dump entries parsed into the catalog: {parsed}\n" +
    $"- pages with no Itempage content (skipped): {noTemplate}\n" +
    $"- catalog items without a stats block (knowledge-only): {statless}\n" +
    $"- raw payload: {payload.Length / 1024} KB; gz: {new FileInfo(outPath).Length / 1024} KB\n\n" +
    "Skipped examples:\n" + string.Join("\n", examples.Select(e => $"  - {e}")) + "\n");

Console.WriteLine($"{parsed} items -> {outPath} ({new FileInfo(outPath).Length / 1024} KB gz); " +
    $"{noTemplate} skipped (no template), {statless} statless. Report: {reportPath}");
