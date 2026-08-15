using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// Log archiving becomes the default in 1.84.0 (discussion #146, wizen).
///
/// The complaint was not that archiving was missing — it had shipped for #52 — but that
/// EQBuddy's behaviour out of the box was to empty a file the player never asked it to
/// empty, and keep no copy. A default nobody chose was destroying data. So the default
/// flips, and the preference worth opting into becomes "I want the disk space back".
///
/// The interesting half is the existing profiles: theirs carry an explicit false from
/// when that WAS the default, so a changed default alone would reach nobody who already
/// runs EQBuddy — which is everybody it matters to. Hence a one-time migration, and
/// hence the flag that keeps it one-time.
/// </summary>
public class LogArchiveDefaultTests
{
    [Fact]
    public void ANewProfileArchivesWithoutBeingAsked()
    {
        Assert.True(new AppSettings().ArchiveLogs);
    }

    [Fact]
    public void AnExistingProfileWithArchivingOffIsTurnedOnOnce()
    {
        // What every pre-1.84.0 settings.json looks like.
        var settings = new AppSettings { ArchiveLogs = false, ArchiveDefaultMigrated = false };

        Assert.True(settings.MigrateArchiveDefault());
        Assert.True(settings.ArchiveLogs);
        Assert.True(settings.ArchiveDefaultMigrated);
    }

    [Fact]
    public void TurningItBackOffAfterwardsSticks()
    {
        // The whole reason for the flag: this is a preference, not a defect to re-fix
        // at every launch. Someone who wants their disk space back gets to keep it.
        var settings = new AppSettings { ArchiveLogs = false, ArchiveDefaultMigrated = false };
        settings.MigrateArchiveDefault();
        settings.ArchiveLogs = false;

        Assert.False(settings.MigrateArchiveDefault());
        Assert.False(settings.ArchiveLogs);
    }

    [Fact]
    public void TheMigrationReportsNoSecondChange()
    {
        // Load() saves whenever a migration returns true; a migration that kept saying
        // "changed" would rewrite settings.json on every launch forever.
        var settings = new AppSettings();

        Assert.True(settings.MigrateArchiveDefault());    // records that it ran
        Assert.False(settings.MigrateArchiveDefault());
    }
}
