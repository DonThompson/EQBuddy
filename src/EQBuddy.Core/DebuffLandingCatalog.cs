using System.Reflection;
using System.Text.Json;

namespace EQBuddy.Core;

/// <summary>
/// Detrimental cast-on-you lines, keyed by the exact message the game prints.
///
/// Its one job is naming what displaced a buff. <see cref="BuffLossLog"/> could already
/// blame a hostile landing when a set buff went missing, but the only evidence it had
/// was a named damage line or a slow — so a pure debuff was invisible. Malaise drops
/// your strength and AC, deals no damage and is not a slow; Frankthetankk documented it
/// displacing Elemental Shield (#120), and the log's only trace of it is the flavor line
/// "You feel somewhat vulnerable."
///
/// Several debuffs share a line, so entries keep every candidate and the loss entry says
/// what the line means rather than picking one of them. Generated from the eqlwiki spell
/// harvest by scripts/harvests/eqlwiki/debuffs-harvest.py, which also guarantees these
/// messages collide with no other exact-match catalog — one line must never parse as two
/// different events.
/// </summary>
public sealed class DebuffLandingCatalog
{
    public sealed class Entry
    {
        public string Message { get; set; } = "";
        public string[] Spells { get; set; } = [];

        /// <summary>What to call it in a loss entry: the spell when a line means exactly
        /// one, else every candidate — "Malaise" against "Cripple / Incapacitate". A
        /// shared line genuinely does not tell you which landed, and inventing certainty
        /// there would put a wrong cause in a bug report.</summary>
        public string Label => Spells.Length == 1 ? Spells[0] : string.Join(" / ", Spells);
    }

    private sealed class Root { public List<Entry> Messages { get; set; } = []; }

    private readonly Dictionary<string, Entry> _byMessage;

    public DebuffLandingCatalog(IEnumerable<Entry> entries) =>
        _byMessage = entries.Where(e => e.Message.Length > 0 && e.Spells.Length > 0)
            .ToDictionary(e => e.Message, e => e, StringComparer.OrdinalIgnoreCase);

    public int Count => _byMessage.Count;

    /// <summary>The entry for this exact line, or null. One hash probe per log line.</summary>
    public Entry? Find(string message) =>
        _byMessage.TryGetValue(message.Trim(), out var e) ? e : null;

    public static DebuffLandingCatalog Default { get; } = LoadEmbedded();

    private static DebuffLandingCatalog LoadEmbedded()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("DebuffLandings.json", StringComparison.Ordinal));
            if (name is null) return new DebuffLandingCatalog([]);
            using var stream = asm.GetManifestResourceStream(name)!;
            var root = JsonSerializer.Deserialize<Root>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return new DebuffLandingCatalog(root?.Messages ?? []);
        }
        catch (Exception ex)
        {
            // A missing or malformed catalog costs a cause on a loss entry, never a
            // crash — the rest of the loss history still works.
            CoreLog.Error(ex);
            return new DebuffLandingCatalog([]);
        }
    }
}
