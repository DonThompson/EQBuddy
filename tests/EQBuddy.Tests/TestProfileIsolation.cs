using System.Runtime.CompilerServices;

namespace EQBuddy.Tests;

/// <summary>
/// Points every test at a throwaway profile folder before a single test runs.
///
/// Why this exists: AppSettings.Save(), the ledgers, and the learned stores all write
/// under AppPaths.Dir, which is the REAL %AppData%\EQBuddy unless EQBUDDY_APPDATA says
/// otherwise. A test that constructed settings and called Save() therefore overwrote a
/// live install's settings.json — LogFolder, watch rules, and checklists gone (hit on
/// David's own machine, 2026-08-14, while running the suite). Reviewing each test for
/// stray writes is the fragile fix; making the writes land somewhere harmless is the
/// durable one.
///
/// A module initializer runs before any test type is touched, so the redirect is in
/// place for static initializers too (CompanionPreview reads its marker path once). An
/// EQBUDDY_APPDATA already set by the harness or a developer wins — this only fills the
/// gap, and the folder is left behind deliberately: a failed test's artifacts are
/// evidence, and the OS reclaims temp.
/// </summary>
internal static class TestProfileIsolation
{
    [ModuleInitializer]
    internal static void RedirectProfileToTemp()
    {
        if (Environment.GetEnvironmentVariable("EQBUDDY_APPDATA") is { Length: > 0 }) return;
        var dir = Path.Combine(Path.GetTempPath(), "eqbuddy-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("EQBUDDY_APPDATA", dir);
    }
}
