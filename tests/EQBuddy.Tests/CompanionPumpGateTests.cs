using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The gate in front of EQBuddy Mobile's 20 Hz pump (1.85.0).
///
/// Mobile used to ride the desktop's 1 Hz redraw, which put up to a second between a log
/// line being parsed and a phone hearing about it. Pumping twenty times a second closes
/// that — but only stays affordable because it is almost always a no-op, and "almost
/// always" is this class's whole job. These tests are the reason that claim can be made
/// out loud: a regression here doesn't break a feature, it quietly burns a core.
/// </summary>
public class CompanionPumpGateTests
{
    [Fact]
    public void NobodyPairedMeansNoWork()
    {
        var gate = new CompanionPumpGate();

        Assert.False(gate.ShouldPush(hasClients: false, version: 42));
    }

    [Fact]
    public void AnUnpairedTickDoesNotConsumeTheVersion()
    {
        // Otherwise pairing a phone mid-session would leave the gate believing it had
        // already sent whatever arrived while nobody was listening.
        var gate = new CompanionPumpGate();
        gate.ShouldPush(hasClients: false, version: 42);

        Assert.True(gate.ShouldPush(hasClients: true, version: 42));
    }

    [Fact]
    public void AMovedSessionPushesOnce()
    {
        var gate = new CompanionPumpGate();

        Assert.True(gate.ShouldPush(true, 1));
        Assert.False(gate.ShouldPush(true, 1));   // same state, nothing to say
        Assert.True(gate.ShouldPush(true, 2));
    }

    /// <summary>
    /// The load-bearing one. A quiet camp holds one version for minutes; at 20 Hz that is
    /// thousands of ticks that must each cost a comparison and nothing else.
    /// </summary>
    [Fact]
    public void AQuietSessionNeverPushesHoweverManyTicksRun()
    {
        var gate = new CompanionPumpGate();
        Assert.True(gate.ShouldPush(true, 7));    // the one real push

        var pushes = 0;
        for (var i = 0; i < 10_000; i++)
            if (gate.ShouldPush(true, 7)) pushes++;

        Assert.Equal(0, pushes);
    }

    [Fact]
    public void TheOneHertzTickSuppressesAnImmediateRepeat()
    {
        // RefreshUi pushes on its own schedule (it drives ForcedPushInterval), so it
        // tells the gate what it covered. Without that, the very next pump tick would
        // send the same state again.
        var gate = new CompanionPumpGate();
        gate.Observe(version: 5);

        Assert.False(gate.ShouldPush(true, 5));
        Assert.True(gate.ShouldPush(true, 6));
    }

    [Fact]
    public void TheFirstPushEverAlwaysHappens()
    {
        // Version 0 is a real version — a session that has applied nothing yet still
        // deserves its first frame, so the "nothing pushed" sentinel must not collide.
        Assert.True(new CompanionPumpGate().ShouldPush(true, 0));
        Assert.Equal(-1, new CompanionPumpGate().PushedVersion);
    }
}
