using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>The once-per-update notes popup: catalog integrity and the
/// which-versions-do-you-see selection.</summary>
public class WhatsNewTests
{
    [Fact]
    public void CatalogLoadsAndEveryEntryParsesAsAVersionWithContent()
    {
        var entries = WhatsNewCatalog.Load();
        Assert.True(entries.Count >= 6);
        foreach (var e in entries)
        {
            Assert.True(Version.TryParse(e.Version, out _), $"unparseable version '{e.Version}'");
            Assert.NotEmpty(e.Highlights);
            Assert.All(e.Highlights, h => Assert.False(string.IsNullOrWhiteSpace(h)));
        }
    }

    [Fact]
    public void SkippedVersionsAllShowNewestFirst()
    {
        var notes = WhatsNewCatalog.EntriesBetween("1.23.1", "1.25.0");
        Assert.Equal(["1.25.0", "1.24.0"], notes.Select(n => n.Version).ToArray());
    }

    [Fact]
    public void NothingNewerThanTheRunningVersionEverShows()
    {
        var notes = WhatsNewCatalog.EntriesBetween("1.21.0", "1.23.0");
        Assert.DoesNotContain(notes, n => n.Version is "1.24.0" or "1.25.0");
        Assert.Contains(notes, n => n.Version == "1.23.0");
    }

    [Fact]
    public void FourPartAssemblyVersionsNormalize()
    {
        // The running version arrives as "1.25.0.0" from assembly metadata.
        var notes = WhatsNewCatalog.EntriesBetween("1.24.0.0", "1.25.0.0");
        Assert.Equal("1.25.0", Assert.Single(notes).Version);
    }

    [Fact]
    public void UpToDateMeansSilence()
    {
        Assert.Empty(WhatsNewCatalog.EntriesBetween("1.25.0", "1.25.0"));
    }
}
