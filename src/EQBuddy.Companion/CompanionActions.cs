using EQBuddy.Core;

namespace EQBuddy.Companion;

/// <summary>One thing a device asked to change. The rule (David, 2026-08-15) is that a
/// tap may do anything a click already does on the desktop — no more, and nothing the
/// desktop hides behind a dialog it hasn't been given here.
/// <see cref="CompanionSurfaces.AcceptsTicks"/> is the whole tick list.</summary>
public sealed record CompanionAction(string Surface, string Id, bool Done);

/// <summary>The four things the desktop map's right-click offers on a spawn point.</summary>
public enum CompanionMapEdit
{
    Confirm,
    Unconfirm,
    Remove,
    ResetZone,
}

/// <summary>A curation tap from a device. The point is named by its LOCATION, not an
/// index: the archive re-clusters as kills land, so an index the phone read a second
/// ago may already mean a different dot. The ledger resolves the nearest cluster, which
/// is exactly how the desktop's own right-click finds its target.
///
/// <see cref="Zone"/> travels with the edit rather than being read from "wherever the
/// player is now" — the tap was aimed at a zone the device was showing, and zoning mid-
/// tap must not silently redirect a removal into the new zone's archive.</summary>
public sealed record CompanionMapAction(CompanionMapEdit Edit, string Zone, double LocY, double LocX);

/// <summary>
/// Applying a device's tick to the same store the desktop writes: the settings lists
/// themselves. Pure and synchronous — the host drains queued actions on the UI tick, so
/// this never runs on a socket thread beside a card that's mid-render.
/// </summary>
public static class CompanionActions
{
    /// <summary>Apply one action. False when nothing matched (a stale id from a phone
    /// that was showing an old checklist) or the surface accepts no ticks — the caller
    /// then saves nothing and repaints nothing.</summary>
    public static bool Apply(AppSettings settings, CompanionAction action)
    {
        if (!CompanionSurfaces.AcceptsTicks(action.Surface)) return false;
        switch (action.Surface)
        {
            case CompanionSurfaces.Epics:
            {
                var item = settings.EpicQuestChecklist
                    .FirstOrDefault(i => string.Equals(i.Id, action.Id, StringComparison.Ordinal));
                if (item is null || item.Acquired == action.Done) return false;
                item.Acquired = action.Done;
                // The player deciding IS the resolution of an unassigned auto-tick —
                // exactly what the desktop's own toggle does.
                item.AcquiredUnassigned = false;
                return true;
            }
            case CompanionSurfaces.Sky:
            {
                var item = settings.SkyQuestChecklist
                    .FirstOrDefault(i => string.Equals(i.Id, action.Id, StringComparison.Ordinal));
                if (item is null || item.Acquired == action.Done) return false;
                item.Acquired = action.Done;
                item.AcquiredUnassigned = false;
                return true;
            }
            case CompanionSurfaces.Gear:
            {
                var item = settings.GearChecklist
                    .FirstOrDefault(i => string.Equals(
                        CompanionProjection.GearRowId(i), action.Id, StringComparison.Ordinal));
                if (item is null || item.Acquired == action.Done) return false;
                item.Acquired = action.Done;
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>Apply a curation tap to the spawn-point archive. Returns the sentence
    /// the desktop's own status bar would have shown — devices get told what happened,
    /// including when the answer is "nothing", because a tap that quietly does nothing
    /// is indistinguishable from a broken one. Null only when the edit was malformed
    /// enough that there is nothing honest to say.</summary>
    public static string? Apply(SpawnPointLedger points, CompanionMapAction action)
    {
        if (action.Zone.Length == 0)
            return "That edit didn't say which zone it was for — nothing changed.";

        switch (action.Edit)
        {
            case CompanionMapEdit.Confirm:
            case CompanionMapEdit.Unconfirm:
            {
                var wanted = action.Edit == CompanionMapEdit.Confirm;
                var now = points.ConfirmPoint(action.Zone, action.LocY, action.LocX, wanted);
                if (now is not { } state)
                    return "No spawn point there any more — it may have been removed since your screen last drew.";
                return state
                    ? "Location confirmed — the dot holds this spot from now on."
                    : "Confirmation removed — the dot refines with new kills again.";
            }
            case CompanionMapEdit.Remove:
                return points.RemovePoint(action.Zone, action.LocY, action.LocX)
                    ? "Spawn point removed — new kills near that spot will re-learn it."
                    : "No spawn point there any more — nothing to remove.";
            case CompanionMapEdit.ResetZone:
            {
                var cleared = points.ClearZone(action.Zone);
                return cleared == 0
                    ? $"Nothing to reset — {action.Zone} has no archived spawn points."
                    : $"Reset {action.Zone} — {cleared} spawn point{(cleared == 1 ? "" : "s")} cleared; " +
                      "the zone learns fresh from here.";
            }
            default:
                return null;
        }
    }
}
