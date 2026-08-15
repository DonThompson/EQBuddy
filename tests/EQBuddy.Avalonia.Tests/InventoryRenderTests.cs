using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EQBuddy.Core;

namespace EQBuddy.Avalonia.Tests;

/// <summary>
/// The inventory and Gear Locker windows over a synthetic dump snapshot — no game
/// folder, no network: both take their inputs as delegates, so the tests hand them
/// exactly what MainWindow will.
/// </summary>
[Collection("avalonia")]
public sealed class InventoryRenderTests : IDisposable
{
    private readonly string _profile = Directory.CreateTempSubdirectory("eqbuddy-inventory-render-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_profile, recursive: true); }
        catch (Exception ex) { Console.Error.WriteLine($"profile cleanup failed: {ex.Message}"); }
    }

    private static InventoryFile.Snapshot Snapshot() =>
        new("/game/Vahlara_teek-Inventory.txt", DateTime.Now.AddMinutes(-5), new Dictionary<string, int>())
        {
            Entries =
            [
                new InventoryFile.Entry("Primary", "Rusty Broad Sword", 1),
                new InventoryFile.Entry("General 1", "Backpack", 1),
                new InventoryFile.Entry("General 1-Slot1", "Bone Chips", 3),
                new InventoryFile.Entry("Bank 1", "Fine Steel Long Sword", 1),
            ],
            SinceDump = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Words of Odus"] = 2,
            },
        };

    private static List<string?> Texts(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

    [AvaloniaFact]
    public void InventoryShowsWornBagsBankAndLootSinceTheDump()
    {
        var window = new InventoryWindow(_ => Snapshot());
        window.Show();

        var texts = Texts(window);
        Assert.Contains("Worn", texts);
        Assert.Contains("Rusty Broad Sword", texts);
        Assert.Contains("Backpack  (General 1 — 1 item)", texts);
        Assert.Contains("Bone Chips ×3", texts);
        Assert.Contains("Elsewhere", texts);
        Assert.Contains("Fine Steel Long Sword", texts);
        Assert.Contains("Looted since this dump (1)", texts);
        Assert.Contains("Words of Odus ×2", texts);
        Assert.Contains(texts, t => t?.StartsWith("Vahlara_teek-Inventory.txt — written") == true);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void InventoryWithoutADumpExplainsTheOutputfileRecipe()
    {
        var window = new InventoryWindow(_ => null);
        window.Show();

        Assert.Contains(Texts(window),
            t => t?.StartsWith("No inventory dump found yet") == true);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void GearLockerOffersOneCountedFetchForItemsWithoutStats()
    {
        // A name no catalog or cache knows: it must land in the not-fetched group and
        // be offered by the explicit, counted fetch button — never fetched silently.
        var wiki = new EqlWikiItemService(Path.Combine(_profile, "items"),
            _ => Task.FromResult<string?>(null));
        var snapshot = new InventoryFile.Snapshot(
            "/game/Vahlara_teek-Inventory.txt", DateTime.Now, new Dictionary<string, int>())
        {
            Entries = [new InventoryFile.Entry("Primary", "Zzz Testblade of Nowhere", 1)],
        };
        var window = new GearLockerWindow(wiki, _ => snapshot, () => [], () => null);
        window.Show();

        var texts = Texts(window);
        Assert.Contains("STATS NOT FETCHED YET (1)", texts);
        var fetch = window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Content?.ToString()?.StartsWith("⇣ fetch stats for") == true);
        Assert.True(fetch.IsVisible);
        Assert.Equal("⇣ fetch stats for 1 item", fetch.Content);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
