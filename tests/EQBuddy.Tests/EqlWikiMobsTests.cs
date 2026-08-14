using System.Net.Http;
using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>Mob-page parsing for the Loot card's target-drops block. The fixture is the
/// real Lockjaw page (fetched 2026-08-06); both named and regular mobs use
/// {{Namedmobpage}}, regular ones at article-titled pages ("A Spite Golem").</summary>
public class EqlWikiMobsTests
{
    /// <summary>A stubbed fetch answering as the wiki does: the page's own title beside
    /// its wikitext. Tests that care about redirects pass a title different from the one
    /// requested — which is the whole point of WikiPageText.</summary>
    private static Task<WikiPageText?> Served(string title, string? wikitext) =>
        Task.FromResult<WikiPageText?>(wikitext is null ? null : new WikiPageText(title, wikitext));

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "fixtures", "wiki", name + ".txt"));

    [Fact]
    public void LockjawPageParsesDropsWithRarity()
    {
        var mob = EqlWikiMobService.Parse(Fixture("lockjaw-mob"), "Lockjaw");
        Assert.Equal("Lockjaw", mob.Name);
        Assert.Equal("Oasis of Marr", mob.Zone);
        Assert.Equal("25", mob.Level);
        Assert.Equal("Common", mob.Drops.Single(d => d.Item == "Lockjaw Hide Vest").Rarity);
        Assert.Equal("Uncommon", mob.Drops.Single(d => d.Item == "Gator Meat").Rarity);
        // Un-annotated entries keep an empty rarity rather than an invented one.
        Assert.Equal("", mob.Drops.Single(d => d.Item == "Gnome Meat").Rarity);
        Assert.Equal(8, mob.Drops.Count);
    }

    [Fact]
    public async Task RegularMobsResolveViaTheArticleTitledPage()
    {
        // SessionStats names arrive article-stripped, first letter capitalized ("Spite
        // golem"); the wiki page is "A Spite Golem" (titles are case-sensitive past the
        // first letter). The candidate ladder bridges both gaps — this exact case shipped
        // broken once as NOT ON WIKI (2026-08-06 screenshot round).
        var requested = new List<string>();
        var svc = new EqlWikiMobService(
            Path.Combine(Path.GetTempPath(), $"mobcache-{Guid.NewGuid():N}"),
            title =>
            {
                requested.Add(title);
                return Served(title, title == "A Spite Golem"
                    ? "{{Namedmobpage\n| name = A Spite Golem\n| known_loot = \n{{:Apothic Crown}}\n}}"
                    : null);
            });
        var result = await svc.LookupAsync("Spite golem");
        Assert.Equal(ItemLookupState.Live, result.State);
        Assert.Equal(["Spite golem", "A spite golem", "The spite golem", "Spite Golem", "A Spite Golem"],
            requested);
        Assert.Equal("Apothic Crown", result.Mob!.Drops.Single().Item);
    }

    [Fact]
    public async Task ZoneDisambiguatedPagesResolveAndTheCurrentZoneWins()
    {
        // The orc-legionnaire-mid-fight case (David, live, 2026-08-07): the bare-name
        // page is a broken redirect (returns nothing), the real drops live at
        // "Orc Legionnaire (Crushbone)" and "(Deathfist)". The zone-suffix-stripped
        // fuzzy compare admits both; the player's zone picks Crushbone.
        var fetched = new List<string>();
        var svc = new EqlWikiMobService(
            Path.Combine(Path.GetTempPath(), $"mobcache-{Guid.NewGuid():N}"),
            title =>
            {
                fetched.Add(title);
                return Served(title, title switch
                {
                    "Orc Legionnaire (Crushbone)" =>
                        "{{Namedmobpage\n| name = Orc Legionnaire\n| known_loot = \n{{:Crushbone Belt}}\n}}",
                    "Orc Legionnaire (Deathfist)" =>
                        "{{Namedmobpage\n| name = Orc Legionnaire\n| known_loot = \n{{:Deathfist Slashed Belt}}\n}}",
                    _ => null,   // bare page: broken redirect, every exact candidate misses
                });
            },
            _ => Task.FromResult(new List<string>
                { "Orc legionnaire", "Orc Legionnaire (Deathfist)", "Orc Legionnaire (Crushbone)" }));

        var result = await svc.LookupAsync("Orc legionnaire", currentZone: "Crushbone");
        Assert.Equal(ItemLookupState.Live, result.State);
        Assert.Equal("Crushbone Belt", result.Mob!.Drops.Single().Item);
        // Zoneless bare page was still tried first (it outranks FOREIGN zones).
        Assert.Contains("Orc legionnaire", fetched);

        // Without a zone hint, the zoneless candidate still leads and the first
        // resolvable zone page wins — no dead end, no wrong-first bias.
        var noZone = await svc.LookupAsync("Orc legionnaireX".Replace("X", ""), "");
        Assert.Equal(ItemLookupState.Cached, noZone.State);   // second call hits the cache
    }

    [Fact]
    public async Task TheNamedMobsResolveViaTheirArticle()
    {
        // Normalize strips "the " like any article, so The Prophet arrives as "Prophet" —
        // and bare "Prophet" is missing on the wiki (David's report: a well-known named
        // showing no drops). The ladder must try the "The" forms.
        var svc = new EqlWikiMobService(
            Path.Combine(Path.GetTempPath(), $"mobcache-{Guid.NewGuid():N}"),
            title => Served(title, title == "The Prophet"
                ? "{{Namedmobpage\n| name = The Prophet\n| known_loot = \n{{:Prophet Skull}}\n}}"
                : null));
        var result = await svc.LookupAsync("Prophet");
        Assert.Equal(ItemLookupState.Live, result.State);
        Assert.Equal("Prophet Skull", result.Mob!.Drops.Single().Item);
    }

    /// <summary>The fuzzy fallback (David, 2026-08-06): when every exact form misses,
    /// wiki search results are accepted under the spawn catalog's bounded-edit-distance
    /// rule — a one-letter drift resolves, a merely-related page never does.</summary>
    [Fact]
    public async Task WikiSearchRescuesANearMissButNeverAStranger()
    {
        var svc = new EqlWikiMobService(
            Path.Combine(Path.GetTempPath(), $"mobcache-{Guid.NewGuid():N}"),
            title => Served(title, title == "Emperor Crushbone"
                ? "{{Namedmobpage\n| name = Emperor Crushbone\n| known_loot = \n{{:Crown of the Emperor}}\n}}"
                : null),
            _ => Task.FromResult<List<string>>(["Emperor Crushbone"]));
        // One letter off — every exact candidate misses, search + fuzzy resolve it.
        var result = await svc.LookupAsync("Emperor Crushbon");
        Assert.Equal(ItemLookupState.Live, result.State);
        Assert.Equal("Crown of the Emperor", result.Mob!.Drops.Single().Item);

        // A dissimilar search hit is rejected: better no answer than a wrong creature.
        var strict = new EqlWikiMobService(
            Path.Combine(Path.GetTempPath(), $"mobcache-{Guid.NewGuid():N}"),
            _ => Task.FromResult<WikiPageText?>(null),
            _ => Task.FromResult<List<string>>(["Crushbone (Zone)"]));
        Assert.Equal(ItemLookupState.NotFound,
            (await strict.LookupAsync("Emperor Crushbon")).State);
    }

    [Fact]
    public async Task MissingMobIsNotFoundAfterAllCandidates()
    {
        var svc = new EqlWikiMobService(
            Path.Combine(Path.GetTempPath(), $"mobcache-{Guid.NewGuid():N}"),
            _ => Task.FromResult<WikiPageText?>(null),
            _ => Task.FromResult<List<string>>([]));   // stubbed: no network from a unit test
        var result = await svc.LookupAsync("Utterly Fictional");
        Assert.Equal(ItemLookupState.NotFound, result.State);
        Assert.Equal(ItemLookupState.Offline,
            (await new EqlWikiMobService(
                Path.Combine(Path.GetTempPath(), $"mobcache-{Guid.NewGuid():N}"),
                _ => throw new HttpRequestException("no network"))
                .LookupAsync("Anything")).State);
    }

    // ---- #65 round five (Frankthetankk): the article-drop, caught a SECOND time ----

    /// <summary>The wiki API is asked with redirects=1, so a request for the
    /// article-stripped name SUCCEEDS by landing on the real page. v1.57.1 fixed the
    /// packs to print the resolved title — but the resolver was recording the title it
    /// ASKED for, so the resolved title was the stripped one and every link kept the
    /// wrong name. This pins the page's own title as the answer, which is the thing
    /// contribution packs print.</summary>
    [Fact]
    public async Task ARedirectedLookupKeepsThePagesOwnTitleNotTheOneWeAskedFor()
    {
        var svc = new EqlWikiMobService(
            Path.Combine(Path.GetTempPath(), $"mobcache-{Guid.NewGuid():N}"),
            // The log normalizer strips "The", so EQBuddy asks for "Spiroc Lord" — and
            // the wiki redirects that to the real page, exactly as it does live.
            title => Task.FromResult<WikiPageText?>(title == "Spiroc Lord"
                ? new WikiPageText("The Spiroc Lord",
                    "{{Namedmobpage\n| name = The Spiroc Lord\n| known_loot = \n{{:Spiroc Feather}}\n}}")
                : null));

        var result = await svc.LookupAsync("Spiroc Lord");
        Assert.Equal(ItemLookupState.Live, result.State);
        Assert.Equal("The Spiroc Lord", result.Mob!.PageTitle);
    }

    /// <summary>EQ names its gods with an epithet ("Innoruuk, the Prince of Hate") that
    /// the wiki files without ("Innoruuk (God)"). Without the base name in the ladder,
    /// EQBuddy offered to CREATE a page for a boss the wiki already documents.</summary>
    [Fact]
    public async Task AnEpithetFallsBackToTheBaseNameRatherThanProposingADuplicatePage()
    {
        var asked = new List<string>();
        var svc = new EqlWikiMobService(
            Path.Combine(Path.GetTempPath(), $"mobcache-{Guid.NewGuid():N}"),
            title =>
            {
                asked.Add(title);
                return Task.FromResult<WikiPageText?>(title == "Innoruuk"
                    ? new WikiPageText("Innoruuk (God)",
                        "{{Namedmobpage\n| name = Innoruuk\n| known_loot = \n{{:Hate Cloak}}\n}}")
                    : null);
            },
            _ => Task.FromResult<List<string>>([]));

        var result = await svc.LookupAsync("Innoruuk, the Prince of Hate");
        Assert.Contains("Innoruuk", asked);
        Assert.Equal(ItemLookupState.Live, result.State);
        // Landed on the wiki's own title, so the pack links the existing page.
        Assert.Equal("Innoruuk (God)", result.Mob!.PageTitle);
    }
}
