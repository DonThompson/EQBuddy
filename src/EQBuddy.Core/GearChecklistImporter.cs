using System.Net;
using System.Text.RegularExpressions;

namespace EQBuddy.Core;

public sealed class GearChecklistImportResult
{
    public string Name { get; set; } = "";
    public List<GearChecklistItem> Items { get; set; } = [];
}

public static partial class GearChecklistImporter
{
    public static GearChecklistImportResult ImportFile(string path)
    {
        var html = File.ReadAllText(path);
        var result = ImportHtml(html);
        if (result.Name.Length == 0)
            result.Name = Path.GetFileNameWithoutExtension(path);
        return result;
    }

    public static GearChecklistImportResult ImportHtml(string html)
    {
        var result = new GearChecklistImportResult { Name = PageTitle(html) };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match slot in SlotCardRegex().Matches(html))
        {
            var heading = Clean(slot.Groups["slot"].Value);
            var body = slot.Groups["body"].Value;
            if (heading.Length == 0) continue;

            // The export nests a "Socketed Exaltations" block inside the same slot
            // card, and each socketed entry carries its own item-link and source
            // lines. The equipped item's entry always precedes that block, so cut
            // the body there — otherwise an exaltation's drop sources get joined
            // into the gear piece's source text.
            var socketed = body.IndexOf("socketed-block", StringComparison.OrdinalIgnoreCase);
            if (socketed >= 0) body = body[..socketed];

            var itemMatch = ItemLinkRegex().Match(body);
            if (!itemMatch.Success) continue;

            var item = Clean(itemMatch.Groups["item"].Value);
            if (item.Length == 0) continue;

            var url = WebUtility.HtmlDecode(itemMatch.Groups["href"].Value).Trim();
            var source = string.Join(" | ", SourceLineRegex().Matches(body)
                .Select(m => Clean(m.Groups["source"].Value))
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase));
            var key = $"{heading}|{item}";
            if (!seen.Add(key)) continue;

            result.Items.Add(new GearChecklistItem
            {
                Slot = TitleSlot(heading),
                Item = item,
                Source = source,
                Url = url,
            });
        }

        return result;
    }

    private static string PageTitle(string html)
    {
        var title = TitleRegex().Match(html);
        if (!title.Success) return "";
        var clean = Clean(title.Groups["title"].Value);
        if (clean.EndsWith(" - EQ Legends Tools", StringComparison.OrdinalIgnoreCase))
            clean = clean[..^" - EQ Legends Tools".Length];
        return clean.EndsWith(" Shopping List", StringComparison.OrdinalIgnoreCase)
            ? clean[..^" Shopping List".Length]
            : clean;
    }

    private static string Clean(string value)
    {
        var noTags = TagRegex().Replace(value, " ");
        var decoded = WebUtility.HtmlDecode(noTags);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    private static string TitleSlot(string value)
    {
        if (value.All(c => !char.IsLetter(c) || char.IsUpper(c)))
            return string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Length <= 3 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
        return value;
    }

    [GeneratedRegex("<title[^>]*>(?<title>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("<div\\s+class=[\"']slot-heading[\"'][^>]*>(?<slot>.*?)</div>(?<body>.*?)(?=<div\\s+class=[\"']slot-heading[\"']|</body>|</html>|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SlotCardRegex();

    [GeneratedRegex("<a\\s+class=[\"']item-link[\"'][^>]*href=[\"'](?<href>[^\"']*)[\"'][^>]*>(?<item>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ItemLinkRegex();

    [GeneratedRegex("<div\\s+class=[\"']source-line[\"'][^>]*>(?<source>.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SourceLineRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();
}
