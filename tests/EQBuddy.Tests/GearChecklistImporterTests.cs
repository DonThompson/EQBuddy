using EQBuddy.Core;

namespace EQBuddy.Tests;

public sealed class GearChecklistImporterTests
{
    [Fact]
    public void ImportsEqLegendsToolsShoppingListSlots()
    {
        const string html = """
            <!doctype html>
            <html>
            <head><title>Erudite BRD/NEC/WIZ Shopping List - EQ Legends Tools</title></head>
            <body>
              <div class="slot-card">
                <div class="slot-heading">HEAD</div>
                <a class="item-link" href="https://eqlwiki.com/Carmine_Turban">Carmine Turban</a>
                <div class="source-lines">
                  <div class="source-line">Drops From: Plane of Fear: various mobs</div>
                  <div class="source-line">Drops From: Plane of Hate: <a href="https://eqlwiki.com/a_spite_golem">a spite golem</a></div>
                </div>
              </div>
              <div class="slot-card">
                <div class="slot-heading">EAR 1</div>
                <a class="item-link" href="https://eqlwiki.com/Ivandyr%27s_Hoop">Ivandyr&#39;s Hoop</a>
                <div class="source-line">Reward from Quest: <a href="https://eqlwiki.com/Lynuga">Lynuga's Gem Collection</a></div>
              </div>
              <div class="slot-card empty">
                <div class="slot-heading">AMMO</div>
                <p>No item selected</p>
              </div>
            </body>
            </html>
            """;

        var result = GearChecklistImporter.ImportHtml(html);

        Assert.Equal("Erudite BRD/NEC/WIZ", result.Name);
        Assert.Collection(result.Items,
            item =>
            {
                Assert.Equal("Head", item.Slot);
                Assert.Equal("Carmine Turban", item.Item);
                Assert.Equal("Drops From: Plane of Fear: various mobs | Drops From: Plane of Hate: a spite golem", item.Source);
                Assert.Equal("https://eqlwiki.com/Carmine_Turban", item.Url);
            },
            item =>
            {
                Assert.Equal("EAR 1", item.Slot);
                Assert.Equal("Ivandyr's Hoop", item.Item);
                Assert.Equal("Reward from Quest: Lynuga's Gem Collection", item.Source);
            });
    }

    [Fact]
    public void SocketedExaltationsDoNotBleedIntoTheGearItem()
    {
        // Real exports nest a socketed-block inside the slot card, whose entries have
        // their own item-link and source-line divs (verified against the live
        // eqlegendstools.com export template, 2026-08-12). The gear row must carry
        // only the equipped item's name and sources.
        const string html = """
            <html>
            <head><title>Test Shopping List - EQ Legends Tools</title></head>
            <body>
              <section class="slot-card">
                <div class="slot-heading">CHEST</div>
                <div class="entry shopping-entry">
                  <div class="entry-meta">Equipped Item</div>
                  <a class="item-link" href="https://eqlwiki.com/Breastplate">Fine Breastplate</a>
                  <div class="source-lines">
                    <div class="source-line">Drops From: Permafrost: an ice goblin</div>
                  </div>
                </div>
                <div class="socketed-block">
                  <div class="socketed-head">Socketed Exaltations</div>
                  <div class="entry socketed-entry">
                    <a class="item-link" href="https://eqlwiki.com/Gem">Glowing Gem</a>
                    <div class="source-lines">
                      <div class="source-line">Drops From: The Hole: a construct</div>
                    </div>
                  </div>
                </div>
              </section>
            </body>
            </html>
            """;

        var item = Assert.Single(GearChecklistImporter.ImportHtml(html).Items);
        Assert.Equal("Fine Breastplate", item.Item);
        Assert.Equal("Drops From: Permafrost: an ice goblin", item.Source);
    }

    [Fact]
    public void ImportsGearAndExaltationsWithTheirCheckedState()
    {
        const string html = """
            <section class="slot-card"><div class="slot-heading">HEAD</div>
              <div class="entry shopping-entry"><input class="collected-checkbox" type="checkbox" checked><a class="item-link" href="/helm">Steel Helm</a><div class="source-line">Drops From: Blackburrow</div></div>
              <div class="socketed-block"><div class="socketed-head">Socketed Exaltations</div>
                <div class="entry socketed-entry shopping-entry focus"><div class="effect-name">Enhancement Haste I</div><input class="collected-checkbox" type="checkbox"><a class="item-link" href="/gem">Haste Gem</a><div class="source-line">Drops From: Solusek's Eye</div></div>
                <div class="entry socketed-entry shopping-entry worn is-collected"><input class="collected-checkbox" type="checkbox"><a class="item-link" href="/eye">See Invisible Eye</a></div>
              </div>
            </section>
            """;

        var result = GearChecklistImporter.ImportHtml(html);

        Assert.Collection(result.Items,
            item => { Assert.False(item.IsExaltation); Assert.True(item.Acquired); Assert.Equal("Steel Helm", item.Item); },
            item => { Assert.True(item.IsExaltation); Assert.False(item.Acquired); Assert.Equal("Haste Gem", item.Item); Assert.Equal("Enhancement Haste I", item.ExaltationEffect); },
            item => { Assert.True(item.IsExaltation); Assert.True(item.Acquired); Assert.Equal("See Invisible Eye", item.Item); });
    }

    [Fact]
    public void ReimportPreservesTicksMadeInTheApp()
    {
        // A box ticked in the app — by hand or by the loot/inventory auto-done — is
        // the player's state; a fresh export that doesn't know about it must not
        // untick it. Matching is the importer's own slot|exaltation|item key, so a
        // changed wish (new item in the slot) honestly starts unticked.
        var existing = new List<GearChecklistItem>
        {
            new() { Slot = "Head", Item = "Steel Helm", Acquired = true },
            new() { Slot = "Head", IsExaltation = true, Item = "Haste Gem", Acquired = true },
            new() { Slot = "Waist", Item = "Crushbone Belt +5", Acquired = false },
        };
        var imported = new List<GearChecklistItem>
        {
            new() { Slot = "Head", Item = "Steel Helm", Acquired = false },
            new() { Slot = "Head", IsExaltation = true, Item = "Haste Gem", Acquired = false },
            new() { Slot = "Waist", Item = "Crushbone Belt +5", Acquired = false },
            new() { Slot = "Chest", Item = "Fine Breastplate", Acquired = true },   // export's own tick
        };

        GearChecklistImporter.PreserveAcquired(imported, existing);

        Assert.True(imported.Single(i => i.Item == "Steel Helm").Acquired);
        Assert.True(imported.Single(i => i.Item == "Haste Gem").Acquired);
        Assert.False(imported.Single(i => i.Item == "Crushbone Belt +5").Acquired);
        Assert.True(imported.Single(i => i.Item == "Fine Breastplate").Acquired);
    }
}
