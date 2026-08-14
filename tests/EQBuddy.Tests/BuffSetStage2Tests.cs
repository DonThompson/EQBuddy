using System.Text.Json;
using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// Buff sets stage 2 (#120, Frankthetankk — his committed design): per-class storage
/// assembled by the active class combination. The core promise under test: swap one
/// class and the OTHER classes' picks survive; the "(any class)" bucket — where every
/// stage-1 set migrates, losslessly — is always part of the assembled set.
/// </summary>
public class BuffSetStage2Tests
{
    private static readonly DateTime T0 = DateTime.Parse("2026-08-14T21:00:00");

    // ---- migration: flat stage-1 sets → the "(any class)" bucket ----

    [Fact]
    public void MigrationMovesFlatSetsIntoTheAnyClassBucket()
    {
        var settings = new AppSettings();
        settings.BuffSets["dranak_legends"] = ["Temperance", "Aegolism"];
        settings.BuffSets["pipsqueak_legends"] = ["Spirit of Wolf"];

        Assert.True(settings.MigrateBuffSetsToClassBuckets());

        Assert.Empty(settings.BuffSets);   // emptied — the pass can never run twice
        Assert.Equal(["Temperance", "Aegolism"],
            BuffSetStore.SpellsFor(settings.BuffSetsByClass["dranak_legends"], BuffSetStore.AnyClass));
        Assert.Equal(["Spirit of Wolf"],
            BuffSetStore.SpellsFor(settings.BuffSetsByClass["pipsqueak_legends"], BuffSetStore.AnyClass));
    }

    [Fact]
    public void MigrationIsIdempotent_AndAFreshInstallIsANoOp()
    {
        var settings = new AppSettings();
        settings.BuffSets["dranak_legends"] = ["Temperance"];
        Assert.True(settings.MigrateBuffSetsToClassBuckets());
        Assert.False(settings.MigrateBuffSetsToClassBuckets());   // second run: nothing to do
        Assert.False(new AppSettings().MigrateBuffSetsToClassBuckets());
        Assert.Equal(["Temperance"],
            BuffSetStore.SpellsFor(settings.BuffSetsByClass["dranak_legends"], BuffSetStore.AnyClass));
    }

    [Fact]
    public void MigrationMergesWithoutDroppingWhatIsAlreadyInTheBucket()
    {
        // A stage-2 build configured picks, then an old flat file resurfaced (settings
        // copied between machines). Nothing anyone configured may be lost.
        var settings = new AppSettings();
        BuffSetStore.Add(settings.BuffSetsByClass, "dranak_legends", BuffSetStore.AnyClass, "Aegolism");
        settings.BuffSets["dranak_legends"] = ["Temperance", "Aegolism"];

        Assert.True(settings.MigrateBuffSetsToClassBuckets());

        Assert.Equal(["Aegolism", "Temperance"],
            BuffSetStore.SpellsFor(settings.BuffSetsByClass["dranak_legends"], BuffSetStore.AnyClass));
    }

    // ---- assembly by class combination: the requester's core scenario ----

    private static Dictionary<string, Dictionary<string, List<string>>> ThreeClassSetup()
    {
        var byClass = new Dictionary<string, Dictionary<string, List<string>>>();
        BuffSetStore.Add(byClass, "dranak_legends", BuffSetStore.AnyClass, "Spirit of Wolf");
        BuffSetStore.Add(byClass, "dranak_legends", "Cleric", "Temperance");
        BuffSetStore.Add(byClass, "dranak_legends", "Enchanter", "Clarity");
        BuffSetStore.Add(byClass, "dranak_legends", "Warrior", "Resolution");
        return byClass;
    }

    [Fact]
    public void SwapWarriorForRogue_TheOtherClassesPicksSurvive()
    {
        var byClass = ThreeClassSetup();

        var before = BuffSetStore.Assemble(byClass["dranak_legends"], ["Cleric", "Enchanter", "Warrior"]);
        Assert.Equal(["Spirit of Wolf", "Temperance", "Clarity", "Resolution"], before);

        // The swap: Warrior out, Rogue in. Cleric's and Enchanter's picks keep riding;
        // Warrior's wait in storage, untouched, for the swap back.
        var after = BuffSetStore.Assemble(byClass["dranak_legends"], ["Cleric", "Enchanter", "Rogue"]);
        Assert.Equal(["Spirit of Wolf", "Temperance", "Clarity"], after);
        Assert.Equal(["Resolution"], BuffSetStore.SpellsFor(byClass["dranak_legends"], "Warrior"));

        var back = BuffSetStore.Assemble(byClass["dranak_legends"], ["Cleric", "Enchanter", "Warrior"]);
        Assert.Equal(before, back);
    }

