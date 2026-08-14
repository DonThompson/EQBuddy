using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using EQBuddy.Companion;
using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>
/// The companion server against real sockets on a loopback ephemeral port: the
/// pairing-page contract, token auth (missing/wrong/rate-limited), the WebSocket
/// round trip (connect → snapshot → publish → update), and per-client surface
/// subscriptions (two devices, different picks, each gets only its own).
/// </summary>
public class CompanionServerTests : IDisposable
{
    private const string Token = "0123456789abcdef0123456789abcdef";
    private readonly CompanionServer _server;

    public CompanionServerTests()
    {
        _server = new CompanionServer(new CompanionServerOptions
        {
            Token = Token,
            Port = 0,                                  // ephemeral — parallel-safe
            Addresses = [IPAddress.Loopback],
            HeartbeatInterval = TimeSpan.FromMilliseconds(200),
        });
        _server.Start();
    }

    public void Dispose() => _server.Dispose();

    private Uri WsUri(string? token = Token) =>
        new($"ws://127.0.0.1:{_server.Port}/ws" + (token is null ? "" : $"?token={token}"));

    private static CancellationToken Deadline(int seconds = 10) =>
        new CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token;

    private static CompanionSnapshot Snap(long version, bool offerSpawns = true, bool offerSession = true)
    {
        var offered = new List<string>();
        if (offerSpawns) offered.Add(CompanionSurfaces.Spawns);
        if (offerSession) offered.Add(CompanionSurfaces.Session);
        return CompanionProjection.Build(
            new StatsSnapshot { Version = version, CurrentZone = "Lower Guk", YourKillCount = (int)version },
            [new SpawnTimerState("legends", "Lower Guk", "Frenzied Ghoul", DateTime.Now, 600)],
            "Dranak", "1.79.0", DateTime.Now, offered);
    }

