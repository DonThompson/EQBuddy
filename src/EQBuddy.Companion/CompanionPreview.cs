namespace EQBuddy.Companion;

/// <summary>
/// The launch gate for EQBuddy Mobile (David, 2026-08-14): the feature is merged so
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

    /// <summary>True only when the preview env var is explicitly set to 1/true/yes.</summary>
    public static bool Enabled { get; } = Read();

    private static bool Read()
    {
        var v = Environment.GetEnvironmentVariable(EnvVar);
        return v is not null
            && (v.Equals("1", StringComparison.Ordinal)
                || v.Equals("true", StringComparison.OrdinalIgnoreCase)
                || v.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }
}
