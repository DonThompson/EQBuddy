namespace EQBuddy.UI.Shared;

/// <summary>The breadcrumb trail's clock: crumbs fade with wall-clock age, not
/// position in the list — a route walked fifteen minutes ago should be gone
/// whether or not any /loc arrived since. Camping a spot produces no new crumbs
/// at all, and the index-based fade left the route in glowing at the camp
/// forever (David's field test, 2026-08-10).</summary>
public static class TrailFade
{
    /// <summary>Newest crumbs draw at this alpha — strong enough to follow, never
    /// shouting over the map lines under it.</summary>
    public const byte FullAlpha = 200;

    /// <summary>Crumbs younger than this keep full strength: the stretch you just
    /// walked reads as one solid line, not a gradient.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(2);

    /// <summary>By this age a crumb has faded out entirely. Long enough to retrace
    /// a route across a zone; short enough that a camp cleans up behind you.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromMinutes(15);

    /// <summary>Straight-line fade from <see cref="FullAlpha"/> at
    /// <see cref="FreshFor"/> down to 0 at <see cref="Horizon"/>.</summary>
    public static byte Alpha(TimeSpan age)
    {
        if (age <= FreshFor) return FullAlpha;
        if (age >= Horizon) return 0;
        var gone = (age - FreshFor) / (Horizon - FreshFor);
        return (byte)(FullAlpha * (1 - gone));
    }
}
