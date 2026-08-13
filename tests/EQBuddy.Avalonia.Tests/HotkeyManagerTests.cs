namespace EQBuddy.Avalonia.Tests;

/// <summary>
/// Pins the gesture grammar of the opt-in hotkeys (#100). The parser is the safety
/// gate that keeps the 1.34.0 disaster buried: a gesture without a modifier must never
/// reach RegisterHotKey, because a bare letter claimed system-wide eats typing in the
/// game's own chat box. Pure-function tests; registration itself is Windows-only.
/// </summary>
public class HotkeyManagerTests
{
    [Theory]
    [InlineData("Ctrl+Alt+M", 0x2u | 0x1u, 0x4Du)]
    [InlineData("Ctrl+Shift+F9", 0x2u | 0x4u, 0x78u)]
    [InlineData("Win+Z", 0x8u, 0x5Au)]
    [InlineData("control + alt + m", 0x2u | 0x1u, 0x4Du)]   // spacing and case are free
    [InlineData("Shift+NumPad5", 0x4u, 0x65u)]
    public void ParsesModifiedGestures(string gesture, uint mods, uint vk)
    {
        var parsed = HotkeyManager.Parse(gesture);
        Assert.NotNull(parsed);
        Assert.Equal((mods, vk), parsed.Value);
    }

    /// <summary>The 1.34.0 rule: no modifier, no hotkey — ever.</summary>
    [Theory]
    [InlineData("M")]
    [InlineData("F9")]
    [InlineData("Ctrl+Alt")]      // modifier with no key
    [InlineData("Ctrl+Fnord")]    // unknown key name
    [InlineData("")]
    public void RejectsUnsafeOrUnparseableGestures(string gesture) =>
        Assert.Null(HotkeyManager.Parse(gesture));

    /// <summary>Every action the Options page offers must survive a settings round-trip
    /// key-for-key; a rename here silently orphans stored bindings.</summary>
    [Fact]
    public void ActionKeysAreStable()
    {
        Assert.Equal(
            ["toggleAll", "toggleMinimize", "toggleMap", "toggleQuests", "toggleSpawns", "toggleClickThrough"],
            HotkeyManager.Actions.Select(a => a.Key));
    }
}
