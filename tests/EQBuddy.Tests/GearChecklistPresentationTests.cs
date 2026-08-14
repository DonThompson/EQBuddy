using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Tests;

public sealed class GearChecklistPresentationTests
{
    [Fact]
    public void BuildsGearThenExaltationsAndKeepsEachCategoryOrder()
    {
        var helm = new GearChecklistItem { Slot = "Head", Item = "Steel Helm" };
        var earring = new GearChecklistItem { Slot = "Ear 1", Item = "Fishbone Earring", IsExaltation = true };
        var mask = new GearChecklistItem { Slot = "Face", Item = "Darkbrood Mask" };

        var groups = GearChecklistPresentation.BuildGroups([helm, earring, mask]);

        Assert.Collection(groups,
            group => { Assert.Equal("Gear", group.Heading); Assert.Equal([helm, mask], group.Items); },
            group => { Assert.Equal("Exaltations", group.Heading); Assert.Equal([earring], group.Items); });
    }

    [Fact]
    public void FormatsExaltationEffectProgressAndTooltip()
    {
        var item = new GearChecklistItem
        {
            Slot = "Ear 1", Item = "Fishbone Earring", IsExaltation = true,
            ExaltationEffect = "Enduring Breath", Source = "Drops From: Qeynos Hills", Url = "https://example.test/fishbone",
            Acquired = true,
        };

        Assert.Equal("Fishbone Earring (Enduring Breath)", GearChecklistPresentation.ItemLabel(item));
        Assert.Equal(new GearChecklistPresentation.ItemText("Fishbone Earring", " (Enduring Breath)"),
            GearChecklistPresentation.TextFor(item));
        Assert.Equal("Urgar - 1/1", GearChecklistPresentation.ListName("Urgar", [item]));
        Assert.Equal("Ear 1: Fishbone Earring (Enduring Breath)\nDrops From: Qeynos Hills\nhttps://example.test/fishbone",
            GearChecklistPresentation.Tooltip(item));
    }
}
