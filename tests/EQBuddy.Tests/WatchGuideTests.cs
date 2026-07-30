using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Tests;

/// <summary>
/// The in-app watch-rule examples. Documentation that quietly stops matching the app is
/// worse than none — someone follows it, it doesn't work, and now they distrust the tool.
/// These check the examples still describe rules the app can actually build.
/// </summary>
public class WatchGuideTests
{
    [Fact]
    public void EveryKindHasAnExample()
    {
        var covered = WatchGuide.Examples.Select(e => e.Kind).Distinct().ToList();
        var all = Enum.GetValues<WatchKind>();
        Assert.Equal(all.Length, covered.Count);
    }

    /// <summary>Each example must be renderable: both UIs index KindNames by the enum value.</summary>
    [Fact]
    public void EveryExampleKindHasADropdownLabel() =>
        Assert.All(WatchGuide.Examples,
            e => Assert.InRange((int)e.Kind, 0, OptionsViewModel.KindNames.Length - 1));

    /// <summary>An example with no match text is only honest for the kinds where empty
    /// really does mean "all of them". Log text is deliberately not one of them.</summary>
    [Fact]
    public void ExamplesWithNoMatchTextAreMatchAllKinds()
    {
        foreach (var ex in WatchGuide.Examples.Where(e => e.Match.Length == 0))
        {
            var rule = new TrackedRule { Name = ex.Name, Pattern = "", Kind = ex.Kind };
            Assert.True(rule.IsMatchAllKind || ex.Kind == WatchKind.SpellFade,
                $"Example \"{ex.Name}\" ({ex.Kind}) shows no match text, but that kind needs one");
        }
    }

    /// <summary>Delays shown in the guide must be values the setting will actually keep —
    /// an example the app silently clamps is a lie.</summary>
    [Fact]
    public void ExampleDelaysSurviveTheSetting()
    {
        foreach (var ex in WatchGuide.Examples.Where(e => e.Delay.Length > 0))
        {
            Assert.True(double.TryParse(ex.Delay, out var seconds), $"\"{ex.Delay}\" is not a number");
            Assert.Equal(seconds, new TrackedRule { AlertDelaySeconds = seconds }.AlertDelaySeconds);
        }
    }

    /// <summary>The example rules must actually match the text they claim to. Cheap, and it
    /// catches an example written against matching rules that later changed.</summary>
    [Theory]
    [InlineData("mote", "Mote of Minor Potential", true)]
    [InlineData("mote", "Crushbone Belt", false)]
    [InlineData("CH -->", "Cleric1 tells the raid, 'CH --> Tank'", true)]
    [InlineData("You begin casting Poison Bolt", "You begin casting Poison Bolt.", true)]
    public void GuideMatchTextBehavesAsDescribed(string pattern, string subject, bool shouldMatch) =>
        Assert.Equal(shouldMatch,
            new TrackedRule { Pattern = pattern, Kind = WatchKind.Loot }.Matches(subject));

    [Fact]
    public void TheBasicsArePresentAndTerse()
    {
        Assert.NotEmpty(WatchGuide.Basics);
        // A panel in a widget, not a manual: anything this long belongs in the FeatureGuide.
        Assert.All(WatchGuide.Basics, b => Assert.True(b.Length < 220, $"too long: {b}"));
    }
}
