using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The accuracy gates (David, 2026-08-11: "being wrong once drops faith in
/// everything"). These invariants run on every build — including the weekly
/// knowledge-refresh PR's CI — so a harvest that reintroduces parse artifacts,
/// index pages, or a new aggregate species CANNOT merge quietly. Each rule
/// encodes a defect actually found in the 2026-08-11 audit.
/// </summary>
public class CatalogSanityTests
{
    private static readonly QuestCatalog Cat = QuestCatalog.LoadEmbedded();

    [Fact]
    public void ItemNamesAreCleanLootMatchableText()
    {
        foreach (var q in Cat.Quests)
            foreach (var i in q.Items)
            {
                Assert.False(string.IsNullOrWhiteSpace(i.Name), $"'{q.Name}': empty item name");
                // The Dreadscale ':Banded Bracer' class of artifact, and anything a
                // loot line could never print.
                Assert.False(i.Name.StartsWith(':') || i.Name.Contains("  ")
                    || i.Name.IndexOfAny(['[', ']', '{', '}', '|', '<', '>']) >= 0,
                    $"'{q.Name}': artifact item name '{i.Name}'");
                Assert.InRange(i.Qty, 1, 100);
            }
    }

    [Fact]
    public void NoQuestListsTheSameItemTwice()
    {
        foreach (var q in Cat.Quests)
        {
            var dupes = q.Items.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(dupes.Count == 0, $"'{q.Name}': duplicate items [{string.Join(", ", dupes)}]");
        }
    }

    [Fact]
    public void AggregatePagesCannotSneakBackIn()
    {
        // Every "...Quests" page must be flagged (the #99 disease, catalog-wide).
        foreach (var q in Cat.Quests.Where(q =>
                     q.Name.EndsWith("Quests", StringComparison.OrdinalIgnoreCase)))
            Assert.True(q.Collection, $"'{q.Name}' looks aggregate but isn't flagged");

        // An UNFLAGGED page with an absurd reward list is a new index page the
        // hygiene list hasn't met yet ("Popular Quests by Level" carried 347).
        // Gambling quests are the legitimate exception: one turn-in, a long list
        // of possible payouts.
        var gamblingPayouts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Deck of Spontaneous Generation Quest", "Knight Card Quest" };
        foreach (var q in Cat.Quests.Where(q => !q.Collection && !gamblingPayouts.Contains(q.Name)))
            Assert.True(q.Rewards.Count < 30,
                $"'{q.Name}': {q.Rewards.Count} rewards unflagged — new index/aggregate page?");

        // And an unflagged non-epic with a vast item list is a new aggregate species.
        foreach (var q in Cat.Quests.Where(q =>
                     !q.Collection && !q.Name.Contains("Epic", StringComparison.OrdinalIgnoreCase)))
            Assert.True(q.Items.Count < 45,
                $"'{q.Name}': {q.Items.Count} items unflagged — new aggregate page?");
    }

    [Fact]
    public void ErasStayOnTheLadder()
    {
        foreach (var q in Cat.Quests.Where(q => q.Era.Length > 0))
            Assert.Contains(QuestEraLadder.Eras, e =>
                e.Equals(q.Era, StringComparison.OrdinalIgnoreCase));
    }
}
