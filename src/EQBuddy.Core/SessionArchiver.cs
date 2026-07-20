namespace EQBuddy.Core;

/// <summary>
/// Glue between live SessionStats and the SQLite history: periodic checkpoints of the
/// active session, finalization on rollover/character switch/app exit, and crash
/// recovery on startup. All writes run off the caller's thread.
/// </summary>
public sealed class SessionArchiver : IDisposable
{
    private readonly SessionRepository _repo;
    private readonly object _lock = new();
    private long _activeId;
    private string _server = "";
    private string _character = "";

    public SessionArchiver(SessionRepository repo)
    {
        _repo = repo;
        _repo.MarkInterruptedAsRecovered();
    }

    public void SetIdentity(string? server, string? character)
    {
        lock (_lock)
        {
            _server = server ?? "";
            _character = character ?? "";
        }
    }

    /// <summary>Checkpoint the active session (no-op for noise-only sessions).</summary>
    public void Checkpoint(StatsSnapshot s)
    {
        if (!SessionRepository.IsMeaningful(s)) return;
        long id; string server, character;
        lock (_lock) { id = _activeId; server = _server; character = _character; }
        if (server.Length == 0 || character.Length == 0) return;
        Task.Run(() =>
        {
            try
            {
                var newId = _repo.Checkpoint(id, s, server, character, "Active");
                lock (_lock) { if (_activeId == id) _activeId = newId; }
            }
            catch (Exception ex) { CoreLog.Error(ex); }
        });
    }

    /// <summary>Finalize the active session with an end reason and start a fresh one.</summary>
    public void FinalizeActive(StatsSnapshot s, string endReason)
    {
        long id; string server, character;
        lock (_lock) { id = _activeId; server = _server; character = _character; _activeId = 0; }
        if (!SessionRepository.IsMeaningful(s) || server.Length == 0 || character.Length == 0)
            return;
        Task.Run(() =>
        {
            try { _repo.Checkpoint(id, s, server, character, endReason); }
            catch (Exception ex) { CoreLog.Error(ex); }
        });
    }

    /// <summary>Synchronous checkpoint — used right before opening the history view.</summary>
    public void CheckpointSync(StatsSnapshot s)
    {
        if (!SessionRepository.IsMeaningful(s)) return;
        long id; string server, character;
        lock (_lock) { id = _activeId; server = _server; character = _character; }
        if (server.Length == 0 || character.Length == 0) return;
        try
        {
            var newId = _repo.Checkpoint(id, s, server, character, "Active");
            lock (_lock) { if (_activeId == id) _activeId = newId; }
        }
        catch (Exception ex) { CoreLog.Error(ex); }
    }

    /// <summary>Synchronous finalize for application shutdown (SESSION-007).</summary>
    public void FinalizeActiveSync(StatsSnapshot s, string endReason)
    {
        long id; string server, character;
        lock (_lock) { id = _activeId; server = _server; character = _character; _activeId = 0; }
        if (!SessionRepository.IsMeaningful(s) || server.Length == 0 || character.Length == 0)
            return;
        try { _repo.Checkpoint(id, s, server, character, endReason); }
        catch (Exception ex) { CoreLog.Error(ex); }
    }

    public void Dispose() { }
}
