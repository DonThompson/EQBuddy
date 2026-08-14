using System.Text.RegularExpressions;
using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>David, 2026-08-14: every surface that names an in-game command offers
/// a one-click ⧉ copy of the EXACT text. The commands live once in GameCommands;
/// these pin the text the game expects and forbid any copy source from carrying
/// its own literal — a future command change can't drift between surfaces.</summary>
public class GameCommandsTests
{
    [Fact]
    public void CommandsAreExactlyWhatTheGameExpects()
    {
        Assert.Equal("/outputfile inventory", GameCommands.OutputfileInventory);
        Assert.Equal("/outputfile achievements", GameCommands.OutputfileAchievements);
        Assert.Equal("/loc", GameCommands.LocSocialLine1);
        Assert.Equal("/doability 1", GameCommands.LocSocialLine2);
    }

    [Fact]
    public void LocSocialIsTwoLinesOnePerSocialSlot()
    {
        var lines = GameCommands.LocSocial.Split(Environment.NewLine);
        Assert.Equal([GameCommands.LocSocialLine1, GameCommands.LocSocialLine2], lines);
        // Each pastes into one social-editor slot: single line, slash-led.
        Assert.All(lines, l => Assert.StartsWith("/", l));
        Assert.All(lines, l => Assert.DoesNotContain('\n', l));
    }

    /// <summary>No clipboard call anywhere in src may copy a slash-command literal —
    /// copy sources must reference GameCommands, the whole point of centralizing.</summary>
    [Fact]
    public void NoCopySurfaceCarriesItsOwnCommandLiteral()
    {
        var src = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src");
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => Regex.IsMatch(File.ReadAllText(f), """SetText(?:Async)?\(\s*@?"/"""))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(offenders);
    }
}