    [Fact]
    public void TheAnyClassBucketIsAlwaysIncluded()
    {
        var byClass = ThreeClassSetup();
        // Even with NO classes known (fresh character, nothing picked or inferred).
        Assert.Equal(["Spirit of Wolf"], BuffSetStore.Assemble(byClass["dranak_legends"], []));
        // And with any combination, first in order.
        Assert.Equal("Spirit of Wolf",
            BuffSetStore.Assemble(byClass["dranak_legends"], ["Warrior"]).First());
    }

    [Fact]
    public void SectionsPutAnyClassFirst_AndKeepEmptyActiveSectionsVisible()
    {
        var byClass = ThreeClassSetup();
        var sections = BuffSetStore.Sections(byClass["dranak_legends"], ["Rogue", "Cleric"]);
        Assert.Equal([BuffSetStore.AnyClass, "Rogue", "Cleric"], sections.Select(s => s.Class));
        // The freshly swapped-in class has no picks yet — its section still shows,
        // because it is where the editors add.
        Assert.Empty(sections.Single(s => s.Class == "Rogue").Spells);
        Assert.Equal(["Temperance"], sections.Single(s => s.Class == "Cleric").Spells);
    }

    [Fact]
    public void AssemblyDedupsTheSameBuffPickedUnderTwoClasses()
    {
        var byClass = new Dictionary<string, Dictionary<string, List<string>>>();
        BuffSetStore.Add(byClass, "c", BuffSetStore.AnyClass, "Temperance");
        BuffSetStore.Add(byClass, "c", "Cleric", "temperance");   // case-insensitive identity
        Assert.Equal(["Temperance"], BuffSetStore.Assemble(byClass["c"], ["Cleric"]));
    }

    [Fact]
    public void NoStoredSetsAssembleToNothing()
    {
        Assert.Empty(BuffSetStore.Assemble(null, ["Cleric"]));
        Assert.Empty(BuffSetStore.Assemble(new Dictionary<string, List<string>>(), []));
    }

    // ---- evaluator over the assembled set ----

    [Fact]
    public void TheEvaluatorSeesTheAssembledUnion()
    {
        var byClass = ThreeClassSetup();
        var assembled = BuffSetStore.Assemble(byClass["dranak_legends"], ["Cleric", "Enchanter"]);
        var active = new List<BuffState>
        {
            new("Temperance", ["Temperance"], "You", T0.AddSeconds(-10), T0.AddSeconds(3000), false),
        };
        var states = BuffSetEvaluator.Evaluate(assembled, active,
            new HashSet<string> { "Clarity" }, new HashSet<string> { "Clarity" }, T0, 60);

        // Cleric's pick is up, Enchanter's was seen fading, the any-class pick was
        // never mentioned — three different claims, one assembled set. Warrior's
        // Resolution is absent entirely: not in the combination, no claim at all.
        Assert.Equal(3, states.Count);
        Assert.Equal(BuffSetStatus.Active, states.Single(s => s.Spell == "Temperance").Status);
        Assert.Equal(BuffSetStatus.Missing, states.Single(s => s.Spell == "Clarity").Status);
        Assert.Equal(BuffSetStatus.NotSeen, states.Single(s => s.Spell == "Spirit of Wolf").Status);
        Assert.DoesNotContain(states, s => s.Spell == "Resolution");
    }

    // ---- per-class add/remove round trip ----

    [Fact]
    public void AddRemoveRoundTrip_PrunesEmptyStructures()
    {
        var byClass = new Dictionary<string, Dictionary<string, List<string>>>();
        Assert.True(BuffSetStore.Add(byClass, "c", "Cleric", "Temperance"));
        Assert.False(BuffSetStore.Add(byClass, "c", "cleric", "TEMPERANCE"));   // dupe, any casing
        Assert.True(BuffSetStore.Add(byClass, "c", "Cleric", "Aegolism"));

        Assert.True(BuffSetStore.Remove(byClass, "c", "CLERIC", "temperance"));
        Assert.Equal(["Aegolism"], BuffSetStore.SpellsFor(byClass["c"], "Cleric"));
        Assert.True(BuffSetStore.Remove(byClass, "c", "Cleric", "Aegolism"));
        Assert.Empty(byClass);   // nothing hollow left behind in settings JSON
        Assert.False(BuffSetStore.Remove(byClass, "c", "Cleric", "Aegolism"));
    }

