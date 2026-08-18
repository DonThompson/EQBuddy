using EQBuddy.Companion;
using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The phone and the desktop must answer the same question the same way — in BOTH
/// directions.
///
/// David, 2026-08-18: mobile and desktop are each first-class, and neither is allowed to
/// be the one that quietly falls behind. #210 (liminalwarmth) is what made the cost
/// visible, and it is worth being precise about which way round it was: EQBuddy Mobile
/// still built the cross-class ready list after the DESKTOP had lost it, so for two days
/// the phone answered "what can I turn in right now" and the big window could not.
/// Restoring the desktop then created the mirror risk — four things the desktops had and
/// the phone did not.
///
/// A list of features kept level by hand drifts; that is the whole history of #184, #122
/// and #152. So parity is asserted against the SHARED module both sides call: if the
/// projection ever stops agreeing with <see cref="QuestChecklistLayout"/>, these fail,
/// rather than a player finding it.
/// </summary>
public class SurfaceParityTests
{
    private static SkyQuestChecklistItem Item(string id, string cls, string reward,
        string item, bool acquired = false, string npc = "Cilin Spellsinger") => new()
        {
            Id = id, ClassName = cls, Npc = npc, Reward = reward, QuestItem = item,
            Source = "Isle 3", Acquired = acquired,
        };

    /// <summary>One class with all four states, plus a second class holding a ready
    /// reward — so "across every class" is a real claim and the ordering has something
    /// to order.</summary>
    private static AppSettings Settings()
    {
        var s = new AppSettings();
        s.SkyQuestChecklist.AddRange([
            Item("a1", "Bard", "Amulet of the Fae", "Amulet piece", acquired: true),
            Item("b1", "Bard", "Mask of Song", "Woolen Mask", acquired: true),
            Item("b2", "Bard", "Mask of Song", "Wind Rune Meda", acquired: true),
            Item("c1", "Bard", "Mantle of the Songweaver", "Woolen Mantle", acquired: true),
            Item("c2", "Bard", "Mantle of the Songweaver", "Wind Rune Azia"),
            Item("d1", "Bard", "Spear of Harmony", "Spear shaft"),
            Item("r1", "Ranger", "Bow of Sky", "Bow stave", acquired: true,
                npc: "Efreeti Lord Djarn"),
        ]);
        s.SkyQuestCompleted.Add(QuestChecklistLayout.RewardKey("Bard", "Amulet of the Fae"));
        return s;
    }

    private static CompanionChecklistSection Sky(AppSettings s) =>
        CompanionProjection.Build(
            new CompanionInputs
            {
                Settings = s,
                Character = "Dranak",
                AppVersion = "1.93.0",
                Offered = CompanionSurfaces.All,
            },
            new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Local)).Quests!.Sky;

    private static IReadOnlyList<QuestChecklistGroup> Desktop(AppSettings s) =>
        QuestChecklistLayout.Sky(s.SkyQuestChecklist, s.SkyQuestCompleted);

    [Fact]
    public void ThePhoneGroupsSkyExactlyAsTheDesktopDoes()
    {
        var s = Settings();

        // Skip the ★ Ready band, which is a summary the desktop draws separately.
        var phone = Sky(s).Groups.Skip(1).Select(g => g.Heading);
        var desktop = Desktop(s).Select(g => g.Heading);

        Assert.Equal(desktop, phone);
    }

    [Fact]
    public void ThePhoneOrdersByActionabilityBecauseTheDesktopDoes()
    {
        // Ready first, then closest-to-done, then untouched, turned-in last. This
        // reached the phone by conversion rather than by being ported, which is the
        // point: nobody had to notice it was missing.
        Assert.Equal(
            ["Bard · Mask of Song", "Bard · Mantle of the Songweaver",
             "Bard · Spear of Harmony", "Bard · Amulet of the Fae", "Ranger · Bow of Sky"],
            Sky(Settings()).Groups.Skip(1).Select(g => g.Heading));
    }

    [Fact]
    public void ThePhoneAndTheDesktopAgreeOnEveryStateNote()
    {
        var s = Settings();

        Assert.Equal(
            Desktop(s).Select(g => g.Note),
            Sky(s).Groups.Skip(1).Select(g => g.Note));
    }

    [Fact]
    public void TheReadyBandSpansEveryClassOnThePhoneToo()
    {
        var band = Sky(Settings()).Groups[0];

        Assert.Equal("★ Ready 2", band.Heading);
        Assert.Equal(
            ["Bard — Mask of Song", "Ranger — Bow of Sky"],
            band.Rows.Select(r => r.Text));
    }

    [Fact]
    public void TheReadyBandNamesWhoTakesTheHandInOnThePhoneToo()
    {
        // "What can I turn in right now" is only actionable with "and to whom" — the
        // phone is the surface most likely to be read while walking to the NPC.
        Assert.Equal("Efreeti Lord Djarn",
            Sky(Settings()).Groups[0].Rows.Single(r => r.Text.StartsWith("Ranger")).Detail);
    }

    [Fact]
    public void ARewardTurnedInOnEitherScreenIsTurnedInOnBoth()
    {
        // The reward key is one spelling in one place. It used to be written out by hand
        // here as well, which is how "done" could have meant two things.
        var s = Settings();
        var key = QuestChecklistLayout.RewardKey("Bard", "Mask of Song");

        UI.Shared.SkyCompleteToggle.MarkTurnedIn(s, key,
            UI.Shared.SkyCompleteToggle.ItemsFor(s.SkyQuestChecklist, key));

        Assert.DoesNotContain(Sky(s).Groups[0].Rows, r => r.Text.Contains("Mask of Song"));
        Assert.Equal("done",
            Sky(s).Groups.Skip(1).Single(g => g.Heading.EndsWith("Mask of Song")).Note);
    }

    [Fact]
    public void ThePhoneCountsWhatTheDesktopCounts()
    {
        var s = Settings();
        var desktop = Desktop(s);

        Assert.Equal(desktop.Sum(g => g.Done), Sky(s).Done);
        Assert.Equal(desktop.Sum(g => g.Total), Sky(s).Total);
    }
}
