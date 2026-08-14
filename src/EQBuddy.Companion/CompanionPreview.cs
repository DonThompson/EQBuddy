using System.IO;
using EQBuddy.Core;

namespace EQBuddy.Companion;

/// <summary>
/// The launch gate for the second screen (David, 2026-08-14): the feature is merged so
/// it stays current with main and runs in CI, but it must not reach players until he has
/// tested it and said go. Released builds therefore never show its Options entry and
/// never open a socket — <see cref="Enabled"/> is false unless EQBUDDY_COMPANION=1 is set
/// in the environment, which no player does by accident.
///
/// This is deliberately a RUNTIME gate rather than #if: the code compiles, its tests run,
/// and the field-test build is the same binary everyone else gets. The dormant path costs
/// one environment read at startup.
///
/// When the feature launches: delete this class, drop the guard in CompanionHost, and
/// un-collapse SecondScreenBlock in OptionsWindow.xaml — plus a What's-new entry, per
/// CONTRIBUTING's rule that a player-visible change never ships silently.
/// </summary>
public static class CompanionPreview
{
    public const string EnvVar = "EQBUDDY_COMPANION";

    /// <summary>The marker file that opts a machine in: an empty file named this, beside
    /// settings.json in the EQBuddy app-data folder. A FILE rather than only an env var
    /// because a process reads the environment it INHERITED — EQBuddy relaunched by the
    /// installer never saw a variable set afterwards, and the feature stayed hidden with
    /// no clue why (field-hit 2026-08-14). A file does not care how the app was launched,
    /// it survives restarts, it can be inspected, and a released build never writes one.</summary>
    public const string MarkerFile = "mobile-preview.enabled";

    /// <summary>True when the marker file exists, or the env var is set (kept for CI and
    /// one-off dev runs). Read once — flipping it means restarting, which is the honest
    /// contract for a build-level preview switch.</summary>
    public static bool Enabled { get; } = Read();

    /// <summary>Where the marker lives — the same folder settings.json uses, so the
    /// isolated-profile dev flow (EQBUDDY_APPDATA) gets its own answer for free.</summary>
    public static string MarkerPath => AppPaths.File(MarkerFile);

    private static bool Read()
    {
        try { return IsOptedIn(Environment.GetEnvironmentVariable(EnvVar), File.Exists(MarkerPath)); }
        catch { return false; }   // an unreadable profile folder is not an opt-in
    }

    /// <summary>The decision itself, free of the machine it runs on — the seam the tests
    /// use, because the developer's own box IS opted in and a test asserting "off here"
    /// would only ever prove where it ran.</summary>
    public static bool IsOptedIn(string? envValue, bool markerExists) =>
        markerExists
        || (envValue is not null
            && (envValue.Equals("1", StringComparison.Ordinal)
                || envValue.Equals("true", StringComparison.OrdinalIgnoreCase)
                || envValue.Equals("yes", StringComparison.OrdinalIgnoreCase)));
}
