using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>
/// Where a running timer's named actually camps — a different question from where its
/// spawn point was archived, and the answer the map window pins and the tablet draws.
///
/// Two consumers ask it (the desktop map's named panel, and the phone's map surface),
/// so it lives here rather than twice: the day the fallback order changes, both
/// windows must change with it or one of them starts lying about a camp.
/// </summary>
public static class CampLocations
{
    /// <summary>Your own /loc at kill time wins — you were standing there. The wiki's
    /// location field is the fallback, flagged <c>FromWiki</c> because it is
    /// approximate and the desktop prints it with a "~". Null when neither knows yet.
    ///
    /// <paramref name="ensureLookup"/> is the caller's memoized, rate-limited kick-off
    /// for the wiki fetch; this never starts a lookup of its own, and an answer that
    /// arrives later simply shows up on a subsequent call.</summary>
    public static (double Y, double X, bool FromWiki)? Resolve(
        SpawnTimerState timer,
        Action<string> ensureLookup,
        Func<string, (double Y, double X)?> wikiCamp)
    {
        if (timer is { CampLocY: { } cy, CampLocX: { } cx }) return (cy, cx, false);
        ensureLookup(timer.Name);
        return wikiCamp(timer.Name) is { } loc ? (loc.Y, loc.X, true) : null;
    }
}
