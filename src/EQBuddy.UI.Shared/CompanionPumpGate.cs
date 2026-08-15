namespace EQBuddy.UI.Shared;

/// <summary>
/// Decides whether EQBuddy Mobile should be pushed to right now.
///
/// The desktop redraws at 1 Hz because that is how often a human wants a card to change
/// under their eyes. A phone showing a mez breaking wants to know as soon as the log
/// does, and until 1.85.0 it rode that same 1 Hz redraw — so up to a second of delay
/// existed for no reason but shared plumbing, on top of the tailer's 150 ms poll.
///
/// A faster pump is only safe because it is nearly always a no-op, and this class is
/// where "nearly always" is decided:
///
/// - **Nobody paired?** Nothing happens. The whole feature costs one bool read.
/// - **Nothing new?** Nothing happens. The session version only moves when an event was
///   actually applied, so a quiet camp pumps nothing however fast the timer runs.
///
/// Both guards are cheap by construction, which is what lets the interval be small.
///
/// It lives here rather than in the window because the window has no test project
/// (docs/TestPlan.md §5), and "is this free when idle" is precisely the claim that must
/// not be taken on trust: get it wrong and EQBuddy rebuilds a snapshot twenty times a
/// second forever, for nobody.
/// </summary>
public sealed class CompanionPumpGate
{
    private long _pushedVersion = -1;

    /// <summary>The session version most recently pushed, or -1 before the first push.</summary>
    public long PushedVersion => _pushedVersion;

    /// <summary>
    /// Should this pump tick do any work? Claims the version when the answer is yes, so
    /// two ticks can never both push the same state.
    /// </summary>
    public bool ShouldPush(bool hasClients, long version)
    {
        if (!hasClients) return false;
        if (version == _pushedVersion) return false;
        _pushedVersion = version;
        return true;
    }

    /// <summary>
    /// Record a push made by somebody else — the 1 Hz reconciliation tick, which pushes
    /// whether or not the version moved. Without this the pump would immediately repeat
    /// a push that had just gone out.
    /// </summary>
    public void Observe(long version) => _pushedVersion = version;

    // There is deliberately no Reset() for "a device just connected". CompanionServer
    // keeps the last published snapshot and sends it to a client the moment it attaches,
    // so a fresh or reconnecting phone is already served without the pump knowing it
    // exists. Adding a reset here would re-push state the device had before it asked.
}
