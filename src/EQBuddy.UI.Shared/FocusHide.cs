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

    /// <summary>
    /// Can this platform answer "which window is in front?" at all? Windows and macOS
    /// can; X11 and Wayland have no portable probe, so <see cref="Decide"/> is never
    /// even reached there and both tick-boxes do nothing.
    ///
    /// This exists so the UI can SAY that (David, 2026-08-16, on #169). The settings
    /// save correctly now that Linux stopped running two copies of EQBuddy — which
    /// means without this note they would tick, persist, and still hide nothing, and a
    /// setting that keeps its state while doing nothing is the silent no-op CLAUDE.md
    /// treats as broken. Implementing the probe is the other answer, and is not ruled
    /// out; this is the honest interim.
    /// </summary>
    public static bool ForegroundProbeAvailable =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>What Options prints under the two tick-boxes where the platform can't
    /// answer — empty where it can, so the note never appears on Windows or macOS.
    /// Names the reason rather than just refusing: a player who knows it is X11's
    /// missing answer, not a bug in EQBuddy, doesn't spend an evening on it.</summary>
    public static string UnavailableNote =>
        ForegroundProbeAvailable
            ? ""
            : "Not available on Linux yet — X11 and Wayland offer no way to ask which "
              + "window is in front, so the widget stays visible. Your choice is saved "
              + "and will start working if that changes.";
}
