using EQBuddy.Companion;
using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>The quest surface: the three-tab consolidation on EQBuddy Mobile. The
/// catalog index ships once per device (the map-geometry contract), the general list's
/// membership comes from Core's QuestMatcher, and a device's pin/class taps land on
/// the same ledger the desktop quest window writes.</summary>
public class CompanionQuestsTests
{
    private static readonly DateTime Now = new(2026, 8, 16, 20, 0, 0);

    private static QuestCatalog Catalog() => new()
    {
        Quests =
        [
            new QuestEntry
            {
                Name = "Crude Stein Quest", Url = "https://eqlwiki.com/Crude_Stein",
                StartZone = "Qeynos", QuestGiver = "Fhara", MinLevel = 5,
                Classes = "ALL", Era = "Classic", Repeatable = true,
                Items = [new QuestItemNeed { Name = "Crude Stein", Qty = 2 }],
                Rewards = ["Fine Stein"],
            },
            new QuestEntry
            {
                Name = "The Falchion", Url = "https://eqlwiki.com/The_Falchion",
                StartZone = "Crushbone", QuestGiver = "Ambassador DVinn",
                Classes = "Paladin",
                Items = [new QuestItemNeed { Name = "Blue Orc Head", Qty = 1 }],
                Rewards = ["The Falchion"],
            },
            new QuestEntry
            {
                Name = "Bone Chip Bounty", Url = "https://eqlwiki.com/Bone_Chips",
                StartZone = "Kaladim", QuestGiver = "Gnilbin",
                Classes = "", Repeatable = false,
                Items = [new QuestItemNeed { Name = "Bone Chips", Qty = 4 }],
                Rewards = ["A few coppers"],
            },
        ],
    };

    private static Dictionary<string, QuestLedgerStore.Entry> Owned(params (string Item, int Count)[] items) =>
        items.ToDictionary(i => i.Item, i => new QuestLedgerStore.Entry { Manual = i.Count },
            StringComparer.OrdinalIgnoreCase);

    private static CompanionSnapshot Build(CompanionQuestRequest req, AppSettings? settings = null)
    {
        var index = req.Catalog is { } c ? CompanionQuestIndex.Build(c) : null;
        return CompanionProjection.Build(new CompanionInputs
        {
            Character = "Dranak",
            AppVersion = "1.88.0",
            Offered = [CompanionSurfaces.Quests],
            Settings = settings,
            Quests = req,
            QuestIndex = index,
        }, Now);
    }

    // ---------------- the index ----------------

    [Fact]
    public void IndexCarriesEveryQuestAndTheClassListItself()
    {
        var index = CompanionQuestIndex.Build(Catalog());
        Assert.Equal(3, index.Quests.Count);
        // The picker's class list rides with the catalog so the page can never keep a
        // stale hand-copied one (Berserker was once missing from exactly such a copy).
        Assert.Equal(QuestClassFilter.Classes.Length, index.AllClasses.Count);
        Assert.Contains(index.AllClasses, c => c is { Name: "Berserker", Abbrev: "BER" });
    }

    [Fact]
    public void IndexResolvesClassTextThroughCoresFilter()
    {
        var index = CompanionQuestIndex.Build(Catalog());
        // "ALL" and empty are unrestricted — shipped as null so 1,200 entries don't
        // each carry fifteen abbrevs.
        Assert.Null(index.Quests[0].A);
        Assert.Null(index.Quests[2].A);
        Assert.Equal(["PAL"], index.Quests[1].A);
    }

    [Fact]
    public void IndexStampMovesWithTheData()
    {
        var a = CompanionQuestIndex.Build(Catalog());
        var changed = Catalog();
        changed.Quests[0].Items[0].Qty = 3;   // a fixed quantity must re-ship the index
        var b = CompanionQuestIndex.Build(changed);
        Assert.NotEqual(a.Stamp, b.Stamp);
        Assert.Equal(a.Stamp, CompanionQuestIndex.Build(Catalog()).Stamp);
    }

    // ---------------- the projected section ----------------

    [Fact]
    public void TabsComeFromCoreAndGeneralHasNoBadge()
    {
        var settings = new AppSettings
        {
            EpicQuestChecklist = [new EpicQuestChecklistItem { Id = "e1", ClassName = "Bard", QuestItem = "x", Acquired = true }],
            SkyQuestChecklist = [new SkyQuestChecklistItem { Id = "s1", ClassName = "Bard", Reward = "y", QuestItem = "z" }],
        };
        var quests = Build(new CompanionQuestRequest { Catalog = Catalog() }, settings).Quests!;
        Assert.Equal(["general", "epic", "sky"], quests.Tabs.Select(t => t.Key));
        Assert.Equal(["Quests", "Epic 1.0", "Plane of Sky"], quests.Tabs.Select(t => t.Label));
        Assert.Null(quests.Tabs[0].Badge);       // a catalog you search, not a checklist you finish
        Assert.Equal("1 / 1", quests.Tabs[1].Badge);
        Assert.Equal("0 / 1", quests.Tabs[2].Badge);
    }

