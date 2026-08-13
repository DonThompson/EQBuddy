using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EQBuddy.Core;

namespace EQBuddy.Avalonia.Tests;

/// <summary>
/// The Quest Tracker rendered headlessly: the ledger-overlap card ("mine"), the
/// whole-catalog search, and the header controls every card carries. Matching and
/// persistence are Core's (QuestMatcher/QuestLedgerStore, tested there); these tests
/// guard that the Avalonia view actually surfaces what Core computes.
/// </summary>
[Collection("avalonia")]
public sealed class QuestsRenderTests : IDisposable
{
    private readonly string _profile = Directory.CreateTempSubdirectory("eqbuddy-quests-render-").FullName;

    public QuestsRenderTests() => Environment.SetEnvironmentVariable("EQBUDDY_APPDATA", _profile);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("EQBUDDY_APPDATA", null);
        try { Directory.Delete(_profile, recursive: true); } catch { /* best effort */ }
    }

    private sealed class FakeHost : IQuestsHost
    {
        public AppSettings Settings { get; } = new();
        public QuestCatalog QuestCatalog { get; init; } = new();
        public QuestLedgerStore? QuestLedger { get; init; }
        public ZoneGraph ZoneGraph { get; } = new();
        public string QuestCharacterKey { get; init; } = "";
        public string CurrentZoneName { get; init; } = "";
        public StatsSnapshot CurrentSnapshot() => new();
        public InventoryFile.Snapshot? LatestInventory(bool refresh = false) => null;
        public string? CachedItemStats(string itemName) => null;
        public Task<string?> FetchItemTooltip(string itemName) => Task.FromResult<string?>(null);
    }

    private static QuestCatalog Catalog() => new()
    {
        Quests =
        [
            new QuestEntry
            {
                Name = "The Falchion", Url = "https://eqlwiki.com/The_Falchion",
                StartZone = "Crushbone", QuestGiver = "Danaria Fyrestone",
                Classes = "Paladin",
                Items = [new QuestItemNeed { Name = "Blue Orc Head", Qty = 2 }],
                Rewards = ["The Falchion"],
            },
            new QuestEntry
            {
                Name = "Crude Stein Quest", Url = "https://eqlwiki.com/Crude_Stein",
                StartZone = "Qeynos",
                Items = [new QuestItemNeed { Name = "Crude Stein", Qty = 1 }],
                Rewards = ["Shiny Stein"],
            },
        ],
    };

    [AvaloniaFact]
    public void MineTabShowsOverlapCardWithProgressAndControls()
    {
        var ledger = new QuestLedgerStore(Path.Combine(_profile, "quest-ledger.json"));
        ledger.SetManual("tester_p1999", "Blue Orc Head", 1);
        var window = new QuestsWindow(new FakeHost
        {
            QuestCatalog = Catalog(),
            QuestLedger = ledger,
            QuestCharacterKey = "tester_p1999",
        });
        window.Show();

        var text = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text).ToList();
        Assert.Contains("🗺 Quest Tracker — Tester", text);
        Assert.Contains("The Falchion", text);
        Assert.Contains("1/1", text);                               // item TYPES with any, over total types
        Assert.Contains("• Blue Orc Head — 1/2", text);             // per-item have/need
        Assert.Contains("📌", text);                                // track pin on every card
        Assert.Contains("✓", text);                                 // catch-up done mark
        Assert.Contains("⚑", text);                                 // data-wrong report flag
        // Crude Stein has no owned items, so "mine" must not show it.
        Assert.DoesNotContain("Crude Stein Quest", text);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void SearchReadsTheWholeCatalogAndIgnoresOverlap()
    {
        var window = new QuestsWindow(new FakeHost
        {
            QuestCatalog = Catalog(),
            QuestCharacterKey = "tester_p1999",
        });
        window.Show();
        window.FilterToItem("Crude Stein");

        var text = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text).ToList();
        Assert.Contains("Crude Stein Quest", text);
        Assert.Contains(text, t => t?.StartsWith("🔎 1 match") == true);
        // Zero owned pieces still renders the item row — search is for finding.
        Assert.Contains("• Crude Stein — 0/1", text);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ReadyQuestShowsHandInAffordance()
    {
        var ledger = new QuestLedgerStore(Path.Combine(_profile, "quest-ledger-ready.json"));
        ledger.SetManual("tester_p1999", "Blue Orc Head", 2);
        var window = new QuestsWindow(new FakeHost
        {
            QuestCatalog = Catalog(),
            QuestLedger = ledger,
            QuestCharacterKey = "tester_p1999",
        });
        window.Show();

        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "✔ ready");

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
