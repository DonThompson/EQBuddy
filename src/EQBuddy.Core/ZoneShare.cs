using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace EQBuddy.Core;

/// <summary>
/// Zone-knowledge share strings ("EQBZ1-…"): a zone's spawn-point archive plus its
/// learned respawn timers, as one paste-safe string — the community layer's phase 1
/// (David, 2026-08-13: streamlined collaboration from within the community, no
/// server, nothing leaves your machine unless you paste it somewhere yourself).
///
/// Import is preview-first and add-only for observations. Timer values pass a
/// DEVIATION GATE before they're applied automatically: a submitted timer that
/// strays far from the zone's established clock (the Befallen-is-about-4:30 test)
/// is flagged for the importer to accept deliberately or leave — tanked timers
/// can't slip in quietly, here or in the review queue upstream.
/// </summary>
public static class ZoneShare
{
    public const string Prefix = "EQBZ1-";

    /// <summary>A timer whose value differs from the current effective one by more
    /// than this fraction is flagged rather than auto-applied.</summary>
    public const double DeviationFlagFraction = 0.40;

    public sealed class Payload
    {
        public string Zone { get; set; } = "";
        public List<SpawnPointLedger.SpawnPoint> Points { get; set; } = [];
        /// <summary>Learned respawn seconds by mob name — only LEARNED values travel;
        /// a sharer's manual edits are their own taste, not evidence.</summary>
        public Dictionary<string, double> LearnedTimers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed record TimerDiff(string Name, double? CurrentSeconds, double IncomingSeconds, bool Flagged);

    public sealed class Preview
    {
        public Payload Payload { get; init; } = new();
        public int NewPoints { get; init; }
        public int RefinedPoints { get; init; }
        public int NewObservations { get; init; }
        public List<TimerDiff> Timers { get; init; } = [];
        public List<TimerDiff> FlaggedTimers => Timers.Where(t => t.Flagged).ToList();
    }

    public static string Export(SpawnPointLedger.ZoneArchive archive, SpawnZone? zone, SpawnOverrides overrides)
    {
        var payload = new Payload { Zone = archive.Zone, Points = archive.Points };
        if (zone is not null)
            foreach (var entry in zone.Named)
                if (overrides.Find(archive.Zone, entry.Name) is { Learned: true, RespawnSeconds: { } s })
                    payload.LearnedTimers[entry.Name] = s;

        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(json);
        return Prefix + Convert.ToBase64String(buffer.ToArray())
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static Preview? PreviewImport(string shareString,
        SpawnPointLedger.ZoneArchive local, SpawnZone? zone, SpawnOverrides overrides)
    {
        Payload payload;
        try
        {
            var trimmed = shareString.Trim();
            if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            var b64 = trimmed[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            var padded = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            using var buffer = new MemoryStream(Convert.FromBase64String(padded));
            using var inflate = new DeflateStream(buffer, CompressionMode.Decompress);
            payload = JsonSerializer.Deserialize<Payload>(inflate) ?? new Payload();
        }
        catch { return null; }
        if (payload.Zone.Length == 0) return null;

        int newPoints = 0, refined = 0, newObs = 0;
        foreach (var incoming in payload.Points)
        {
            var mine = local.Points.FirstOrDefault(p =>
                Math.Sqrt(Math.Pow(p.LocY - incoming.LocY, 2) + Math.Pow(p.LocX - incoming.LocX, 2))
                    <= SpawnPointLedger.ClusterRadius);
            if (mine is null) { newPoints++; newObs += incoming.TotalKills(); }
            else
            {
                refined++;
                foreach (var (name, seen) in incoming.Mobs)
                    newObs += Math.Max(0, seen.Kills - (mine.Mobs.GetValueOrDefault(name)?.Kills ?? 0));
            }
        }

        var timers = new List<TimerDiff>();
        foreach (var (name, incoming) in payload.LearnedTimers)
        {
            var entry = zone?.Named.FirstOrDefault(e => SpawnCatalog.NameMatches(e.Name, name));
            var current = overrides.Find(payload.Zone, name)?.RespawnSeconds
                ?? (entry is not null && zone is not null ? SpawnCatalog.EffectiveSeconds(zone, entry) : null);
            // The deviation gate: no baseline = flagged (nothing to corroborate);
            // a big swing from the established clock = flagged.
            var flagged = current is not { } cur
                || Math.Abs(incoming - cur) / Math.Max(1, cur) > DeviationFlagFraction;
            timers.Add(new TimerDiff(name, current, incoming, flagged));
        }

        return new Preview
        {
            Payload = payload, NewPoints = newPoints, RefinedPoints = refined,
            NewObservations = newObs, Timers = timers,
        };
    }

    /// <summary>Apply a previewed import: observations merge ADD-ONLY (counts take
    /// the max of both sides — re-importing the same string twice adds nothing);
    /// unflagged timers apply as learned overrides; flagged ones only when the
    /// importer explicitly said so.</summary>
    public static void Apply(Preview preview, SpawnPointLedger.ZoneArchive local,
        SpawnOverrides overrides, bool includeFlagged)
    {
        foreach (var incoming in preview.Payload.Points)
        {
            var mine = local.Points.FirstOrDefault(p =>
                Math.Sqrt(Math.Pow(p.LocY - incoming.LocY, 2) + Math.Pow(p.LocX - incoming.LocX, 2))
                    <= SpawnPointLedger.ClusterRadius);
            if (mine is null)
            {
                local.Points.Add(incoming);
                continue;
            }
            foreach (var (name, seen) in incoming.Mobs)
            {
                var ours = mine.Mobs.TryGetValue(name, out var m) ? m
                    : mine.Mobs[name] = new SpawnPointLedger.MobSeen();
                ours.Kills = Math.Max(ours.Kills, seen.Kills);
                if (seen.LastKill > ours.LastKill) ours.LastKill = seen.LastKill;
            }
        }

        foreach (var diff in preview.Timers)
        {
            if (diff.Flagged && !includeFlagged) continue;
            // Never overwrite the importer's own MANUAL edit — their number wins
            // over anyone's, always.
            var existing = overrides.Find(preview.Payload.Zone, diff.Name);
            if (existing is { Learned: false, RespawnSeconds: not null }) continue;
            var o = overrides.GetOrAdd(preview.Payload.Zone, diff.Name);
            o.RespawnSeconds = diff.IncomingSeconds;
            o.Learned = true;
            overrides.Save();
        }
    }
}