    [Fact]
    public void MineIsTheMatchersListByNameWithOwnedCountsAlongside()
    {
        var quests = Build(new CompanionQuestRequest
        {
            Catalog = Catalog(),
            Owned = Owned(("Bone Chips", 4), ("Blue Orc Head", 1)),
            Tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Crude Stein Quest" },
        }).Quests!;

        // Tracked first (the matcher's promise), then most-complete.
        Assert.Equal(["Crude Stein Quest", "Bone Chip Bounty", "The Falchion"], quests.Mine);
        Assert.Equal(0, quests.MineMore);
        Assert.Equal(4, quests.Owned["Bone Chips"]);
        Assert.Equal(["Crude Stein Quest"], quests.Tracked);
    }

    [Fact]
    public void MineExcludesDismissedAndCompletedNonRepeatables()
    {
        var quests = Build(new CompanionQuestRequest
        {
            Catalog = Catalog(),
            Owned = Owned(("Bone Chips", 4), ("Blue Orc Head", 1), ("Crude Stein", 1)),
            Hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "The Falchion" },
            Completed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Bone Chip Bounty"] = 1,      // non-repeatable: leaves the list
                ["Crude Stein Quest"] = 2,     // repeatable: stays
            },
        }).Quests!;
        Assert.Equal(["Crude Stein Quest"], quests.Mine);
        Assert.Equal(2, quests.Completed["Crude Stein Quest"]);
    }

    [Fact]
    public void ClassesShipWithAbbrevsAndInferredOnlyFillsAnEmptyPick()
    {
        var picked = Build(new CompanionQuestRequest
        {
            Catalog = Catalog(),
            Classes = ["Bard", "Shadow Knight"],
            InferredClass = "Monk",
        }).Quests!;
        Assert.Equal(["BRD", "SHD"], picked.Classes.Select(c => c.Abbrev));
        Assert.Null(picked.InferredClass);   // a pick beats inference, as on the desktop

        var inferred = Build(new CompanionQuestRequest
        {
            Catalog = Catalog(),
            InferredClass = "Monk",
        }).Quests!;
        Assert.Equal("Monk", inferred.InferredClass);
    }

    // ---------------- fingerprints ----------------

    [Fact]
    public void FingerprintMovesOnLedgerChangesButNeverOnTime()
    {
        var req = new CompanionQuestRequest { Catalog = Catalog(), Owned = Owned(("Bone Chips", 2)) };
        string Print(CompanionQuestRequest r, DateTime at) =>
            CompanionProjection.SectionFingerprints(CompanionProjection.Build(new CompanionInputs
            {
                Character = "Dranak",
                Offered = [CompanionSurfaces.Quests],
                Quests = r,
                QuestIndex = CompanionQuestIndex.Build(r.Catalog!),
            }, at))[CompanionSurfaces.Quests];

        var baseline = Print(req, Now);
        Assert.Equal(baseline, Print(req, Now.AddMinutes(10)));   // trap 8: no drift
        Assert.NotEqual(baseline, Print(req with { Owned = Owned(("Bone Chips", 3)) }, Now));
        Assert.NotEqual(baseline, Print(req with
        {
            Tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "The Falchion" },
        }, Now));
    }

    // ---------------- the sticky catalog ----------------

    [Fact]
    public void TheCatalogIsSentOncePerDeviceAndTheStampAlwaysRides()
    {
        var snap = Build(new CompanionQuestRequest { Catalog = Catalog() });
        var state = new CompanionClientState();

        var first = snap.ForClient([CompanionSurfaces.Quests], state);
        Assert.NotNull(first.Quests!.Catalog);

        var second = snap.ForClient([CompanionSurfaces.Quests], state);
        Assert.Null(second.Quests!.Catalog);                       // already held
        Assert.Equal(first.Quests.CatalogStamp, second.Quests.CatalogStamp);

        // A fresh device (a reconnect) is told everything again.
        Assert.NotNull(snap.ForClient([CompanionSurfaces.Quests], new CompanionClientState()).Quests!.Catalog);
    }

    // ---------------- taps ----------------

    [Fact]
    public void PinTapsLandOnTheLedgerAndRepeatsAreNotChanges()
    {
        var dir = Directory.CreateTempSubdirectory("eqb-quests-");
        try
        {
            var ledger = new QuestLedgerStore(Path.Combine(dir.FullName, "ledger.json"));
            Assert.True(CompanionActions.Apply(ledger, "dranak_legends",
                new CompanionAction(CompanionSurfaces.Quests, "track|The Falchion", true)));
            Assert.Contains("The Falchion", ledger.TrackedFor("dranak_legends"));
            // Same state again: no change, no repaint.
            Assert.False(CompanionActions.Apply(ledger, "dranak_legends",
                new CompanionAction(CompanionSurfaces.Quests, "track|The Falchion", true)));
            Assert.True(CompanionActions.Apply(ledger, "dranak_legends",
                new CompanionAction(CompanionSurfaces.Quests, "track|The Falchion", false)));
            Assert.Empty(ledger.TrackedFor("dranak_legends"));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void ClassTapsUseTheDesktopsOwnSetClassesAndDropUnknownNames()
    {
        var dir = Directory.CreateTempSubdirectory("eqb-quests-");
        try
        {
            var ledger = new QuestLedgerStore(Path.Combine(dir.FullName, "ledger.json"));
            Assert.True(CompanionActions.Apply(ledger, "dranak_legends",
                new CompanionAction(CompanionSurfaces.Quests, "classes|Bard,Monk,NotAClass", true)));
            Assert.Equal(["Bard", "Monk"], ledger.ClassesFor("dranak_legends"));
            // The same set again is not a change.
            Assert.False(CompanionActions.Apply(ledger, "dranak_legends",
                new CompanionAction(CompanionSurfaces.Quests, "classes|Monk,Bard", true)));
            // And an empty list clears the pick.
            Assert.True(CompanionActions.Apply(ledger, "dranak_legends",
                new CompanionAction(CompanionSurfaces.Quests, "classes|", true)));
            Assert.Empty(ledger.ClassesFor("dranak_legends"));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void MalformedQuestTapsChangeNothing()
    {
        var dir = Directory.CreateTempSubdirectory("eqb-quests-");
        try
        {
            var ledger = new QuestLedgerStore(Path.Combine(dir.FullName, "ledger.json"));
            Assert.False(CompanionActions.Apply(ledger, "",
                new CompanionAction(CompanionSurfaces.Quests, "track|X", true)));
            Assert.False(CompanionActions.Apply(ledger, "dranak_legends",
                new CompanionAction(CompanionSurfaces.Quests, "no-verb-here", true)));
            Assert.False(CompanionActions.Apply(ledger, "dranak_legends",
                new CompanionAction(CompanionSurfaces.Quests, "track|", true)));
            Assert.False(CompanionActions.Apply(ledger, "dranak_legends",
                new CompanionAction(CompanionSurfaces.Loot, "track|X", true)));
        }
        finally { dir.Delete(true); }
    }

    // ---------------- the registry and the old names ----------------

    [Fact]
    public void QuestsReplacedEpicsAndSkyInTheOfferListButTheirTicksStillApply()
    {
        Assert.Contains(CompanionSurfaces.Quests, CompanionSurfaces.All);
        Assert.DoesNotContain(CompanionSurfaces.Epics, CompanionSurfaces.All);
        Assert.DoesNotContain(CompanionSurfaces.Sky, CompanionSurfaces.All);
        // The Epic/Sky tabs inside the quest surface still tick under the old names.
        Assert.True(CompanionSurfaces.AcceptsTicks(CompanionSurfaces.Epics));
        Assert.True(CompanionSurfaces.AcceptsTicks(CompanionSurfaces.Sky));
        Assert.True(CompanionSurfaces.AcceptsTicks(CompanionSurfaces.Quests));
    }

    [Fact]
    public void EpicAndSkyGroupsNameTheirClassForThePagesLens()
    {
        var settings = new AppSettings
        {
            EpicQuestChecklist =
            [
                new EpicQuestChecklistItem { Id = "e1", ClassName = "Bard", Section = "Pieces", QuestItem = "x" },
                new EpicQuestChecklistItem { Id = "e2", ClassName = "Monk", Section = "Pieces", QuestItem = "y" },
            ],
            SkyQuestChecklist =
            [
                new SkyQuestChecklistItem { Id = "s1", ClassName = "Bard", Reward = "r", Npc = "n", QuestItem = "q" },
            ],
        };
        var quests = Build(new CompanionQuestRequest { Catalog = Catalog() }, settings).Quests!;
        Assert.Equal(["Bard", "Monk"], quests.Epics.Groups.Select(g => g.Class));
        Assert.All(quests.Sky.Groups.Where(g => !g.Heading.StartsWith('★')),
            g => Assert.Equal("Bard", g.Class));
    }
}
