using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>One worked example: what to put in each box, and what you get for it.</summary>
public sealed record WatchGuideExample(
    WatchKind Kind, string Name, string Match, string Delay, string What);

/// <summary>
/// The in-app guide to setting up watch rules, shown by the "Show examples" toggle in
/// Options. Lives here rather than in either UI so both show the same thing, and so
/// <c>WatchGuideTests</c> can check the examples still describe rules the app can actually
/// build — an example naming a kind that no longer exists is worse than no example.
///
/// Written because a user asked whether "Mote" alone was enough or whether the full item
/// name was needed (discussion #24). That question has an obvious answer once you know match
/// text is a substring, and no answer at all if you don't.
/// </summary>
public static class WatchGuide
{
    /// <summary>The handful of rules that explain most confusion, in the order they tend to
    /// bite. Kept to single sentences — this is a panel in a widget, not a manual.</summary>
    public static readonly string[] Basics =
    [
        "Match text is a case-insensitive substring, not a whole name and not a regex — \"mote\" catches Mote of Minor Potential and every other tier.",
        "Leave Match empty and the rule's Name is used instead, so a rule just called \"Ghoul\" works.",
        "Kind decides what the text is matched against. A Loot pattern will never match if Kind is left on Kill.",
        "Death, Milestone, and class-filtered Spell fade rules need no match text at all — empty means \"all of them\".",
        "Log text is the exception: it matches raw log lines, and an empty pattern matches nothing rather than everything.",
        "Delay holds the alert back, turning a rule into a cue. Seconds by default; add m for minutes (8m), up to 30 minutes.",
        "For an immediate and a delayed alert on the same trigger, make two rules with the same Match and different sounds.",
        "The card shows the current session only, and a session ends after 60 minutes of no log activity.",
    ];

    public static readonly WatchGuideExample[] Examples =
    [
        new(WatchKind.Loot, "Motes", "mote", "",
            "Every mote tier. If nothing appears, check whether the game is routing them to currency storage — then the log says nothing to match."),
        new(WatchKind.Loot, "Crushbone gear", "Crushbone", "",
            "Any item with Crushbone in the name, however it was looted."),
        new(WatchKind.Kill, "Taskmasters", "taskmaster", "",
            "Kills by you or your pet whose name contains it."),
        new(WatchKind.Kill, "Respawn", "placeholder", "8m",
            "A camp timer: kill the placeholder, get told 8 minutes later. Delay works on any kind, not just Log text, and a timer this long survives your death — dying doesn't change when a mob pops."),
        new(WatchKind.SkillUp, "Smithing", "Blacksmithing", "",
            "Fires when that skill goes up — useful while grinding a trade skill. Match the skill's name, not what you made."),
        new(WatchKind.Death, "My deaths", "", "",
            "Every death, with whatever killed you. Put a name in Match to watch one killer."),
        new(WatchKind.Milestone, "Levels & AA", "", "",
            "Level-ups and ability points. Match text is ignored."),
        new(WatchKind.SpellFade, "Charm broke", "", "",
            "Set the class picker to Charm — no match text, and it keeps working as you level into new charm spells."),
        new(WatchKind.SpellFade, "HoT dropped", "", "",
            "Set the class picker to HoT — fires when a heal-over-time wears off, so you know to recast. Learned from the ticks themselves, so it covers spells EQBuddy has never seen before."),
        new(WatchKind.Text, "CH call heard", "CH -->", "",
            "Any log line containing the text, including lines EQBuddy doesn't otherwise understand — another player's raid-assist script, a server's custom emotes."),
        new(WatchKind.Text, "CAST NOW", "CH -->", "2.5",
            "The same trigger, 2.5 s later. Pair it with the rule above, give it a louder sound, and you get \"heard it\" then \"do it now\"."),
        new(WatchKind.Text, "Recast Poison Bolt", "You begin casting Poison Bolt", "18",
            "A recast reminder 18 s after you cast. The seconds are yours to pick — the log never says how long a spell lasts. Fires even if the cast fizzles."),
    ];
}
