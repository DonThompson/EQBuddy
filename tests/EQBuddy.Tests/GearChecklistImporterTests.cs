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
}
