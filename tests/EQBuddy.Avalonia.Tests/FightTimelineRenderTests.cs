using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EQBuddy.Core;

namespace EQBuddy.Avalonia.Tests;

/// <summary>The fight timeline window over a canned journal slice: the shared
/// TimelineBuilder does the math; these assert the Avalonia view actually consumes it
/// (header, live tag, peak line) instead of constructing an empty shell.</summary>
[Collection("avalonia")]
public sealed class FightTimelineRenderTests : IDisposable
{
    private readonly string _profile =
        Directory.CreateTempSubdirectory("eqbuddy-timeline-").FullName;

    public FightTimelineRenderTests() =>
        Environment.SetEnvironmentVariable("EQBUDDY_APPDATA", _profile);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("EQBUDDY_APPDATA", null);
        try { Directory.Delete(_profile, recursive: true); }
        catch (Exception ex) { Console.Error.WriteLine($"profile cleanup failed: {ex.Message}"); }
    }

    private static (LastFightInfo?, List<GameEvent>, string) Fight()
    {
        var start = new DateTime(2026, 8, 13, 20, 0, 0);
        var fight = new LastFightInfo("a gnoll pup", 30, 500, 120, 0, 16.7, 0, "Killed",
            InProgress: false, ByAbility: [], HealsBySpell: [], ByIncoming: [])
        { Start = start };
        var events = new List<GameEvent>
        {
            new DamageDealtEvent(start.AddSeconds(1), "a gnoll pup", 60, DamageKind.Melee, "Slash", false),
            new DamageDealtEvent(start.AddSeconds(4), "a gnoll pup", 140, DamageKind.Melee, "Slash", true),
            new MissEvent(start.AddSeconds(6), Outgoing: true, Ability: "Slash", Reason: "dodges"),
            new DamageTakenEvent(start.AddSeconds(8), "a gnoll pup", 20, Melee: true),
            new StanceEvent(start.AddSeconds(10), "Aggressive"),
        };
        return (fight, events, "");
    }

    [AvaloniaFact]
    public void TimelineHeaderStatesTheFightAndItsPeak()
    {
        var window = new FightTimelineWindow(new AppSettings(), Fight);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text).ToList();
        Assert.Contains("a gnoll pup", texts);
        Assert.Contains(texts, t => t?.Contains("events") == true);
        Assert.Contains(texts, t => t?.StartsWith("peak ") == true);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void NoFightYetSaysSoInsteadOfDrawingALie()
    {
        var window = new FightTimelineWindow(new AppSettings(),
            () => (null, [], ""));
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "no fight yet — pull something");

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
