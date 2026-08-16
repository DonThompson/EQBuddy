namespace EQBuddy.Core;

/// <summary>
/// Which of this machine's IPv4 addresses to print on the pairing QR (David,
/// 2026-08-15: "Scanning the QR code didn't bring up the mobile interface").
///
/// <para>The original rule was "private addresses first", which cannot separate the
/// address a tablet can reach from one it cannot: a Hyper-V / VirtualBox / WSL host
/// adapter at 192.168.200.1 is exactly as RFC1918-private as the real LAN at 10.0.0.84,
/// and Windows often enumerates the virtual one first. The QR then encodes an address
/// that exists only inside this PC, and the scan appears to do nothing at all.</para>
///
/// <para>The signal that actually distinguishes them is a DEFAULT GATEWAY: the
/// interface that routes to the rest of the network is the interface the tablet shares.
/// Virtual host-only adapters have no gateway; nor do most VPN tunnels worth skipping
/// here. Ranking is a pure function of (address, has-gateway, adapter description) so
/// it can be tested without a network — the machine that showed the bug has three
/// candidates and reproducing it any other way means owning the same hardware.</para>
/// </summary>
public static class LanAddressRank
{
    /// <summary>Adapter-description fragments that mean "this is not the network your
    /// tablet is on". Matched case-insensitively against the NIC description. Tailscale
    /// and ZeroTier are reachable in their own way, but a QR scanned from a tablet on
    /// the house Wi-Fi will not reach them, so they lose to the real LAN.</summary>
    private static readonly string[] VirtualAdapterHints =
    [
        "hyper-v", "virtualbox", "vmware", "docker", "wsl", "vethernet",
        "tailscale", "zerotier", "loopback", "tap-", "tun", "openvpn",
        "wireguard", "bluetooth", "npcap", "pseudo",
    ];

    /// <summary>Lower sorts earlier. The components are additive so a real LAN address
    /// on a gatewayed physical adapter always beats every combination of penalties.</summary>
    public static int Score(string address, bool hasGateway, string adapterDescription)
    {
        var score = 0;
        // The decisive one: no route off this machine, no tablet.
        if (!hasGateway) score += 100;
        var desc = adapterDescription ?? "";
        foreach (var hint in VirtualAdapterHints)
            if (desc.Contains(hint, StringComparison.OrdinalIgnoreCase)) { score += 50; break; }
        // 100.64/10 is carrier-grade NAT, which in practice here means a mesh VPN.
        if (IsCarrierGradeNat(address)) score += 25;
        if (!IsPrivate(address)) score += 10;
        return score;
    }

    /// <summary>RFC1918. Kept as the tiebreak it always was, no longer as the only rule.</summary>
    public static bool IsPrivate(string address)
    {
        var b = Octets(address);
        if (b is null) return false;
        return b[0] == 192 && b[1] == 168
            || b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31);
    }

    /// <summary>100.64.0.0/10 — Tailscale and friends live here.</summary>
    public static bool IsCarrierGradeNat(string address)
    {
        var b = Octets(address);
        return b is not null && b[0] == 100 && b[1] >= 64 && b[1] <= 127;
    }

    private static int[]? Octets(string address)
    {
        var parts = (address ?? "").Split('.');
        if (parts.Length != 4) return null;
        var octets = new int[4];
        for (var i = 0; i < 4; i++)
            if (!int.TryParse(parts[i], out octets[i])) return null;
        return octets;
    }
}