    [Fact]
    public void PerClassSetsSurviveTheSettingsSerializer()
    {
        var opts = new JsonSerializerOptions
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        var settings = new AppSettings();
        BuffSetStore.Add(settings.BuffSetsByClass, "dranak_legends", BuffSetStore.AnyClass, "Spirit of Wolf");
        BuffSetStore.Add(settings.BuffSetsByClass, "dranak_legends", "Cleric", "Temperance");

        var reloaded = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings, opts), opts)!;

        // Reloaded dictionaries carry the DEFAULT comparer — the store must still
        // find buckets case-insensitively, and edits must keep working.
        Assert.Equal(["Temperance"],
            BuffSetStore.SpellsFor(reloaded.BuffSetsByClass["dranak_legends"], "cleric"));
        Assert.True(BuffSetStore.Remove(reloaded.BuffSetsByClass, "dranak_legends", "CLERIC", "Temperance"));
        Assert.Equal(["Spirit of Wolf"],
            BuffSetStore.Assemble(reloaded.BuffSetsByClass["dranak_legends"], []));
    }

    [Fact]
    public void StoredClassesListsEveryBucketWithPicks()
    {
        var byClass = ThreeClassSetup();
        Assert.Equal([BuffSetStore.AnyClass, "Cleric", "Enchanter", "Warrior"],
            BuffSetStore.StoredClasses(byClass["dranak_legends"]).Order(StringComparer.OrdinalIgnoreCase));
    }

    // ---- the editors' shared search ----

    [Fact]
    public void SearchRanksSeenCastsFirst_AndSkipsOnlyTheTargetBucket()
    {
        var results = BuffSetSearch.Rank("temp",
            seenCasts: ["Temperance"],
            exclude: ["Temperance V"],
            catalog: ["Temperance", "Temperance V", "Temperate Winds", "Clarity"]);
        Assert.Equal(("Temperance", true), results[0]);
        Assert.Contains(("Temperate Winds", false), results);
        Assert.DoesNotContain(results, r => r.Spell == "Temperance V");   // already in the bucket
        Assert.DoesNotContain(results, r => r.Spell == "Clarity");        // no match
    }

    // ---- the stage-1 flag, fixed: sights reset with the log selection ----

    [Fact]
    public void ResetSessionClearsSightsAndActiveBuffs_ButKeepsLearnedDurations()
    {
        var fadeLine = FadeMessageCatalog.Default.FindBySpell("Armor of Faith")!;
        var t = new BuffTracker();
        var store = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            File.WriteAllText(store, "{\"Temperance\": 900}");
            t.AttachStore(store);
            Assert.NotEmpty(t.LearnedDurations);
            GameEvent Ev(int seconds, string message) =>
                LogParser.Parse($"[{T0.AddSeconds(seconds):ddd MMM d HH:mm:ss yyyy}] {message}")!;
            t.Apply(Ev(0, "You begin casting Armor of Faith."));
            t.Apply(Ev(3, "You feel the favor of the gods upon you."));
            t.Apply(Ev(600, fadeLine.Message));

            // The character switch: without the reset, character A's landing would put
            // character B's identical set entry at Missing instead of NotSeen.
            t.ResetSession();

            var sights = t.SetSights();
            Assert.Empty(sights.Landings);
            Assert.Empty(sights.Fades);
            Assert.Empty(sights.OwnCasts);
            Assert.Empty(t.Snapshot(T0.AddSeconds(601)));
            Assert.NotEmpty(t.LearnedDurations);   // install-wide, not session evidence

            var s = Assert.Single(BuffSetEvaluator.Evaluate(["Armor of Faith"],
                t.Snapshot(T0.AddSeconds(601)), sights.Landings, sights.Fades, T0.AddSeconds(601), 60));
            Assert.Equal(BuffSetStatus.NotSeen, s.Status);
        }
        finally { File.Delete(store); }
    }
}
