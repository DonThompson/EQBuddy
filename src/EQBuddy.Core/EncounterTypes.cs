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
