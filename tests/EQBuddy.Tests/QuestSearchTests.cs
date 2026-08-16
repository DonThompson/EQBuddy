using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

// The Quest Tracker's one way in (David, 2026-08-15). The search box existed before this
// but sat under a "+ I have this" item/quantity row that dominated it — "it exists but is
// so compressed I missed it". The row is gone and this is now the only entry point, so
// what it matches is a contract rather than a convenience: a player who knows only the
// REWARD they want must find the quest that grants it.
public class QuestSearchTests
{
    private static readonly QuestEntry Wakizashi = new()
    {
        Name = "Ritual Summoning of the Frozen Skies",
        StartZone = "Iceclad Ocean",
        QuestGiver = "Sentry Alecs",
        Items = [new QuestItemNeed { Name = "Frozen Sky Shard", Qty = 4 }],
        Rewards = ["Wakizashi of the Frozen Skies", "Cloak of Flames"],
    };

    [Theory]
    // The motivating case: you want the sword, you know nothing else.
    [InlineData("Wakizashi of the Frozen Skies")]
    [InlineData("wakizashi")]              // case-insensitive
    [InlineData("Frozen Skies")]           // a fragment of the reward
    [InlineData("Cloak of Flames")]        // the OTHER reward, not just the first
    [InlineData("Frozen Sky Shard")]       // a turn-in item
    [InlineData("Ritual Summoning")]       // the quest's own name
    [InlineData("Sentry Alecs")]           // the quest giver
    [InlineData("Iceclad")]                // the start zone
    public void EveryThingAPlayerMightKnowFindsTheQuest(string typed) =>
        Assert.True(QuestSearch.Matches(Wakizashi, typed));

    [Fact]
    public void AnEmptySearchMatchesEverything()
    {
        // The default state of the box is "show me the list", not "show me nothing".
        Assert.True(QuestSearch.Matches(Wakizashi, ""));
    }

    [Fact]
    public void SomethingUnrelatedMatchesNothing()
    {
        // Guards against a match rule so loose it stops narrowing anything.
        Assert.False(QuestSearch.Matches(Wakizashi, "Rubicite Breastplate"));
    }
}
