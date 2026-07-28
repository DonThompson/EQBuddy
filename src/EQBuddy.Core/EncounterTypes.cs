namespace EQBuddy.Core;

/// <summary>A finalized fight against one creature (ENCOUNTER-*).</summary>
public sealed record EncounterInfo(
    string Name, DateTime Start, double DurationSeconds,
    long DamageOut, long DamageIn, double Dps, string Outcome);

public sealed record MobLoot(string Item, int Count, double? DropRatePct);

/// <summary>Per-creature farming aggregate (MOB-*). Drop rates are observed personal
/// rates — the kill denominator is always surfaced next to the percentage.</summary>
public sealed record MobSummary(
    string Name, int Kills, int Encounters, double AvgFightSeconds,
    double XpPercent, long Copper, List<MobLoot> Loot);

/// <summary>Combat time and damage while a stance was active (STANCE-*).</summary>
public sealed record StanceInfo(string Name, double CombatSeconds, long Damage, double Dps);

/// <summary>A spell observed hitting several creatures at once, measured per cast.
/// AvgTargets below MaxTargets means some casts caught fewer creatures than the best one —
/// the gap between them is how much AoE value is being left on the table by pull size.</summary>
public sealed record AreaSpellInfo(
    string Name, int Casts, double AvgTargets, int MaxTargets, long Damage, double DamagePerCast);
