using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>
/// Perf audit #13: LogWatcher splits each line ONCE (TrySplitLine) and hands the
/// parts to Parse and ObserveRawLine, instead of both re-running the line regex and
/// timestamp parse. These tests pin the contract that made that safe: the split path
/// accepts exactly the lines Parse accepts — including the single-digit-day stamps
/// whose double space used to be normalized only inside Parse — and the split parts
/// reproduce identical events.
/// </summary>
public class SplitOnceParserTests
{
    private static string FixturePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "eqlog_Testchar_fixture.txt"));

    [Fact]
    public void SingleDigitDayStampsSplitAndParseIdentically()
    {
        // Real logs pad single-digit days with a double space.
        const string line = "[Thu Aug  6 14:43:24 2026] You died.";

        Assert.True(LogParser.TrySplitLine(line, out var ts, out var msg));
        Assert.Equal(new DateTime(2026, 8, 6, 14, 43, 24), ts);
        Assert.Equal("You died.", msg);

        var whole = LogParser.Parse(line);
        var split = LogParser.Parse(ts, msg);
        Assert.IsType<DeathEvent>(whole);
        Assert.IsType<DeathEvent>(split);
        Assert.Equal(whole, split);   // records: value equality
        Assert.Equal(ts, split!.Time);
    }

    [Fact]
    public void SplitAndWholeLineParsingAgreeOnEveryFixtureLine()
    {
        var parsed = 0;
        foreach (var line in File.ReadLines(FixturePath))
        {
            var whole = LogParser.Parse(line);
            var ok = LogParser.TrySplitLine(line, out var ts, out var msg);
            if (whole is null)
            {
                // A line Parse rejects must not be one the split path would have
                // fed the pipeline an event for.
                if (ok) Assert.Null(LogParser.Parse(ts, msg));
                continue;
            }
            parsed++;
            Assert.True(ok);
            Assert.Equal(whole.Time, ts);
            var split = LogParser.Parse(ts, msg);
            Assert.NotNull(split);
            Assert.Equal(whole.GetType(), split!.GetType());
            Assert.Equal(whole.ToString(), split.ToString());
        }
        Assert.True(parsed > 50, $"fixture should exercise the parser broadly (parsed {parsed})");
    }

    [Fact]
    public void ReplayThroughTheSplitPathMatchesWholeLineReplay()
    {
        var wholeStats = new SessionStats { CharacterName = "Testchar" };
        var splitStats = new SessionStats { CharacterName = "Testchar" };
        foreach (var line in File.ReadLines(FixturePath))
        {
            if (LogParser.Parse(line) is { } evt) wholeStats.Apply(evt);
            wholeStats.ObserveRawLine(line);

            // The LogWatcher split-once shape.
            if (LogParser.TrySplitLine(line, out var ts, out var msg))
            {
                if (LogParser.Parse(ts, msg) is { } evt2) splitStats.Apply(evt2);
                splitStats.ObserveRawLine(ts, msg);
            }
        }
        var a = wholeStats.Snapshot();
        var b = splitStats.Snapshot();
        Assert.Equal(a.Version, b.Version);   // same number of applied events
        Assert.Equal(a.YourKillCount, b.YourKillCount);
        Assert.Equal(a.DamageDealt, b.DamageDealt);
        Assert.Equal(a.LootTotal, b.LootTotal);
        Assert.Equal(a.XpPercent, b.XpPercent);
        Assert.Equal(a.SessionStart, b.SessionStart);
        Assert.Equal(a.LastEventTime, b.LastEventTime);
        Assert.Equal(wholeStats.RecentLines().Count, splitStats.RecentLines().Count);
    }
}
