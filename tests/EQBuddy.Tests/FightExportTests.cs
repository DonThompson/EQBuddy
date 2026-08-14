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

    [Fact]
    public void IncomingHealsAndDeathsGetTheirOwnSections()
    {
        var f = Fight() with
        {
            HealsBySpell = [new SourceDamage("Chloroplast", 2, 128)],
        };
        var text = FightExport.ToText(f, "Kasobtik", "v1", deaths: ["a pledge familiar"]);

        var taken = text.IndexOf("Taken:", StringComparison.Ordinal);
        var heals = text.IndexOf("Heals:", StringComparison.Ordinal);
        Assert.True(taken >= 0 && heals > taken, "sections in order: damage, taken, heals");
        Assert.Contains("Chloroplast", text);
        Assert.Contains("You died — a pledge familiar", text);
    }

    [Fact]
    public void LongAbilityListsTruncateWithACount()
    {
        var f = Fight() with
        {
            ByAbility = [.. Enumerable.Range(1, 14).Select(i =>
                new SourceDamage($"Ability {i}", i, 100 * i))],
            PetAbilities = [],
        };
        var text = FightExport.ToText(f, "Kasobtik", "v1");
        Assert.Contains("+4 more", text);             // 14 rows, 10 shown
        Assert.DoesNotContain("Ability 4 ", text);    // the small rows are the ones folded
    }

    [Fact]
    public void EveryLineFitsDiscordMobileAndTheWholeBlockFitsOneMessage()
    {
        // Worst case on purpose: long names everywhere, every section overfull.
        var wide = Enumerable.Range(1, 30).Select(i =>
            new SourceDamage($"An Extremely Verbose Ability Name Number {i}", i * 111, 987654 + i))
            .ToList();
        var f = Fight() with
        {
            Name = "a horde of unspeakably long-named creatures + several more ×12",
            DamageOut = wide.Sum(r => r.Total),
            DamageIn = 1234567,
            Healed = 7654321,
            ByAbility = wide,
            ByIncoming = [.. wide.Select(r => r with { Name = $"attacker: {r.Name}" })],
            HealsBySpell = wide,
        };
        var text = FightExport.ToText(f, "Kasobtik", "v1.77.0",
            deaths: ["a very verbosely named creature", "another one", "a third"]);

        Assert.True(text.Length < 2000, $"must fit one Discord message, was {text.Length}");
        foreach (var line in text.Split('\n'))
            Assert.True(line.TrimEnd().Length <= 60,
                $"line wraps on Discord mobile ({line.TrimEnd().Length} chars): {line}");
    }

    [Fact]
    public void HistoryPullExportsThroughTheSameBuilder()
    {
        var start = new DateTime(2026, 8, 13, 20, 0, 0);
        var pull = EncounterGrouping.Group(
        [
            new EncounterInfo("a gnoll pup", start, 30, 900, 120, 30, "Killed")
            {
                ByAbility = [new SourceDamage("Pierce", 9, 900)],
                ByIncoming = [new SourceDamage("a gnoll pup", 4, 120)],
            },
        ])[0];
        var text = FightExport.ToText(pull, "Kasobtik", "v1");
        Assert.Contains("Kasobtik vs a gnoll pup — 30s · Killed", text);
        Assert.Contains("Pierce", text);
        Assert.StartsWith("```", text);
        Assert.EndsWith("```", text);
    }

    [Fact]
    public void DeathsDuringKeepsOnlyTheFightWindow()
    {
        var start = new DateTime(2026, 8, 13, 20, 0, 0);
        var deaths = new List<TimedDetail>
        {
            new(start.AddSeconds(-5), "an earlier mob"),      // before the pull
            new(start.AddSeconds(20), "a gnoll pup"),         // mid-fight
            new(start.AddSeconds(32), "a gnoll pup"),         // killing blow trailing the end
            new(start.AddMinutes(10), "a much later mob"),    // next pull's problem
        };
        Assert.Equal(new[] { "a gnoll pup", "a gnoll pup" },
            FightExport.DeathsDuring(start, 30, deaths));
        Assert.Empty(FightExport.DeathsDuring(default, 30, deaths));   // pre-Start snapshot
    }
}
