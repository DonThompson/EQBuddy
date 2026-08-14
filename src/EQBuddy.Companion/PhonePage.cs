using System.Reflection;

namespace EQBuddy.Companion;

/// <summary>The embedded phone page. One self-contained HTML file (inline CSS/JS, no
/// CDN, nothing fetched from the internet — the phone talks to the PC and only the
/// PC). Loaded once; the server serves the same bytes to every GET /.
///
/// Beside it: the web app manifest and its icon, which is what turns "Add to Home
/// Screen" into a chrome-free window (David's tablet ask, 2026-08-14). Both are
/// static, contain no player data, and are the only other things the unauthenticated
/// HTTP surface will hand out.</summary>
public static class PhonePage
{
    public static string Html { get; } = Load();

    /// <summary>The manifest. Colors are the default palette rather than the live
    /// theme: the launcher paints the splash before any WebSocket exists, so there is
    /// no live palette to read at that moment. The page retints itself the instant it
    /// connects.
    ///
    /// start_url deliberately carries no pairing token — this file is served without
    /// authentication, and a token in it would be a token anyone on the network could
    /// read. A home-screen launch reconnects from the code the device remembered when
    /// it paired (see the page's token handling).</summary>
    public static string Manifest { get; } =
        """
        {
          "name": "EQBuddy Mobile",
          "short_name": "EQBuddy",
          "description": "A live EQBuddy display on your phone or tablet.",
          "display": "standalone",
          "orientation": "any",
          "start_url": "./",
          "scope": "./",
          "background_color": "#16130e",
          "theme_color": "#16130e",
          "icons": [
            { "src": "icon.png", "sizes": "180x180", "type": "image/png", "purpose": "any" }
          ]
        }
        """;

    /// <summary>The home-screen icon: a spawn-point circle in brass on the dark
    /// parchment ground — the same shape the map draws. 180×180 PNG, which is both the
    /// size iOS wants for apple-touch-icon and small enough to carry in source.</summary>
    public static byte[] Icon { get; } = Convert.FromBase64String(IconBase64);

    private const string IconBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAALQAAAC0CAIAAACyr5FlAAADaUlEQVR42u3dwY3VQBAE0I6AC9LPh5gJiURIgAuCtaurn1QBeMpv21p/ezyf799E/phRgcAhcAgcAofAIXAIHAKHwCFwiMAhcAgcAofAIXAIHAKHwFGVXz9//FXgQAGXAzi+QsNBK8MEJc04EkxUKhksEGnDkc+igMgwQUkDju0y1vkYLBDZiqOPxSIiQwYfy3BcYJFPZMjgYweOzJNxlsgckXH2UBtw7G252MdUyrCEEhxlhTYtZzqqbP2H6y6O+ptI2xc4e4u783vhLRwHH4/YuORRk4Wn4PDs7qIGRi98vI+DjHVtjC74WInDq5r9OMhY2s+QwccmHHaFOIGDjNV1jQuKi8sOHJdlBJY2ZPCRjoOJwPaGDD6ew+GCUlPjGBuGRygOApLLHGPD8EjE4dyHVzrGhuERh8NZzy92jA3DIwuH872iXjjg+GIcZFT6gAMOOP7+4OGYmzKW/m/18DGfw7H69apmHKtlrFsCHM+xWLcQOF6QsWUt7+PIL7RsN9knj3C6x0blbtRwwAHHZhn564LjTRnhS4MDjkgcsQ12f0rnsQMbY8PwgAMOOOCAI0pG8hrhgAMOOOCAAw444IADDjjggAMON8HcBIMDDjjg8JO9n+xzcBgeHvaBAw4PGHvA2KsJXk2AA449OLwOee51SC9Se5HaFgy2YLB5i81bbPtk2yc4/huRjYcdgeNjq8lGGXBsChxSh4OPPhlwwJGHg48V3fqMl7GRh4OP/FZ9OtTYiMTBR3ifPldubKTi4CO5yfdx8BFb41SSNzZycfDR0d4UT0UXlFAcfBSUloXjuI+0xubO34GxkYWDj9VFzbVR6YIShIOPvf3M8fWTsRVHN5H8TkYXZLyPg491PYxeNJCC4/PPr7rfZPHKwkdNlpyF41OxVUb9Smd1a+FEti9wCurbvl1C7LqmpscQIk3LmbJCa3ZLTjgvUzaHn++3aR/LUByfL95t+OyhluD4pH7qoPgDDJtwPH8ychJ4Iqb4DgEZnTjuEEnuPxpHvY/w8tNxtBJZUfsOHE1EFhW+CUeBj11tL8OxVMnSkrfi2EJkdb27cSQTKSi2AUeUkqY+q3C8qKSyxk4cd35Vh+N9LjdbOopD4BA4BA6BQ+AQOAQOgUPgEIFD4BA4BA6BQ+AQOAQOgUPgkNP5Ddx5S4uTXAGFAAAAAElFTkSuQmCC";

    private static string Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("EQBuddy.Companion.Web.index.html")
            ?? throw new InvalidOperationException("Embedded phone page missing — check the csproj EmbeddedResource.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
