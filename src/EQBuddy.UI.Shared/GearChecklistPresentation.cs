using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>Framework-neutral display rules for the imported Gear-card checklist.
/// WPF and Avalonia create their own controls from these groups; neither UI gets to
/// decide which rows are gear versus socketed exaltations or how their text reads.</summary>
public static class GearChecklistPresentation
{
    public sealed record Group(string Heading, IReadOnlyList<GearChecklistItem> Items);
    public sealed record ItemText(string Name, string EffectSuffix);

    public static IReadOnlyList<Group> BuildGroups(IReadOnlyList<GearChecklistItem> items)
    {
        var groups = new List<Group>(2);
        AddGroup(groups, "Gear", items.Where(item => !item.IsExaltation));
        AddGroup(groups, "Exaltations", items.Where(item => item.IsExaltation));
        return groups;
    }

    public static string ListName(string checklistName, IReadOnlyCollection<GearChecklistItem> items)
    {
        var done = items.Count(item => item.Acquired);
        return checklistName.Length > 0
            ? $"{checklistName} - {done}/{items.Count}"
            : $"{done}/{items.Count} imported gear pieces";
    }

    public static ItemText TextFor(GearChecklistItem item) => new(
        item.Item,
        item.IsExaltation && item.ExaltationEffect.Length > 0
            ? $" ({item.ExaltationEffect})"
            : "");

    public static string ItemLabel(GearChecklistItem item)
    {
        var text = TextFor(item);
        return text.Name + text.EffectSuffix;
    }

    public static string Tooltip(GearChecklistItem item)
    {
        var text = $"{item.Slot}: {ItemLabel(item)}";
        if (item.Source.Length > 0) text += "\n" + item.Source;
        if (item.Url.Length > 0) text += "\n" + item.Url;
        return text;
    }

    private static void AddGroup(List<Group> groups, string heading, IEnumerable<GearChecklistItem> items)
    {
        var rows = items.ToList();
        if (rows.Count > 0) groups.Add(new Group(heading, rows));
    }
}
