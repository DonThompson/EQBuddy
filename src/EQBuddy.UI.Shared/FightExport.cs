using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>
/// One fight as paste-ready text (#89, jeremycranfill): the official Discord blocks
/// image sharing, so the parse goes as a monospace code block instead — aligned
/// columns, percentages, one screen tall. Deliberately share-only: no upload, no
/// leaderboard, and only YOUR numbers, per the house rule that EQBuddy never
/// measures other players.
/// </summary>
public static class FightExport
{
    public static string ToText(LastFightInfo f, string character, string version)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("```");
        var who = character.Length > 0 ? character : "You";
        sb.AppendLine($"{who} vs {f.Name} — {f.DurationSeconds:0}s · {f.Outcome}");
        sb.Append($"Damage {f.DamageOut:N0} ({f.Dps:0.#} dps)");
        if (f.DamageIn > 0) sb.Append($" · taken {f.DamageIn:N0}");
        if (f.Healed > 0) sb.Append($" · healed {f.Healed:N0} ({f.Hps:0.#} hps)");
        sb.AppendLine();

        // Your rows and the pet's, one table: the pet's damage IS your damage
        // (the same accounting every card uses), labeled so nobody wonders.
        var rows = f.ByAbility
            .Concat(f.PetAbilities.Select(p => p with { Name = $"Pet · {p.Name}" }))
            .OrderByDescending(r => r.Total)
            .ToList();
        if (rows.Count > 0 && f.DamageOut > 0)
        {
            var nameWidth = Math.Min(28, rows.Max(r => r.Name.Length));
            foreach (var r in rows)
            {
                var name = r.Name.Length > nameWidth ? r.Name[..(nameWidth - 1)] + "…" : r.Name;
                sb.AppendLine(
                    $"  {name.PadRight(nameWidth)} {r.Total,8:N0}  x{r.Hits,-4} {100.0 * r.Total / f.DamageOut,5:0.#}%");
            }
        }
        sb.AppendLine($"(EQBuddy {version} — from my log only)");
        sb.Append("```");
        return sb.ToString();
    }
}
