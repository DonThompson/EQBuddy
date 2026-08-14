using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>
/// The archiver's id lifecycle (audit findings 1/6/7): a finalize closes the active
/// row for good, a checkpoint queued in an earlier session can neither rewrite that
/// row nor hand its id to the next session, and identity is read at request time.
/// </summary>
public class SessionArchiverTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eqbuddy-arch-").FullName;
    private readonly SessionRepository _repo;
    private readonly SessionArchiver _archiver;

    public SessionArchiverTests()
    {
        _repo = new SessionRepository(Path.Combine(_dir, "history.db"));
        _archiver = new SessionArchiver(_repo);
    }

    public void Dispose()
    {
        _archiver.Dispose();
        _repo.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>A meaningful snapshot whose SessionStart is <paramref name="hour"/>:00 —
    /// distinct hours are distinct sessions to the repository's adopt-by-start rule.</summary>
    private static StatsSnapshot Snap(int hour, int kills = 1)
    {
        var stats = new SessionStats { CharacterName = "Kaybek", ServerName = "freeport" };
        void L(string line) => stats.Apply(LogParser.Parse(line)!);
        L($"[Sat Jul 18 {hour:00}:00:00 2026] You have entered Clan Crushbone.");
        for (var i = 0; i < kills; i++)
        {
            L($"[Sat Jul 18 {hour:00}:0{i + 1}:00 2026] You slash orc pawn for 10 points of damage.");
            L($"[Sat Jul 18 {hour:00}:0{i + 1}:10 2026] You have slain orc pawn!");
        }
        return stats.Snapshot();
    }

    [Fact]
    public void CheckpointResetCheckpointLeavesTheOldRowUntouched()
    {
        // Finding 1's contract: OnReset finalizes BEFORE the stats reset, and after a
        // finalize the next checkpoint must open a NEW row — never keep writing the
        // finalized one (the pre-fix widget overwrote it with post-reset numbers).
        _archiver.SetIdentity("freeport", "Kaybek");
        _archiver.CheckpointSync(Snap(15, kills: 2));
        _archiver.FinalizeActiveSync(Snap(15, kills: 2), "ManualReset");
        _archiver.CheckpointSync(Snap(18, kills: 1));

        var rows = _repo.Query();
        Assert.Equal(2, rows.Count);
        var old = Assert.Single(rows, r => r.EndReason == "ManualReset");
        Assert.Equal(2, old.Kills);
        var fresh = Assert.Single(rows, r => r.EndReason == "Active");
        Assert.Equal(1, fresh.Kills);
    }

    [Fact]
    public void ADelayedFirstCheckpointCannotHandItsRowToTheNextSession()
    {
        // Finding 6, the ABA through the id-0 sentinel: the first Checkpoint's queued
        // task (captured id 0, generation 0) completes only AFTER a finalize has reset
        // _activeId to 0. With the bare id equality it re-adopted the finalized row
        // (same server/character/start) and installed it as the NEW session's active
        // id — every later checkpoint then overwrote history. RunCheckpoint is the
        // queued half, called directly because Task.Run won't order this on demand.
        _archiver.SetIdentity("freeport", "Kaybek");
        var s1 = Snap(15, kills: 2);
        _archiver.FinalizeActiveSync(s1, "ManualReset");           // row A; generation moves on
        _archiver.RunCheckpoint(gen: 0, id: 0, s1, "freeport", "Kaybek");   // the straggler
        _archiver.CheckpointSync(Snap(18, kills: 1));              // the new session

        var rows = _repo.Query();
        Assert.Equal(2, rows.Count);
        var old = Assert.Single(rows, r => r.EndReason == "ManualReset");
        Assert.Equal(2, old.Kills);                                // s1's numbers, untouched
        Assert.Equal(1, Assert.Single(rows, r => r.EndReason == "Active").Kills);
    }

    [Fact]
    public void FinalizeUsesTheIdentitySetAtCallTime()
    {
        // Finding 7's archiver-side contract: MainWindow now calls SetIdentity BEFORE
        // LogWatcher.Select, so a 60-minute-gap rollover during the new character's
        // background ingest finalizes under the NEW name. The wiring itself is WPF
        // code this suite can't host; what it relies on is that the identity read
        // happens when the write is requested, pinned here.
        _archiver.SetIdentity("freeport", "Newchar");
        _archiver.FinalizeActiveSync(Snap(15), "IdleTimeout");
        Assert.Equal("Newchar", Assert.Single(_repo.Query()).Character);
    }
}
