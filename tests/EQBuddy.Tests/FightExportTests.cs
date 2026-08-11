using EQBuddy.Core;
using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>The Discord-ready single-fight export (#89): a monospace code block with
/// aligned columns, pet damage labeled into the same table, and no other player's
/// numbers anywhere — share-only by design.</summary>
public class FightExportTests
{
    private static LastFightInfo Fight() => new(
        Name: "a pledge familiar", DurationSeconds: 42, DamageOut: 1000, DamageIn: 312,
        Healed: 128, Dps: 23.8, Hps: 3.0, Outcome: "Slain", InProgress: false,
        ByAbility:
        [
            new SourceDamage("Bolt of Flame", 2, 460),
            new SourceDamage("Pierce", 9, 340),
        ],
        HealsBySpell: [],
        ByIncoming: [new SourceDamage("a pledge familiar", 12, 312)])
    {
        PetAbilities = [new SourceDamage("Lifespike", 4, 200)],
    };

    [Fact]
    public void ExportIsACodeBlockWithHeaderRowsAndPercentages()
    {
        var text = FightExport.ToText(Fight(), "Kasobtik", "v1.55.0");

        Assert.StartsWith("```", text);
        Assert.EndsWith("```", text);
        Assert.Contains("Kasobtik vs a pledge familiar — 42s · Slain", text);
        Assert.Contains("Damage 1,000 (23.8 dps) · taken 312 · healed 128", text);
        Assert.Contains("Bolt of Flame", text);
        Assert.Contains("46%", text);            // 460 of 1000
        Assert.Contains("Pet · Lifespike", text); // pet rows join the table, labeled
        Assert.Contains("from my log only", text);
    }

    [Fact]
    public void RowsSortByDamageAndLongNamesTruncate()
    {
        var f = Fight() with
        {
            ByAbility =
            [
                new SourceDamage("A Preposterously Long Ability Name Indeed", 1, 10),
                new SourceDamage("Big", 1, 700),
            ],
        };
        var text = FightExport.ToText(f, "", "v1");
        var big = text.IndexOf("Big", StringComparison.Ordinal);
        var small = text.IndexOf("A Preposterously", StringComparison.Ordinal);
        Assert.True(big >= 0 && small > big, "rows must sort by damage, descending");
        Assert.Contains("…", text);   // the long name is clipped, not wrapped
        Assert.Contains("You vs", text);   // no character name known — still reads
    }
}
