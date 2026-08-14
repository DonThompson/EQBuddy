using System.Security.Cryptography;
using EQBuddy.Core;

namespace EQBuddy.Companion;

/// <summary>
/// The desktop-side lifecycle glue, UI-toolkit-free so WPF and Avalonia hosts share
/// it: owns the server per AppSettings (enabled/port/token/surface gate), and turns
/// the app's once-a-second tick into pushes — only when something moved, and only
/// when a phone is actually connected. With the feature off or nobody paired, Tick
/// is two field reads.
/// </summary>
public sealed class CompanionHost : IDisposable
{
    /// <summary>Belt-and-braces refresh: even with an unchanged fingerprint, connected
    /// phones get a full snapshot this often so slow-drifting numbers (xp/hr, session
    /// length) never stagnate through a quiet camp.</summary>
    private static readonly TimeSpan ForcedPushInterval = TimeSpan.FromSeconds(30);

    private readonly AppSettings _settings;
    private readonly string _appVersion;
    private CompanionServer? _server;
    private string _lastFingerprint = "";
    private DateTime _lastPush = DateTime.MinValue;

    public CompanionHost(AppSettings settings, string appVersion)
    {
        _settings = settings;
        _appVersion = appVersion;
        if (settings.CompanionEnabled) Start();
    }

    public bool Running => _server is not null;
    public int ClientCount => _server?.ClientCount ?? 0;

    /// <summary>Why the last Start failed (port in use, no permission), for the
    /// pairing window to show honestly; null when all is well.</summary>
    public string? LastError { get; private set; }

    /// <summary>Raised (possibly on a worker thread) when a phone connects/drops.</summary>
    public event Action? ClientsChanged;

    /// <summary>The address to pair against: http://ip:port/#token. Null while stopped.
    /// The token travels in the FRAGMENT, so it never appears in an HTTP request line —
    /// only the page's JS reads it and presents it on the WebSocket connect.</summary>
    public string? PairingUrl =>
        _server is { BoundAddresses.Count: > 0 } s
            ? $"http://{s.BoundAddresses[0]}:{s.Port}/#{_settings.CompanionToken}"
            : null;

    /// <summary>The desktop gate: every known surface minus the ones the owner
    /// unticked (persisted as the hidden-list, same idiom as HiddenSections).</summary>
    public IReadOnlyList<string> OfferedSurfaces =>
        CompanionSurfaces.All
            .Where(s => !_settings.CompanionHiddenSurfaces.Contains(s, StringComparer.OrdinalIgnoreCase))
            .ToList();

    public void SetEnabled(bool enabled)
    {
        if (enabled == _settings.CompanionEnabled && enabled == Running) return;
        _settings.CompanionEnabled = enabled;
        _settings.Save();
        if (enabled) Start(); else Stop();
    }

    /// <summary>New token, old phones revoked: the server restarts under the new
    /// token, which drops every open connection and refuses their reconnects.</summary>
    public void RegenerateToken()
    {
        _settings.CompanionToken = MintToken();
        _settings.Save();
        if (Running) { Stop(); Start(); }
    }

    /// <summary>Flip one surface in the desktop gate; connected phones learn on the
    /// next tick (the offer list is part of the push fingerprint).</summary>
    public void SetSurfaceOffered(string surface, bool offered)
    {
        var hidden = _settings.CompanionHiddenSurfaces;
        var present = hidden.Contains(surface, StringComparer.OrdinalIgnoreCase);
        if (offered && present) hidden.RemoveAll(s => string.Equals(s, surface, StringComparison.OrdinalIgnoreCase));
        else if (!offered && !present) hidden.Add(surface);
        else return;
        _settings.Save();
    }

    private void Start()
    {
        // The hard half of the preview gate: even a settings file carrying
        // CompanionEnabled=true (copied from a field-test machine, or hand-edited)
        // opens no socket in a released build. The Options entry is hidden too, but
        // this is the guard that matters — it is the one standing between a dormant
        // feature and a listening port.
        if (!CompanionPreview.Enabled) return;
        LastError = null;
        _settings.CompanionToken ??= MintToken();
        try
        {
            var server = new CompanionServer(new CompanionServerOptions
            {
                Token = _settings.CompanionToken,
                Port = _settings.CompanionPort,
            });
            server.ClientsChanged += () => ClientsChanged?.Invoke();
            server.Start();
            _server = server;
        }
        catch (Exception ex)
        {
            CoreLog.Error(ex);
            LastError = $"Couldn't listen on port {_settings.CompanionPort} — {ex.Message} " +
                        "(is another program using it? Change the port and try again.)";
        }
        _lastFingerprint = "";
    }

    private void Stop()
    {
        _server?.Dispose();
        _server = null;
    }

    /// <summary>Once-a-second feed from the app's existing UI tick, handing over the
    /// SAME shared snapshot the desktop cards render from (the perf pass's rule: one
    /// snapshot per tick, no extras built for the companion). Takes SpawnTimers
    /// itself, not a list, so its Snapshot() isn't even taken while nobody's paired.</summary>
    public void Tick(StatsSnapshot? stats, SpawnTimers spawnTimers, string character, DateTime now)
    {
        if (_server is not { ClientCount: > 0 } server) return; // zero cost while idle

        var snap = CompanionProjection.Build(
            stats, spawnTimers.Snapshot(now), character, _appVersion, now, OfferedSurfaces);
        var fingerprint = CompanionProjection.Fingerprint(snap);
        if (fingerprint == _lastFingerprint && now - _lastPush < ForcedPushInterval) return;
        _lastFingerprint = fingerprint;
        _lastPush = now;
        server.Publish(snap);
    }

    /// <summary>128 crypto-random bits as lowercase hex — long enough that guessing
    /// races the per-IP rate limiter for longer than the universe cares to wait,
    /// short enough to keep the QR at version ≤ 4.</summary>
    private static string MintToken() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    public void Dispose() => Stop();
}
