using System.Globalization;
using System.Text;

namespace EQBuddy.E2E;

/// <summary>
/// C# port of scripts/make-test-session.ps1: rewrites the fixture log's timestamps so
/// it ends one minute ago, with idle gaps over 45 minutes compressed to 3 — one rich,
/// live-looking session. Ported rather than shelled out so the suite has no pwsh
/// dependency and each test can stamp its own copy.
/// </summary>
internal static class FixtureLog
{
    /// <summary>The EQ log timestamp shape: "Mon Jul 20 16:38:00 2026".</summary>
    public const string TimestampFormat = "ddd MMM dd HH:mm:ss yyyy";

    public static string Stamp(DateTime time, string message) =>
        $"[{time.ToString(TimestampFormat, CultureInfo.InvariantCulture)}] {message}";

    /// <summary>
    /// Writes the shifted fixture as eqlog_{character}_{server}.txt under
    /// <paramref name="logFolder"/> and returns the target path. Latin1, matching what
    /// LogWatcher reads — a UTF-8 BOM would corrupt the first line's timestamp.
    /// </summary>
    public static string WriteShifted(string fixturePath, string logFolder,
        string character = "Testchar", string server = "test")
    {
        var events = new List<(DateTime Time, string Message)>();
        foreach (var line in File.ReadLines(fixturePath))
        {
            if (line.Length < 27 || line[0] != '[') continue;
            var close = line.IndexOf(']');
            if (close < 0) continue;
            var time = DateTime.ParseExact(line[1..close], TimestampFormat,
                CultureInfo.InvariantCulture);
            events.Add((time, line[(close + 2)..]));
        }
        if (events.Count == 0)
            throw new InvalidOperationException($"No timestamped lines in fixture: {fixturePath}");

        var shift = TimeSpan.Zero;
        var shifted = new List<(DateTime Time, string Message)>(events.Count);
        for (var i = 0; i < events.Count; i++)
        {
            if (i > 0)
            {
                var gap = events[i].Time - events[i - 1].Time;
                if (gap > TimeSpan.FromMinutes(45)) shift += gap - TimeSpan.FromMinutes(3);
            }
            shifted.Add((events[i].Time - shift, events[i].Message));
        }

        var delta = DateTime.Now.AddMinutes(-1) - shifted[^1].Time;
        Directory.CreateDirectory(logFolder);
        var target = Path.Combine(logFolder, $"eqlog_{character}_{server}.txt");
        File.WriteAllLines(target,
            shifted.Select(e => Stamp(e.Time + delta, e.Message)), Encoding.Latin1);
        return target;
    }
}
