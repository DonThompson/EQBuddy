using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

// David's machine, 2026-08-15: "the Mobile link doesn't work. Scanning the QR code
// didn't bring up the mobile interface". EQBuddy was enabled, listening, and bound to
// three addresses — 10.0.0.84 (the real LAN), 100.118.30.124 (Tailscale) and
// 192.168.200.1 (a virtual host adapter). The old rule ordered by RFC1918 privateness
// alone, which scores the virtual adapter identically to the real LAN, so NIC
// enumeration order picked the QR's address. A tablet on the house Wi-Fi can reach
// exactly one of those three.
public class LanAddressRankTests
{
    // The real machine, as reported by Get-NetTCPConnection.
    private const string Lan = "10.0.0.84";
    private const string Tailscale = "100.118.30.124";
    private const string VirtualHost = "192.168.200.1";

    private static List<string> Ordered(params (string Ip, bool Gw, string Desc)[] candidates) =>
        [.. candidates
            .OrderBy(c => LanAddressRank.Score(c.Ip, c.Gw, c.Desc))
            .Select(c => c.Ip)];

    [Fact]
    public void TheRealLanWinsOverAVirtualAdapterAndAMeshVpn()
    {
        // Deliberately listed with the real LAN LAST, which is the ordering that broke
        // it: if the rule works only when Windows happens to enumerate favourably, it
        // does not work.
        var order = Ordered(
            (VirtualHost, false, "Hyper-V Virtual Ethernet Adapter"),
            (Tailscale, false, "Tailscale Tunnel"),
            (Lan, true, "Intel(R) Ethernet Controller I225-V"));
        Assert.Equal(Lan, order[0]);
    }

    [Fact]
    public void AGatewayBeatsNoGatewayEvenWhenBothLookPrivate()
    {
        // The decisive signal, isolated: same shape of address, only the route differs.
        Assert.True(LanAddressRank.Score("10.0.0.84", hasGateway: true, "Ethernet")
            < LanAddressRank.Score("192.168.200.1", hasGateway: false, "Ethernet"));
    }

    [Fact]
    public void AVirtualAdapterLosesEvenIfItSomehowReportsAGateway()
    {
        // Docker and WSL adapters have been seen advertising one.
        Assert.True(LanAddressRank.Score(Lan, true, "Realtek Gaming GbE")
            < LanAddressRank.Score("172.17.0.1", true, "Docker Desktop vEthernet"));
    }

    [Fact]
    public void TailscaleIsRecognisedByItsAddressRangeNotOnlyItsName()
    {
        // A mesh VPN under an unfamiliar adapter name still must not win the QR.
        Assert.True(LanAddressRank.IsCarrierGradeNat(Tailscale));
        Assert.True(LanAddressRank.Score(Lan, true, "Ethernet")
            < LanAddressRank.Score(Tailscale, true, "Unknown Adapter"));
    }

    [Fact]
    public void APublicAddressLosesToAPrivateOne()
    {
        Assert.False(LanAddressRank.IsPrivate("203.0.113.9"));
        Assert.True(LanAddressRank.Score(Lan, true, "Ethernet")
            < LanAddressRank.Score("203.0.113.9", true, "Ethernet"));
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("10.255.255.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    public void Rfc1918RangesAreRecognised(string ip) => Assert.True(LanAddressRank.IsPrivate(ip));

    [Theory]
    [InlineData("172.15.0.1")]   // just below the 172.16–31 block
    [InlineData("172.32.0.1")]   // just above it
    [InlineData("100.63.0.1")]   // just below CGNAT
    [InlineData("not.an.ip.at.all")]
    public void NearMissesAreNotTreatedAsPrivate(string ip) => Assert.False(LanAddressRank.IsPrivate(ip));

    [Fact]
    public void GarbageNeverThrows()
    {
        // LanAddresses() runs during startup; a malformed address must not take the
        // pairing window down with it.
        LanAddressRank.Score("", false, "");
        LanAddressRank.Score("1.2.3", true, null!);
        Assert.False(LanAddressRank.IsCarrierGradeNat("...."));
    }
}
