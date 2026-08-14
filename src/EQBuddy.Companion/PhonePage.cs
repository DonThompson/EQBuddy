using System.Reflection;

namespace EQBuddy.Companion;

/// <summary>The embedded phone page. One self-contained HTML file (inline CSS/JS, no
/// CDN, nothing fetched from the internet — the phone talks to the PC and only the
/// PC). Loaded once; the server serves the same bytes to every GET /.</summary>
public static class PhonePage
{
    public static string Html { get; } = Load();

    private static string Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("EQBuddy.Companion.Web.index.html")
            ?? throw new InvalidOperationException("Embedded phone page missing — check the csproj EmbeddedResource.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
