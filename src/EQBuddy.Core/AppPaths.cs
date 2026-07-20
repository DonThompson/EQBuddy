namespace EQBuddy.Core;

/// <summary>
/// Where EQBuddy keeps settings, history, and logs. The EQBUDDY_APPDATA environment
/// variable overrides it — an isolated profile for testing without touching real data.
/// </summary>
public static class AppPaths
{
    public static string Dir =>
        Environment.GetEnvironmentVariable("EQBUDDY_APPDATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EQBuddy");

    public static string File(string name) => Path.Combine(Dir, name);
}