    private static async Task<JsonElement> ReceiveAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var text = new StringBuilder();
        while (true)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage) break;
        }
        return JsonDocument.Parse(text.ToString()).RootElement.Clone();
    }

    /// <summary>Next message matching, skipping heartbeats (they interleave freely).</summary>
    private static async Task<JsonElement> NextSnapshotAsync(
        ClientWebSocket ws, CancellationToken ct, Func<JsonElement, bool>? where = null)
    {
        while (true)
        {
            var msg = await ReceiveAsync(ws, ct);
            if (msg.GetProperty("kind").GetString() != "snapshot") continue;
            if (where is null || where(msg)) return msg;
        }
    }

    // ---------------- HTTP surface ----------------

    [Fact]
    public async Task PairingPage_ServesWithoutToken_AndCarriesNoData()
    {
        using var http = new HttpClient();
        var response = await http.GetAsync($"http://127.0.0.1:{_server.Port}/", Deadline());
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        // The explainer is there; the page itself contains no player data — data only
        // ever arrives over the token-checked WebSocket.
        Assert.Contains("EQBuddy Mobile (Beta)", html);
        Assert.Contains("pairing code", html);
    }

    [Fact]
    public async Task UnknownPath_Is404()
    {
        using var http = new HttpClient();
        var response = await http.GetAsync($"http://127.0.0.1:{_server.Port}/admin", Deadline());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------- auth ----------------

    [Fact]
    public async Task Ws_MissingToken_Rejected()
    {
        using var ws = new ClientWebSocket();
        await Assert.ThrowsAsync<WebSocketException>(
            () => ws.ConnectAsync(WsUri(token: null), Deadline()));
    }

    [Fact]
    public async Task Ws_WrongToken_Rejected()
    {
        using var ws = new ClientWebSocket();
        await Assert.ThrowsAsync<WebSocketException>(
            () => ws.ConnectAsync(WsUri(token: new string('f', 32)), Deadline()));
    }

    [Fact]
    public async Task Ws_RepeatedFailures_RateLimitEvenTheRightToken()
    {
        for (var i = 0; i < 5; i++)
        {
            using var bad = new ClientWebSocket();
            await Assert.ThrowsAsync<WebSocketException>(
                () => bad.ConnectAsync(WsUri(token: "wrong"), Deadline()));
        }
        // Budget burned: now even the correct token bounces until the window passes —
        // brute force can't alternate guesses with probes.
        using var good = new ClientWebSocket();
        await Assert.ThrowsAsync<WebSocketException>(
            () => good.ConnectAsync(WsUri(), Deadline()));
    }

    // ---------------- the WS round trip ----------------

    [Fact]
    public async Task Ws_ConnectGetsSnapshot_PublishPushesUpdate()
    {
        _server.Publish(Snap(version: 1));

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(WsUri(), Deadline());
        var ct = Deadline();

        var first = await NextSnapshotAsync(ws, ct);
        Assert.Equal(1, first.GetProperty("version").GetInt64());
        Assert.Equal("Dranak", first.GetProperty("identity").GetProperty("character").GetString());
        Assert.Equal("Frenzied Ghoul", first.GetProperty("spawns").GetProperty("timers")[0]
            .GetProperty("name").GetString());

        _server.Publish(Snap(version: 2));
        var second = await NextSnapshotAsync(ws, ct, m => m.GetProperty("version").GetInt64() == 2);
        Assert.Equal(2, second.GetProperty("session").GetProperty("kills").GetInt32());
        Assert.Equal(1, _server.ClientCount);
    }

    [Fact]
    public async Task Ws_HeartbeatsFlowWhileQuiet()
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(WsUri(), Deadline());
        var msg = await ReceiveAsync(ws, Deadline()); // nothing published: first message is a beat
        Assert.Equal("heartbeat", msg.GetProperty("kind").GetString());
    }

    // ---------------- per-device subscriptions ----------------

    private static Task SubscribeAsync(ClientWebSocket ws, CancellationToken ct, params string[] surfaces) =>
        ws.SendAsync(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { kind = "subscribe", surfaces })),
            WebSocketMessageType.Text, endOfMessage: true, ct);

    [Fact]
    public async Task TwoClients_DifferentSubscriptions_EachGetsOnlyItsSections()
    {
        _server.Publish(Snap(version: 1));
        var ct = Deadline(15);

        using var spawnsOnly = new ClientWebSocket();
        using var sessionOnly = new ClientWebSocket();
        await spawnsOnly.ConnectAsync(WsUri(), ct);
        await sessionOnly.ConnectAsync(WsUri(), ct);
        await SubscribeAsync(spawnsOnly, ct, CompanionSurfaces.Spawns);
        await SubscribeAsync(sessionOnly, ct, CompanionSurfaces.Session);

        // Drain until each has acknowledged its subscription (the filtered re-send).
        var a = await NextSnapshotAsync(spawnsOnly, ct, m => !m.TryGetProperty("session", out _));
        var b = await NextSnapshotAsync(sessionOnly, ct, m => !m.TryGetProperty("spawns", out _));
        Assert.True(a.TryGetProperty("spawns", out _));
        Assert.True(b.TryGetProperty("session", out _));

        // A fresh publish keeps respecting each device's picks.
        _server.Publish(Snap(version: 2));
        var a2 = await NextSnapshotAsync(spawnsOnly, ct, m => m.GetProperty("version").GetInt64() == 2);
        var b2 = await NextSnapshotAsync(sessionOnly, ct, m => m.GetProperty("version").GetInt64() == 2);
        Assert.True(a2.TryGetProperty("spawns", out _));
        Assert.False(a2.TryGetProperty("session", out _));
        Assert.True(b2.TryGetProperty("session", out _));
        Assert.False(b2.TryGetProperty("spawns", out _));
    }

    [Fact]
    public async Task SubscriptionChange_MidConnection_ReprojectsWithoutReconnect()
    {
        _server.Publish(Snap(version: 1));
        var ct = Deadline(15);

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(WsUri(), ct);
        var full = await NextSnapshotAsync(ws, ct);
        Assert.True(full.TryGetProperty("spawns", out _)); // default = everything offered
        Assert.True(full.TryGetProperty("session", out _));

        await SubscribeAsync(ws, ct, CompanionSurfaces.Session);
        var narrowed = await NextSnapshotAsync(ws, ct, m => !m.TryGetProperty("spawns", out _));
        Assert.True(narrowed.TryGetProperty("session", out _));
        Assert.Equal(1, narrowed.GetProperty("version").GetInt64()); // same data, re-projected
    }

    [Fact]
    public async Task DesktopGate_WithheldSurface_IsAnExplicitNoOnTheWire()
    {
        _server.Publish(Snap(version: 1, offerSession: false)); // the owner keeps session home
        var ct = Deadline(15);

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(WsUri(), ct);
        await SubscribeAsync(ws, ct, CompanionSurfaces.Spawns, CompanionSurfaces.Session);

        var msg = await NextSnapshotAsync(ws, ct, m => m.TryGetProperty("notOffered", out _));
        Assert.False(msg.TryGetProperty("session", out _));
        Assert.True(msg.TryGetProperty("spawns", out _));
        Assert.Equal("session", msg.GetProperty("notOffered")[0].GetString());
    }
}
