using EQBuddy.Core;
using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>#105 (wizen) + the rule-from-recent-line picker: manual rule order,
/// and the recent-lines ring the picker reads.</summary>
public class WatchArrangeTests
{
    [Fact]
    public void MoveRuleReordersAndClampsAtTheEdges()
    {
        var settings = new AppSettings();
        settings.TrackedRules.AddRange([
            new TrackedRule { Name = "a" }, new TrackedRule { Name = "b" },
            new TrackedRule { Name = "c" },
        ]);
        var vm = new OptionsViewModel(settings, () => { });

        vm.MoveRule(settings.TrackedRules[2], -1);
        Assert.Equal(["a", "c", "b"], settings.TrackedRules.Select(r => r.Name));

        vm.MoveRule(settings.TrackedRules[0], -1);   // already first — no-op
        Assert.Equal("a", settings.TrackedRules[0].Name);
        vm.MoveRule(settings.TrackedRules[2], +1);   // already last — no-op
        Assert.Equal("b", settings.TrackedRules[2].Name);
    }

    [Fact]
    public void RecentLinesKeepTheNewestCapAndSurviveHavingNoTextRules()
    {
        var stats = new SessionStats();   // no text rules configured at all
        for (var i = 0; i < SessionStats.RecentLineCap + 20; i++)
            stats.ObserveRawLine($"[Wed Aug 12 21:{i / 60:D2}:{i % 60:D2} 2026] Soandso says, 'line {i}'");

        var lines = stats.RecentLines();
        Assert.Equal(SessionStats.RecentLineCap, lines.Count);
        Assert.EndsWith($"line {SessionStats.RecentLineCap + 19}'", lines[^1].Message);
        Assert.EndsWith("line 20'", lines[0].Message);   // the oldest 20 fell off
    }
}
