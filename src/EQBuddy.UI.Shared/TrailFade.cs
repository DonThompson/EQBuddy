namespace EQBuddy.UI.Shared;

/// <summary>The breadcrumb trail's clock: crumbs fade with wall-clock age, not
/// position in the list — camping a spot produces no new crumbs at all, and the
/// index-based fade left the route in glowing at the camp forever (David's field
/// test, 2026-08-10). Second field pass, same day: the trail is the last MINUTE
/// of movement, fading continuously from the moment a crumb lands — a comet
/// tail, not a persistent route history.</summary>
public static class TrailFade
{
    /// <summary>A brand-new crumb draws at this alpha — strong enough to follow,
    /// never shouting over the map lines under it.</summary>
    public const byte FullAlpha = 200;

    /// <summary>By this age a crumb has faded out entirely.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromMinutes(1);

    /// <summary>Straight-line fade from <see cref="FullAlpha"/> at age zero down
    /// to 0 at <see cref="Horizon"/> — no full-strength plateau, so the tail is
    /// already dimming the moment it's drawn.</summary>
    public static byte Alpha(TimeSpan age)
    {
        if (age <= TimeSpan.Zero) return FullAlpha;
        if (age >= Horizon) return 0;
        return (byte)(FullAlpha * (1 - age / Horizon));
    }
}
