namespace EQBuddy.UI.Shared;

/// <summary>
/// What the ↻ Reset button says about itself — its tooltip, and the confirmation it asks
/// before running.
///
/// The button was labelled "Reset session stats", which reads as "clear the numbers on
/// screen". With archiving on it also MOVES your live log into Logs\archive and starts a
/// fresh one. Frankthetankk raised the mismatch in #159, having just lost a session to
/// the idle cleanup: a button that touches a file on disk should say so before it does.
///
/// Nothing here is deleted, and the wording is careful to say that rather than leave the
/// player to infer it — the fear the report was written from was of losing data, and
/// "moved to Logs\archive" answers that in a way "reset" never could.
///
/// The text depends on a setting, which is exactly why it lives here instead of being
/// baked into XAML: with archiving OFF the log is genuinely untouched, and claiming
/// otherwise would be its own kind of lie.
/// </summary>
public static class ResetPrompt
{
    /// <summary>Hover text for the ↻ button.</summary>
    public static string Tooltip(bool archiveLogs) => archiveLogs
        ? "Start a new session\nStats reset to zero, and your current log moves to "
          + "Logs\\archive so a fresh one can begin. Nothing is deleted."
        : "Start a new session\nStats reset to zero. Your log file is left alone "
          + "(archiving is off in Options).";

    /// <summary>Body of the confirmation dialog. Null when no confirmation is warranted —
    /// with archiving off this only clears numbers you can rebuild by reloading the
    /// session, so a dialog would be a speed bump with nothing behind it.</summary>
    public static string? Confirmation(bool archiveLogs) => archiveLogs
        ? "Start a new session?\n\n"
          + "Session stats reset to zero, and your current log is moved to Logs\\archive "
          + "with a timestamp so a fresh log can begin.\n\n"
          + "Nothing is deleted — the archived file stays until you remove it yourself."
        : null;

    public const string ConfirmationTitle = "Reset session";
}
