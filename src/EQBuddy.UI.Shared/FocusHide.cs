namespace EQBuddy.UI.Shared;

/// <summary>
/// The hide-the-overlay decision, pure so tests can walk the truth table without a
/// real foreground window. Two independent opt-ins share it (#41, #114):
///
///  - "Hide while the game runs unfocused": alt-tab away and the overlay yields the
///    corner; the game closed entirely keeps it visible (reviewing history, drops,
///    quests between sessions must stay possible).
///  - "Hide while the game isn't running": the overlay exists only while playing
///    (Frankthetankk's #114). Launching the game brings it back; so does launching
///    EQBuddy again — the second copy asks the running one to surface.
///
/// EQBuddy's own windows having focus always wins: hiding the thing the player is
/// clicking reads as broken, and it's also the escape hatch that keeps Options
/// reachable while the game is closed.
/// </summary>
public static class FocusHide
{
    public static bool Decide(
        bool hideWhenUnfocused, bool hideWhenNotRunning,
        bool foregroundIsSelf, bool foregroundIsGame, bool gameRunning)
    {
        if (!hideWhenUnfocused && !hideWhenNotRunning) return false;
        if (foregroundIsSelf) return false;   // the player is using EQBuddy itself
        if (foregroundIsGame) return false;   // playing — showing is the overlay's job
        return gameRunning ? hideWhenUnfocused : hideWhenNotRunning;
    }
}
