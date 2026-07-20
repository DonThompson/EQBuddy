namespace EQBuddy.Core;

/// <summary>
/// REL-003: Core has no UI dependency, so unexpected failures are routed through a sink
/// the host application wires up (both UIs point this at their error.log writer).
/// Never throws — logging must not break parsing.
/// </summary>
public static class CoreLog
{
    public static Action<object?>? Sink { get; set; }

    public static void Error(object? error)
    {
        try { Sink?.Invoke(error); }
        catch { /* logging must never cascade */ }
    }
}
