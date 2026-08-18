using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>
/// What the Kills card shows: the pace line, your kills, the per-creature farming block
/// and the group's kills (Gate 5b).
///
/// Framework-free, so it is tested without a window — which is the point of the exercise
/// as much as the tidiness is. The WPF layer has no test project (docs/TestPlan.md §5), so
/// every string a card computes inside the window is a string nothing can assert. These
/// were computed inline in <c>RefreshUi</c>, mixed in with the control assignments.
/// </summary>
public static class KillsPresentation
{
    /// <summary>The pace line under the header: kills per hour of session, per hour of
    /// ACTIVE time, and what the recent window saw. The two rates differ by exactly the
    /// downtime, which is the point of showing both.</summary>
    public static string Summary(StatsSnapshot s) =>
        $"{s.KillsPerHour:0.0} kills/hr · {s.KillsPerActiveHour:0.0} active"
        + (s.Recent is { } r ? $" · last {(int)r.Window.TotalMinutes}m: {r.Kills}" : "");

    /// <summary>Your own kills, by creature.</summary>
    public static List<CardRow> YourKills(StatsSnapshot s) =>
        [.. s.YourKills.Select(k => new CardRow(k.Name, $"×{k.Count}"))];

    /// <summary>The farming block: a creature, then its drops beneath it. A creature with
    /// no kills is not farming and does not appear — the card is about what you have
    /// actually been killing.</summary>
    public static List<CardRow> Farming(StatsSnapshot s)
    {
        var rows = new List<CardRow>();
        foreach (var mob in s.Mobs.Where(m => m.Kills > 0))
        {
            rows.Add(new CardRow(mob.Name,
                $"avg {mob.AvgFightSeconds:0}s · {StatsSnapshot.FormatCoin(mob.Copper)} · {mob.XpPercent:0.0}% xp"));
            foreach (var loot in mob.Loot)
                rows.Add(new CardRow(loot.Item,
                    // A drop rate is only honest per creature, which is why it lives here
                    // and not on the Loot card: that one mixes every source together.
                    loot.DropRatePct is { } pct ? $"×{loot.Count} · {pct:0}%" : $"×{loot.Count}",
                    Indent: true));
        }
        return rows;
    }

    /// <summary>What the rest of the group killed. Counts only — never a comparison, never
    /// a ranking: measuring other players is the one line this project does not cross
    /// (CLAUDE.md). "Who landed the killing blow" is bookkeeping about the camp, and it is
    /// deliberately not presented beside yours as a score.</summary>
    public static List<CardRow> PartyKills(StatsSnapshot s) =>
        [.. s.PartyKillsByKiller.Select(k => new CardRow(k.Name, $"×{k.Count}"))];

    /// <summary>The farming block is worth a heading only when it has rows.</summary>
    public static bool ShowFarming(StatsSnapshot s) => s.Mobs.Any(m => m.Kills > 0);

    public static bool ShowPartyKills(StatsSnapshot s) => s.PartyKillsByKiller.Count > 0;

    public const string FarmingLabel = "Farming (per creature)";
    public const string PartyKillsLabel = "Group kills";
}
