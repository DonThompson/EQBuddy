using EQBuddy.Core;

namespace EQBuddy.Tests;

public class SessionRepositoryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eqbuddy-db-").FullName;
    private readonly SessionRepository _repo;

    public SessionRepositoryTests() =>
        _repo = new SessionRepository(Path.Combine(_dir, "history.db"));

    public void Dispose()
    {
        _repo.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private static StatsSnapshot SampleSnapshot()
    {
        var stats = new SessionStats { CharacterName = "Kaybek", ServerName = "freeport" };
        foreach (var line in new[]
        {
            "[Sat Jul 18 15:00:00 2026] You have entered Clan Crushbone.",
            "[Sat Jul 18 15:00:05 2026] You slash orc pawn for 10 points of damage.",
            "[Sat Jul 18 15:00:10 2026] You have slain orc pawn!",
            "[Sat Jul 18 15:00:12 2026] --You have looted a Mote of Infinitesimal Potential from orc pawn's corpse.--",
            "[Sat Jul 18 15:00:15 2026] You gain party experience! (1.5%)",
        })
            stats.Apply(LogParser.Parse(line)!);
        return stats.Snapshot();
    }

    [Fact]
    public void CheckpointFinalizeAndReload()
    {
        var s = SampleSnapshot();
        var id = _repo.Checkpoint(0, s, "freeport", "Kaybek", "Active");
        Assert.True(id > 0);
        _repo.Checkpoint(id, s, "freeport", "Kaybek", "IdleTimeout");

        var rows = _repo.Query();
        var row = Assert.Single(rows);
        Assert.Equal(("freeport", "Kaybek", "IdleTimeout", 1), (row.Server, row.Character, row.EndReason, row.Kills));
        Assert.Equal("Clan Crushbone", row.PrimaryZone);

        var loaded = _repo.LoadSnapshot(id);
        Assert.NotNull(loaded);
        Assert.Equal(s.DamageDealt, loaded!.DamageDealt);
        Assert.Equal("Mote of Infinitesimal Potential", Assert.Single(loaded.Loot).Item);
    }

    [Fact]
    public void ReingestedSessionAdoptsExistingRowInsteadOfDuplicating()
    {
        // Restarting with auto-empty off (or re-importing a log) replays sessions the
        // store already has; same (server, character, start) must update, not insert.
        var s = SampleSnapshot();
        var first = _repo.Checkpoint(0, s, "qeynos", "Douglas", "IdleTimeout");
        var second = _repo.Checkpoint(0, s, "qeynos", "Douglas", "IdleTimeout");
        Assert.Equal(first, second);
        Assert.Single(_repo.Query());
        // A different character with the same start time is still a separate session.
        _repo.Checkpoint(0, s, "qeynos", "Caybin", "IdleTimeout");
        Assert.Equal(2, _repo.Query().Count);
    }

    [Fact]
    public void SearchMatchesLootInsideSnapshot()
    {
        var id = _repo.Checkpoint(0, SampleSnapshot(), "freeport", "Kaybek", "IdleTimeout");
        Assert.True(id > 0);
        Assert.Single(_repo.Query(search: "mote"));
        Assert.Empty(_repo.Query(search: "banded mail"));
    }

    [Fact]
    public void NotesAndTagsRoundTripAndSearch()
    {
        var id = _repo.Checkpoint(0, SampleSnapshot(), "freeport", "Kaybek", "IdleTimeout");
        _repo.SetNoteTags(id, "great camp", "solo,basement");
        var row = Assert.Single(_repo.Query(search: "basement"));
        Assert.Equal("great camp", row.Note);
    }

    [Fact]
    public void InterruptedSessionsBecomeRecovered()
    {
        _repo.Checkpoint(0, SampleSnapshot(), "freeport", "Kaybek", "Active");
        Assert.Equal(1, _repo.MarkInterruptedAsRecovered());
        Assert.Equal("RecoveredAfterCrash", Assert.Single(_repo.Query()).EndReason);
    }

    [Fact]
    public void NoiseOnlySessionsAreNotMeaningful()
    {
        var stats = new SessionStats();
        stats.Apply(LogParser.Parse("[Sat Jul 18 15:00:00 2026] You have entered Clan Crushbone.")!);
        Assert.False(SessionRepository.IsMeaningful(stats.Snapshot()));
        Assert.True(SessionRepository.IsMeaningful(SampleSnapshot()));
    }

    [Fact]
    public void DeleteRemovesRow()
    {
        var id = _repo.Checkpoint(0, SampleSnapshot(), "freeport", "Kaybek", "Manual");
        _repo.Delete(id);
        Assert.Empty(_repo.Query());
    }

    [Fact]
    public void CharactersEnumerates()
    {
        _repo.Checkpoint(0, SampleSnapshot(), "freeport", "Kaybek", "Manual");
        _repo.Checkpoint(0, SampleSnapshot(), "qeynos", "Douglas", "Manual");
        Assert.Equal(2, _repo.Characters().Count);
    }
}
