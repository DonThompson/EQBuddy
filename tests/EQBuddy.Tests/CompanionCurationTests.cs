using EQBuddy.Companion;
using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// Spawn-point curation from a tablet: the desktop map's right-click, arriving over the
/// wire. David's rule (2026-08-15) is that a tap may do anything a click already does —
/// so these assert the tap does the SAME thing, and above all that it always answers.
/// A write that goes quiet is indistinguishable from one that failed.
/// </summary>
public class CompanionCurationTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 8, 15, 20, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eqb-curate-" + Guid.NewGuid().ToString("N"));
    private readonly SpawnCatalog _catalog = SpawnCatalog.LoadEmbedded();

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* temp dir */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>A ledger with one archived point, learned the way the app learns them:
    /// a /loc, then a kill near it.</summary>
    private (SpawnPointLedger Points, string Zone, double LocY, double LocX) Archive()
    {
        var zone = _catalog.Zones.First(z => z.Named.Count > 0);
        var points = new SpawnPointLedger(Path.Combine(_dir, "zone-spawns"), _catalog);
        points.Apply(new ZoneEvent(Now.AddMinutes(-10), zone.Zone));
        points.Apply(new LocationEvent(Now.AddMinutes(-9), 100, 200, 0));
        points.Apply(new KillEvent(Now.AddMinutes(-9), zone.Named[0].Name, "Dranak"));
        var archived = points.Snapshot(zone.Zone).Points.Single();
        return (points, zone.Zone, archived.LocY, archived.LocX);
    }

    [Fact]
    public void ConfirmingAPointHoldsItAndUnconfirmingReleasesIt()
    {
        var (points, zone, locY, locX) = Archive();

        var said = CompanionActions.Apply(points,
            new CompanionMapAction(CompanionMapEdit.Confirm, zone, locY, locX));
        Assert.Contains("confirmed", said, StringComparison.OrdinalIgnoreCase);
        Assert.True(points.Snapshot(zone).Points.Single().Confirmed);

        said = CompanionActions.Apply(points,
            new CompanionMapAction(CompanionMapEdit.Unconfirm, zone, locY, locX));
        Assert.Contains("refines with new kills", said);
        Assert.False(points.Snapshot(zone).Points.Single().Confirmed);
    }

    [Fact]
    public void RemovingAPointTakesItOutOfTheArchive()
    {
        var (points, zone, locY, locX) = Archive();

        var said = CompanionActions.Apply(points,
            new CompanionMapAction(CompanionMapEdit.Remove, zone, locY, locX));
        Assert.Contains("removed", said, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(points.Snapshot(zone).Points);
    }

    [Fact]
    public void ResettingAZoneClearsItAndCountsWhatWent()
    {
        var (points, zone, _, _) = Archive();

        var said = CompanionActions.Apply(points,
            new CompanionMapAction(CompanionMapEdit.ResetZone, zone, 0, 0));
        Assert.Contains("1 spawn point cleared", said);
        Assert.Empty(points.Snapshot(zone).Points);
    }

    [Fact]
    public void AnEditThatChangesNothingStillSaysSo()
    {
        // The whole point of returning a sentence rather than a bool: the tablet is not
        // beside the PC, and a tap that quietly does nothing reads as a broken app.
        var (points, zone, locY, locX) = Archive();
        CompanionActions.Apply(points, new CompanionMapAction(CompanionMapEdit.Remove, zone, locY, locX));

        // Same tap again, on a point that is now gone.
        var said = CompanionActions.Apply(points,
            new CompanionMapAction(CompanionMapEdit.Remove, zone, locY, locX));
        Assert.False(string.IsNullOrWhiteSpace(said));
        Assert.Contains("nothing to remove", said, StringComparison.OrdinalIgnoreCase);

        var confirmSaid = CompanionActions.Apply(points,
            new CompanionMapAction(CompanionMapEdit.Confirm, zone, locY, locX));
        Assert.Contains("No spawn point there", confirmSaid);

        var resetSaid = CompanionActions.Apply(points,
            new CompanionMapAction(CompanionMapEdit.ResetZone, zone, 0, 0));
        Assert.Contains("Nothing to reset", resetSaid);
    }

    [Fact]
    public void AnEditWithNoZoneIsRefusedRatherThanGuessed()
    {
        // The zone rides with the edit precisely so a player zoning mid-tap can't have
        // a removal land in the archive of wherever they ended up.
        var (points, zone, locY, locX) = Archive();

        var said = CompanionActions.Apply(points,
            new CompanionMapAction(CompanionMapEdit.Remove, "", locY, locX));
        Assert.Contains("didn't say which zone", said);
        Assert.Single(points.Snapshot(zone).Points);   // untouched
    }

    [Fact]
    public void ATabletEditMakesTheDesktopMapRedraw()
    {
        // Neither map window is told about the other; both watch the ledger's revision
        // counter (MapWindow.UpdateSpawnCircles, CompanionMapSource.BuildCircles). That
        // is the whole mechanism by which curating from the couch updates the PC — so
        // an edit that didn't move the revision would be an edit nobody sees.
        var (points, zone, locY, locX) = Archive();
        var before = points.Revision;

        CompanionActions.Apply(points, new CompanionMapAction(CompanionMapEdit.Confirm, zone, locY, locX));
        Assert.True(points.Revision > before, "Confirming from a device must move the ledger revision.");

        var afterConfirm = points.Revision;
        CompanionActions.Apply(points, new CompanionMapAction(CompanionMapEdit.Remove, zone, locY, locX));
        Assert.True(points.Revision > afterConfirm, "Removing from a device must move the ledger revision.");
    }

    [Fact]
    public void TheCircleCarriesTheCoordinatesACurationTapEchoesBack()
    {
        // The page must never invert map space to get back to game coordinates; it
        // sends back what it was given. That only works if the circle carries them.
        var (points, zone, locY, locX) = Archive();
        var map = new CompanionMapSource(new AppSettings { MapFolder = _dir })
            .Build(new CompanionMapRequest { MapZone = zone, TimerZone = zone, Points = points }, Now);

        var circle = Assert.Single(map.Circles);
        Assert.Equal(locY, circle.LocY);
        Assert.Equal(locX, circle.LocX);
        // And the section names the archive's zone, which is what the edit must quote.
        Assert.Equal(zone, map.TimerZone);

        // Round trip: what the page would send back finds the same point.
        var said = CompanionActions.Apply(points,
            new CompanionMapAction(CompanionMapEdit.Confirm, map.TimerZone, circle.LocY, circle.LocX));
        Assert.Contains("confirmed", said, StringComparison.OrdinalIgnoreCase);
    }
}
