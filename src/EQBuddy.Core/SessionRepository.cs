using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace EQBuddy.Core;

/// <summary>One row in the history list.</summary>
public sealed record SessionRow(
    long Id, string Server, string Character, DateTime StartLocal, DateTime? EndLocal,
    double ElapsedSeconds, double ActiveSeconds, string EndReason, string PrimaryZone,
    int Kills, double XpPercent, long Copper, int LootCount, int Deaths, double Dps,
    string Note, string Tags);

/// <summary>
/// SQLite session history (STORE-*): lives in app-data, versioned schema, survives
/// upgrades. Sessions are stored as a queryable summary row plus the full
/// StatsSnapshot as JSON for the detail view.
/// </summary>
public sealed class SessionRepository : IDisposable
{
    public const int CurrentSchemaVersion = 1;

    // Rate fields can legitimately hit Infinity in degenerate sessions; never let
    // that kill a checkpoint write (observed in the wild via error.log).
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };
    private readonly SqliteConnection _db;
    private readonly object _lock = new();

    public static string DefaultDbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EQBuddy", "history.db");

    public SessionRepository(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();
        Exec("""
            CREATE TABLE IF NOT EXISTS Meta (Key TEXT PRIMARY KEY, Value TEXT);
            CREATE TABLE IF NOT EXISTS Sessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Server TEXT NOT NULL, Character TEXT NOT NULL,
                StartUtc TEXT NOT NULL, EndUtc TEXT,
                LocalOffsetMinutes INTEGER NOT NULL,
                ElapsedSeconds REAL NOT NULL, ActiveSeconds REAL NOT NULL,
                EndReason TEXT NOT NULL, PrimaryZone TEXT NOT NULL DEFAULT '',
                Kills INTEGER NOT NULL, XpPercent REAL NOT NULL,
                Copper INTEGER NOT NULL, LootCount INTEGER NOT NULL,
                Deaths INTEGER NOT NULL, Dps REAL NOT NULL,
                Note TEXT NOT NULL DEFAULT '', Tags TEXT NOT NULL DEFAULT '',
                SnapshotJson TEXT NOT NULL,
                AppVersion TEXT NOT NULL DEFAULT '', SchemaVersion INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_Sessions_Char ON Sessions(Server, Character, StartUtc DESC);
            """);
        var stored = QueryScalar("SELECT Value FROM Meta WHERE Key='SchemaVersion'");
        if (stored is null)
            Exec($"INSERT INTO Meta (Key, Value) VALUES ('SchemaVersion', '{CurrentSchemaVersion}')");
        // Future migrations run here, transactionally, keyed off the stored version.
    }

    /// <summary>Insert or update the active session's checkpoint row. Returns the row id.</summary>
    public long Checkpoint(long id, StatsSnapshot s, string server, string character, string endReason)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            if (id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO Sessions (Server, Character, StartUtc, EndUtc, LocalOffsetMinutes,
                        ElapsedSeconds, ActiveSeconds, EndReason, PrimaryZone, Kills, XpPercent,
                        Copper, LootCount, Deaths, Dps, SnapshotJson, AppVersion, SchemaVersion, CreatedUtc)
                    VALUES ($server, $char, $start, $end, $offset, $elapsed, $active, $reason, $zone,
                        $kills, $xp, $copper, $loot, $deaths, $dps, $json, $app, $schema, $created);
                    SELECT last_insert_rowid();
                    """;
            }
            else
            {
                cmd.CommandText = """
                    UPDATE Sessions SET EndUtc=$end, ElapsedSeconds=$elapsed, ActiveSeconds=$active,
                        EndReason=$reason, PrimaryZone=$zone, Kills=$kills, XpPercent=$xp,
                        Copper=$copper, LootCount=$loot, Deaths=$deaths, Dps=$dps, SnapshotJson=$json
                    WHERE Id=$id;
                    SELECT $id;
                    """;
                cmd.Parameters.AddWithValue("$id", id);
            }
            var start = (s.SessionStart ?? DateTime.Now).ToUniversalTime();
            cmd.Parameters.AddWithValue("$server", server);
            cmd.Parameters.AddWithValue("$char", character);
            if (id == 0)
            {
                cmd.Parameters.AddWithValue("$start", start.ToString("O"));
                cmd.Parameters.AddWithValue("$offset",
                    (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes);
                cmd.Parameters.AddWithValue("$app",
                    typeof(SessionRepository).Assembly.GetName().Version?.ToString() ?? "");
                cmd.Parameters.AddWithValue("$schema", CurrentSchemaVersion);
                cmd.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
            }
            cmd.Parameters.AddWithValue("$end",
                (s.LastEventTime ?? DateTime.Now).ToUniversalTime().ToString("O"));
            cmd.Parameters.AddWithValue("$elapsed", s.Elapsed.TotalSeconds);
            cmd.Parameters.AddWithValue("$active", s.ActiveSeconds);
            cmd.Parameters.AddWithValue("$reason", endReason);
            cmd.Parameters.AddWithValue("$zone", s.CurrentZone);
            cmd.Parameters.AddWithValue("$kills", s.YourKillCount);
            cmd.Parameters.AddWithValue("$xp", s.XpPercent);
            cmd.Parameters.AddWithValue("$copper", s.Copper);
            cmd.Parameters.AddWithValue("$loot", s.LootTotal);
            cmd.Parameters.AddWithValue("$deaths", s.Deaths.Count);
            cmd.Parameters.AddWithValue("$dps", s.SessionDps);
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(s, JsonOpts));
            return (long)cmd.ExecuteScalar()!;
        }
    }

    /// <summary>A session worth keeping (SESSION-003: no noise-only rows).</summary>
    public static bool IsMeaningful(StatsSnapshot s) =>
        s.YourKillCount > 0 || s.XpPercent > 0 || s.LootTotal > 0 ||
        s.DamageDealt > 0 || s.Deaths.Count > 0 || s.Elapsed >= TimeSpan.FromMinutes(10);

    /// <summary>On startup: sessions left 'Active' by a crash become recovered history (RECOVERY-004/005).</summary>
    public int MarkInterruptedAsRecovered()
    {
        lock (_lock)
            return Exec("UPDATE Sessions SET EndReason='RecoveredAfterCrash' WHERE EndReason='Active'");
    }

    public List<(string Server, string Character)> Characters()
    {
        lock (_lock)
        {
            var result = new List<(string, string)>();
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT Server, Character FROM Sessions ORDER BY Server, Character";
            using var r = cmd.ExecuteReader();
            while (r.Read()) result.Add((r.GetString(0), r.GetString(1)));
            return result;
        }
    }

    /// <summary>Newest-first session list; search matches character, zone, note, tags, or snapshot JSON (loot/creatures).</summary>
    public List<SessionRow> Query(string? server = null, string? character = null, string? search = null)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Server, Character, StartUtc, EndUtc, LocalOffsetMinutes, ElapsedSeconds,
                       ActiveSeconds, EndReason, PrimaryZone, Kills, XpPercent, Copper, LootCount,
                       Deaths, Dps, Note, Tags
                FROM Sessions
                WHERE ($server IS NULL OR Server = $server)
                  AND ($char IS NULL OR Character = $char)
                  AND ($search IS NULL OR Character LIKE $like OR PrimaryZone LIKE $like
                       OR Note LIKE $like OR Tags LIKE $like OR SnapshotJson LIKE $like)
                ORDER BY StartUtc DESC LIMIT 500
                """;
            cmd.Parameters.AddWithValue("$server", (object?)server ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$char", (object?)character ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$search",
                string.IsNullOrWhiteSpace(search) ? DBNull.Value : search);
            cmd.Parameters.AddWithValue("$like", $"%{search}%");
            using var r = cmd.ExecuteReader();
            var rows = new List<SessionRow>();
            while (r.Read())
            {
                rows.Add(new SessionRow(
                    r.GetInt64(0), r.GetString(1), r.GetString(2),
                    DateTime.Parse(r.GetString(3)).ToLocalTime(),
                    r.IsDBNull(4) ? null : DateTime.Parse(r.GetString(4)).ToLocalTime(),
                    r.GetDouble(6), r.GetDouble(7), r.GetString(8), r.GetString(9),
                    r.GetInt32(10), r.GetDouble(11), r.GetInt64(12), r.GetInt32(13),
                    r.GetInt32(14), r.GetDouble(15), r.GetString(16), r.GetString(17)));
            }
            return rows;
        }
    }

    public StatsSnapshot? LoadSnapshot(long id)
    {
        lock (_lock)
        {
            var json = QueryScalar("SELECT SnapshotJson FROM Sessions WHERE Id=" + id);
            if (json is null) return null;
            try { return JsonSerializer.Deserialize<StatsSnapshot>(json, JsonOpts); }
            catch (Exception ex) { CoreLog.Error(ex); return null; }
        }
    }

    public void SetNoteTags(long id, string note, string tags)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE Sessions SET Note=$note, Tags=$tags WHERE Id=$id";
            cmd.Parameters.AddWithValue("$note", note);
            cmd.Parameters.AddWithValue("$tags", tags);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void Delete(long id)
    {
        lock (_lock) Exec("DELETE FROM Sessions WHERE Id=" + id);
    }

    private int Exec(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteNonQuery();
    }

    private string? QueryScalar(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar() as string;
    }

    public void Dispose() => _db.Dispose();
}
