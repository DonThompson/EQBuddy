using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Tests;

/// <summary>
/// Per-rule alert sounds. The point of the feature is telling events apart by ear, so
/// the resolution rules matter more than the UI: a rule's own sound wins, a rule with
/// none inherits the shared choice, and a muted rule plays nothing at all.
/// </summary>
public class AlertSoundTests
{
    private static TrackedRule Rule(bool on, string sound = "") =>
        new() { Name = "r", AlertSound = on, AlertSoundName = sound };

    [Fact]
    public void AMutedRuleResolvesToNothingRatherThanADefault() =>
        Assert.Null(AlertSoundCatalog.Resolve(Rule(on: false, "Chimes"), "Ding"));

    [Fact]
    public void ARuleWithNoSoundOfItsOwnInheritsTheSharedChoice() =>
        Assert.Equal("Alarm", AlertSoundCatalog.Resolve(Rule(on: true), "Alarm"));

    [Fact]
    public void ARulesOwnSoundBeatsTheSharedChoice() =>
        Assert.Equal("Chimes", AlertSoundCatalog.Resolve(Rule(on: true, "Chimes"), "Ding"));

    [Fact]
    public void CustomPathsPassThroughUntouched()
    {
        const string path = @"C:\sounds\mote.wav";
        Assert.Equal(path, AlertSoundCatalog.Resolve(Rule(on: true, path), "Ding"));
    }

    /// <summary>Two rules with different sounds must not collapse onto one — this is the
    /// bug a tester reported ("one sound for everything").</summary>
    [Fact]
    public void DifferentRulesResolveToDifferentSounds()
    {
        var charm = Rule(on: true, "Alarm");
        var loot = Rule(on: true, "Chimes");
        Assert.NotEqual(AlertSoundCatalog.Resolve(charm, "Ding"),
                        AlertSoundCatalog.Resolve(loot, "Ding"));
    }

    [Fact]
    public void LegacySystemSoundNamesStillMapOntoThePalette() =>
        Assert.Equal("Chord", AlertSoundCatalog.Resolve(Rule(on: true, "Beep"), "Ding"));

    // ---- picker round-tripping ----

    [Fact]
    public void RulesSavedBeforeThisFeatureKeepTheSharedSound()
    {
        // Old settings.json has AlertSound=true and no AlertSoundName at all.
        var legacy = new TrackedRule { Name = "mote", AlertSound = true };
        Assert.Equal("", legacy.AlertSoundName);
        Assert.Equal(1, AlertSoundCatalog.RuleChoiceIndex(legacy));   // "Default"
        Assert.Equal("Ding", AlertSoundCatalog.Resolve(legacy, "Ding"));
    }

    [Theory]
    [InlineData(0)]   // Off
    [InlineData(1)]   // Default
    [InlineData(2)]   // first built-in
    [InlineData(5)]   // a later built-in
    public void EveryNonCustomChoiceRoundTripsThroughTheIndex(int index)
    {
        var rule = Rule(on: true, "Chimes");
        Assert.False(AlertSoundCatalog.ApplyRuleChoice(rule, index));   // no file dialog needed
        Assert.Equal(index, AlertSoundCatalog.RuleChoiceIndex(rule));
    }

    [Fact]
    public void ChoosingOffMutesTheRule()
    {
        var rule = Rule(on: true, "Chimes");
        AlertSoundCatalog.ApplyRuleChoice(rule, 0);
        Assert.False(rule.AlertSound);
        Assert.Null(AlertSoundCatalog.Resolve(rule, "Ding"));
    }

    [Fact]
    public void ChoosingCustomAsksTheViewForAFile()
    {
        var rule = Rule(on: true);
        Assert.True(AlertSoundCatalog.ApplyRuleChoice(rule, AlertSoundCatalog.RuleChoices.Length - 1));
        Assert.True(rule.AlertSound);   // enabled, but the path is the view's job
    }

    [Fact]
    public void ACustomPathLandsOnTheCustomEntry()
    {
        var rule = Rule(on: true, @"C:\sounds\mote.wav");
        Assert.Equal(AlertSoundCatalog.RuleChoices.Length - 1, AlertSoundCatalog.RuleChoiceIndex(rule));
    }

    [Fact]
    public void ThePickerListsOffDefaultEveryBuiltInAndCustom()
    {
        var choices = AlertSoundCatalog.RuleChoices;
        Assert.Equal(AlertSoundCatalog.OffChoice, choices[0]);
        Assert.Equal(AlertSoundCatalog.InheritChoice, choices[1]);
        Assert.Equal(AlertSoundCatalog.CustomChoice, choices[^1]);
        Assert.Equal(AlertSoundCatalog.Names.Length + 2, choices.Length - 1);
        foreach (var name in AlertSoundCatalog.Names) Assert.Contains(name, choices);
    }
}
