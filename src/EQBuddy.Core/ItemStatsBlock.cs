using System.Text.RegularExpressions;

namespace EQBuddy.Core;

/// <summary>
/// The wiki's in-game stats block ("Slot: PRIMARY", "AC: 10", "STR: +15 WIS: +15"),
/// parsed into numbers the Gear Locker can compare (#104). The block is what the
/// game shows in the item window, transcribed by wiki editors — so parsing is a
/// whitelist over observed shapes and anything unreadable simply stays null:
/// a Locker row honestly missing a number beats one wearing a guessed one.
/// </summary>
public sealed partial class ItemStatsBlock
{
    /// <summary>Equip slots exactly as the block prints them (PRIMARY, HEAD, EAR…).</summary>
    public List<string> Slots { get; init; } = [];
    public int? Ac { get; init; }
    public int? Dmg { get; init; }
    public int? Delay { get; init; }
    public int? Hp { get; init; }
    public int? Mana { get; init; }
    /// <summary>Named attributes and saves (STR/STA/…/SvFire), signed as printed.</summary>
    public Dictionary<string, int> Attributes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Empty = usable by all (the block prints "Class: ALL" or nothing).</summary>
    public List<string> Classes { get; init; } = [];
    public string Skill { get; init; } = "";
    public bool Magic { get; init; }
    public bool Lore { get; init; }
    public bool NoDrop { get; init; }

    /// <summary>DMG per point of delay — the honest one-number weapon comparison.</summary>
    public double? Ratio => Dmg is { } d && Delay is { } dl && dl > 0 ? (double)d / dl : null;

    /// <summary>True when the item goes in a worn slot at all — spell scrolls, quest
    /// pieces and containers have no Slot line and no place in the Locker.</summary>
    public bool Wearable => Slots.Count > 0;

    private static readonly HashSet<string> AttributeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "STR", "STA", "AGI", "DEX", "WIS", "INT", "CHA",
        "SV FIRE", "SV COLD", "SV MAGIC", "SV DISEASE", "SV POISON",
    };

    public static ItemStatsBlock Parse(IEnumerable<string> statsLines)
    {
        var slots = new List<string>();
        var classes = new List<string>();
        var attrs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int? ac = null, dmg = null, delay = null, hp = null, mana = null;
        var skill = "";
        bool magic = false, lore = false, noDrop = false;

        foreach (var line in statsLines)
        {
            magic |= line.Contains("MAGIC ITEM", StringComparison.OrdinalIgnoreCase);
            lore |= line.Contains("LORE ITEM", StringComparison.OrdinalIgnoreCase);
            noDrop |= line.Contains("NO DROP", StringComparison.OrdinalIgnoreCase)
                   || line.Contains("NO TRADE", StringComparison.OrdinalIgnoreCase);

            if (SlotRx().Match(line) is { Success: true } slotM)
                slots.AddRange(slotM.Groups[1].Value
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (ClassRx().Match(line) is { Success: true } classM)
            {
                var names = classM.Groups[1].Value
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!names.Any(c => c.Equals("ALL", StringComparison.OrdinalIgnoreCase)))
                    classes.AddRange(names);
            }
            if (SkillRx().Match(line) is { Success: true } skillM)
                skill = skillM.Groups[1].Value.Trim();

            foreach (Match m in PairRx().Matches(line))
            {
                var key = Regex.Replace(m.Groups["key"].Value.Trim(), @"\s+", " ").ToUpperInvariant();
                var value = int.Parse(m.Groups["val"].Value);
                switch (key)
                {
                    case "AC": ac = value; break;
                    case "DMG": dmg = value; break;
                    case "ATK DELAY": case "DELAY": delay = value; break;
                    case "HP": hp = value; break;
                    case "MANA": mana = value; break;
                    default:
                        if (AttributeKeys.Contains(key)) attrs[key] = value;
                        break;
                }
            }
        }

        return new ItemStatsBlock
        {
            Slots = slots, Classes = classes, Attributes = attrs,
            Ac = ac, Dmg = dmg, Delay = delay, Hp = hp, Mana = mana,
            Skill = skill, Magic = magic, Lore = lore, NoDrop = noDrop,
        };
    }

    // "Slot: PRIMARY SECONDARY" — the block's own word list.
    [GeneratedRegex(@"^Slot:\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SlotRx();
    // "Class: PAL WAR" / "Class: ALL"
    [GeneratedRegex(@"^Class(?:es)?:\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ClassRx();
    // "Skill: 1H Slashing Atk Delay: 26" — the skill runs until the Delay key or
    // the end of the line; a greedier cut ("1H") lied about two-word skills.
    [GeneratedRegex(@"Skill:\s*(.+?)(?=\s+(?:Atk\s+)?Delay:|\s*$)", RegexOptions.IgnoreCase)]
    private static partial Regex SkillRx();
    // "AC: 10" · "STR: +15" · "Atk Delay: 26" · "SV FIRE: +5" — several per line.
    [GeneratedRegex(@"(?<key>[A-Za-z][A-Za-z ]{0,9}?):\s*(?<val>[+-]?\d+)\b")]
    private static partial Regex PairRx();
}
