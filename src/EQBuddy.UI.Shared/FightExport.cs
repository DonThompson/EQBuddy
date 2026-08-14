using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>
/// One fight as paste-ready text (#89, jeremycranfill): the official Discord blocks
/// image sharing, so the parse goes as a monospace code block instead — aligned
/// columns, percentages, one screen tall. Deliberately share-only: no upload, no
/// leaderboard, and only YOUR numbers, per the house rule that EQBuddy never
/// measures other players (incoming rows name creatures, never players).
/// </summary>
public static class FightExport
{
    // Discord: mobile wraps a code block near 60 monospace columns, and a message
    // caps at 2000 chars. The row caps bound the whole block well under both —
    // worst case (every section full, every line at width) lands around 1,600.
    private const int MaxLine = 60;
    private const int MaxAbilityRows = 10;
    private const int MaxIncomingRows = 5;
    private const int MaxHealRows = 4;

    public static string ToText(LastFightInfo f, string character, string version,
        IReadOnlyList<string>? deaths = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("```");
        var who = character.Length > 0 ? character : "You";
        var tail = $" — {f.DurationSeconds:0}s · {f.Outcome}";
        // Clip the ASSEMBLED line, not just the name: a multi-fight pull's outcome
        // tail ("X Timeout · Y Timeout") is itself unbounded and was sailing past
        // the column budget on its own.
        sb.AppendLine(Clip(
            $"{who} vs {Clip(f.Name, Math.Max(2, MaxLine - who.Length - 4 - tail.Length))}{tail}",
            MaxLine));

        var dmg = $"Damage {f.DamageOut:N0} ({f.Dps:0.#} dps)";
        if (f.DamageIn > 0) dmg += $" · taken {f.DamageIn:N0}";
        var healed = f.Healed > 0 ? $"healed {f.Healed:N0} ({f.Hps:0.#} hps)" : null;
        // One stat line when it fits; healing drops to its own line rather than
        // letting Discord's mobile wrap break it mid-number.
        if (healed is not null && dmg.Length + 3 + healed.Length <= MaxLine)
            sb.AppendLine($"{dmg} · {healed}");
        else
        {
            sb.AppendLine(dmg);
            if (healed is not null) sb.AppendLine("H" + healed[1..]);
        }

        // Your rows and the pet's, one table: the pet's damage IS your damage
        // (the same accounting every card uses), labeled so nobody wonders.
        var rows = f.ByAbility
            .Concat(f.PetAbilities.Select(p => p with { Name = $"Pet · {p.Name}" }))
            .OrderByDescending(r => r.Total)
            .ToList();
        if (rows.Count > 0 && f.DamageOut > 0)
            AppendRows(sb, rows, f.DamageOut, MaxAbilityRows);
        if (f.ByIncoming.Count > 0 && f.DamageIn > 0)
        {
            sb.AppendLine("Taken:");
            AppendRows(sb, f.ByIncoming.OrderByDescending(r => r.Total).ToList(),
                f.DamageIn, MaxIncomingRows);
        }
        if (f.HealsBySpell.Count > 0 && f.Healed > 0)
        {
            sb.AppendLine("Heals:");
            AppendRows(sb, f.HealsBySpell.OrderByDescending(r => r.Total).ToList(),
                f.Healed, MaxHealRows);
        }
        if (deaths is { Count: > 0 })
        {
            var names = string.Join(", ", deaths.Distinct(StringComparer.OrdinalIgnoreCase));
            sb.AppendLine(Clip(deaths.Count == 1
                ? $"You died — {names}"
                : $"You died ×{deaths.Count} — {names}", MaxLine));
        }
        sb.AppendLine($"(EQBuddy {version} — from my log only)");
        sb.Append("```");
        return sb.ToString();
    }

    /// <summary>The Session History variant: a reviewed pull instead of the live
    /// card's fight. Outcome derives from the per-creature fights the same way the
    /// live card's does (SessionStats.BuildLastFight), so both copies read alike.</summary>
    public static string ToText(PullInfo p, string character, string version,
        IReadOnlyList<string>? deaths = null)
    {
        var outcome = p.Fights.Any(f => f.Outcome == "Fighting") ? "Fighting"
            : p.Fights.All(f => f.Outcome == "Killed") ? "Killed"
            : p.Fights.Count == 1 ? p.Fights[0].Outcome
            : string.Join(" · ", p.Fights.Where(f => f.Outcome is not ("Killed" or "Fighting"))
                .Select(f => $"{f.Name} {f.Outcome}").Distinct());
        return ToText(new LastFightInfo(p.Title, p.DurationSeconds, p.DamageOut, p.DamageIn,
            p.Healed, p.Dps, p.Healed / Math.Max(1, p.DurationSeconds), outcome,
            InProgress: false, p.ByAbility, p.HealsBySpell, p.ByIncoming)
        { Fights = p.Fights, PetAbilities = p.PetAbilities, Start = p.Start },
            character, version, deaths);
    }

    /// <summary>Session deaths that fall inside the fight's window, as killer names.
    /// +3 s slack: the killing blow's log line can trail the last damage tick the
    /// duration was cut at. Start == default (pre-Start snapshots) claims none.</summary>
    public static List<string> DeathsDuring(
        DateTime start, double durationSeconds, IEnumerable<TimedDetail> deaths) =>
        start == default
            ? []
            : deaths.Where(d => d.Time >= start && d.Time <= start.AddSeconds(durationSeconds + 3))
                .Select(d => d.Text).ToList();

    private static void AppendRows(
        System.Text.StringBuilder sb, List<SourceDamage> rows, long total, int max)
    {
        var nameWidth = Math.Min(28, rows.Max(r => r.Name.Length));
        foreach (var r in rows.Take(max))
            sb.AppendLine(
                $"  {Clip(r.Name, nameWidth).PadRight(nameWidth)} {r.Total,8:N0}  x{r.Hits,-4} {Pct(r.Total, total),5:0.#}%");
        if (rows.Count > max)
        {
            var rest = rows.Skip(max).Sum(r => r.Total);
            sb.AppendLine(
                $"  {$"+{rows.Count - max} more".PadRight(nameWidth)} {rest,8:N0}  {"",-5} {Pct(rest, total),5:0.#}%");
        }
    }

    private static double Pct(long part, long total) => 100.0 * part / Math.Max(1, total);

    private static string Clip(string s, int max) =>
        s.Length <= max ? s : s[..(Math.Max(2, max) - 1)] + "…";
}
