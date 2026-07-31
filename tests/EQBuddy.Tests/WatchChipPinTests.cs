using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>
/// Which watch rules reach the mini dashboard.
///
/// Written after a real report: chips went per-rule, new rules defaulted to unpinned, and a
/// user who built four rules during play saw an empty mini bar with no hint that a pin
/// existed. The feature looked broken because the default was wrong.
/// </summary>
public class WatchChipPinTests
{
    /// <summary>A new rule shows up without anyone having to find the pin. Chips used to show
    /// every enabled rule; the pin exists to take rules out of a crowded bar, not to opt each
    /// one in.</summary>
    [Fact]
    public void ANewRuleIsPinnedByDefault() =>
        Assert.True(new TrackedRule { Name = "Respawn", Pattern = "placeholder" }.Pinned);

    /// <summary>A rule saved as unpinned stays unpinned — the default must not override a
    /// choice already made.</summary>
    [Fact]
    public void AnExplicitlyUnpinnedRuleSurvivesARoundTrip()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new TrackedRule { Name = "quiet", Pattern = "x", Pinned = false });
        var restored = System.Text.Json.JsonSerializer.Deserialize<TrackedRule>(json)!;

        Assert.False(restored.Pinned);
    }

    /// <summary>The built-in CC-broke rule is a rule like any other, so it appears too.</summary>
    [Fact]
    public void TheDefaultRuleIsPinned()
    {
        var settings = new AppSettings();
        settings.ApplyDefaultRules();

        Assert.All(settings.TrackedRules, r => Assert.True(r.Pinned));
    }
}
