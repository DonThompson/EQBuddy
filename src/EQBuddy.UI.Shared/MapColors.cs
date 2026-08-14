namespace EQBuddy.UI.Shared;

/// <summary>Map-pack colors, made safe for a dark UI. Shared because the desktop map
/// and the phone's map must lift the same lines the same way.</summary>
public static class MapColors
{
    /// <summary>Map packs assume the game's parchment map window; a pure-black line
    /// vanishes on our dark theme. Lines darker than the floor are lifted to a
    /// readable gray, everything else keeps its pack color.</summary>
    public static (byte R, byte G, byte B) Readable(byte r, byte g, byte b) =>
        r * 2 + g * 5 + b < 300 ? ((byte)170, (byte)170, (byte)170) : (r, g, b);
}
