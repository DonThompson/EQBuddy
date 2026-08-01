using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>The Faction card's right-hand column, shared so both UIs say it the same way.</summary>
public static class FactionFormat
{
    /// <summary>
    /// "+120", "-45" — or "maxed" once the standing hits the cap, because a farmed faction
    /// that silently stops moving looks like a bug ("could not possibly get any better" is
    /// the game's answer, and it deserves to reach the card). A faction that moved earlier
    /// in the session and then capped shows both: "+120 · maxed".
    /// </summary>
    public static string Net(FactionDetail f) =>
        !f.Capped ? $"{(f.Net >= 0 ? "+" : "")}{f.Net}"
        : f.Net != 0 ? $"{(f.Net >= 0 ? "+" : "")}{f.Net} · maxed"
        : "maxed";
}
