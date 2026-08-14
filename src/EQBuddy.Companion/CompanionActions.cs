using EQBuddy.Core;

namespace EQBuddy.Companion;

/// <summary>One thing a device asked to change. The phone stays read-MOSTLY: the only
/// writes it can make are the checklist ticks the desktop already offers with a click,
/// and <see cref="CompanionSurfaces.AcceptsTicks"/> is the whole list.</summary>
public sealed record CompanionAction(string Surface, string Id, bool Done);

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
}
