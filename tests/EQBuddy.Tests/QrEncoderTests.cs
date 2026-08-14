using EQBuddy.Companion;

namespace EQBuddy.Tests;

/// <summary>
/// The hand-rolled QR encoder, checked three independent ways: Reed-Solomon output
/// verified mathematically (syndromes of the full codeword must vanish — computed
/// here with a test-local GF(256), not the encoder's), format info re-read from the
/// matrix and BCH-checked, and the structural invariants every scanner leans on
/// (finders, timing, dark module, size). During the spike the output was also
/// decoded end-to-end by zxing; these tests keep the encoder honest from then on.
/// </summary>
public class QrEncoderTests
{
    private const string PairingUrl = "http://192.168.1.23:47859/#0123456789abcdef0123456789abcdef";

    // ---------------- version selection ----------------

    [Theory]
    [InlineData(1, 1)]     // trivial
    [InlineData(14, 1)]    // v1-M byte capacity boundary
    [InlineData(15, 2)]
    [InlineData(26, 2)]
    [InlineData(27, 3)]
    [InlineData(42, 3)]
    [InlineData(43, 4)]
    [InlineData(62, 4)]    // the pairing URL's neighborhood
    [InlineData(63, 5)]
    [InlineData(84, 5)]
    [InlineData(85, 6)]
    [InlineData(106, 6)]   // v6-M ceiling
    public void VersionSelection_MatchesByteModeCapacities(int bytes, int expectedVersion) =>
        Assert.Equal(expectedVersion, QrEncoder.SelectVersion(bytes));

    [Fact]
    public void TooLong_ThrowsInsteadOfLying() =>
        Assert.Throws<ArgumentException>(() => QrEncoder.SelectVersion(107));

    [Fact]
    public void MatrixSize_Is17Plus4V()
    {
        Assert.Equal(21, QrEncoder.Encode("x").GetLength(0));                // v1
        Assert.Equal(33, QrEncoder.Encode(PairingUrl).GetLength(0));         // 55 bytes → v4
    }

    // ---------------- Reed-Solomon, independently verified ----------------

    /// <summary>A valid RS codeword c(x) = data·x^n + ec evaluates to zero at every
    /// generator root α^0..α^(n−1). Computed with this test's own GF tables.</summary>
    [Theory]
    [InlineData(16, 10)]   // v1-M block shape
    [InlineData(64, 18)]   // v4-M block shape (two blocks of 32 use 18 each)
    public void EcCodewords_HaveVanishingSyndromes(int dataLen, int ecLen)
    {
        var rng = new Random(42);
        for (var round = 0; round < 25; round++)
        {
            var data = new byte[dataLen];
            rng.NextBytes(data);
            var ec = QrEncoder.ComputeEc(data, ecLen);
            Assert.Equal(ecLen, ec.Length);

            var codeword = data.Concat(ec).ToArray();
            for (var i = 0; i < ecLen; i++)
                Assert.Equal(0, PolyEval(codeword, Exp(i)));
        }
    }

    // Test-local GF(256) (poly 0x11D) — deliberately not the encoder's tables.
    private static byte Exp(int power)
    {
        var x = 1;
        for (var i = 0; i < power; i++) { x <<= 1; if (x >= 256) x ^= 0x11D; }
        return (byte)x;
    }

    private static byte Mul(byte a, byte b)
    {
        var result = 0;
        var aa = (int)a;
        for (var bit = 0; bit < 8; bit++)
        {
            if (((b >> (7 - bit)) & 1) != 0) result ^= aa << (7 - bit);
        }
        // Reduce the 15-bit product modulo x^8+x^4+x^3+x^2+1.
        for (var i = 14; i >= 8; i--)
            if (((result >> i) & 1) != 0) result ^= 0x11D << (i - 8);
        return (byte)result;
    }

    private static byte PolyEval(byte[] coefficients, byte x)
    {
        byte y = 0;
        foreach (var c in coefficients) y = (byte)(Mul(y, x) ^ c);
        return y;
    }

    // ---------------- format info ----------------

    [Fact]
    public void FormatBits_SurviveTheBchCheck_AndEncodeLevelM()
    {
        for (var mask = 0; mask < 8; mask++)
        {
            var bits = QrEncoder.FormatBits(mask) ^ 0x5412; // strip the fixed XOR mask
            // BCH(15,5) self-check: the whole 15-bit word must divide by 0x537.
            var rem = bits;
            for (var i = 14; i >= 10; i--)
                if (((rem >> i) & 1) != 0) rem ^= 0x537 << (i - 10);
            Assert.Equal(0, rem);
            // Top 5 bits: EC level M (00) then the mask id.
            Assert.Equal(mask, bits >> 10);
        }
    }

    [Fact]
    public void Matrix_CarriesConsistentFormatInfo_InBothCopies()
    {
        var m = QrEncoder.Encode(PairingUrl);
        var size = m.GetLength(0);

        var copy1 = 0;
        var copy2 = 0;
        for (var i = 0; i < 15; i++)
        {
            var b1 = i < 6 ? m[i, 8] : i < 8 ? m[i + 1, 8] : i == 8 ? m[8, 7] : m[8, 14 - i];
            var b2 = i < 8 ? m[8, size - 1 - i] : m[size - 15 + i, 8];
            if (b1) copy1 |= 1 << i;
            if (b2) copy2 |= 1 << i;
        }
        Assert.Equal(copy1, copy2);
        // Whatever mask won the penalty contest, the strip must be ITS format word.
        var legal = Enumerable.Range(0, 8).Select(QrEncoder.FormatBits).ToList();
        Assert.Contains(copy1, legal);
    }

    // ---------------- structural invariants ----------------

    [Fact]
    public void FindersTimingAndDarkModule_AreInPlace()
    {
        var m = QrEncoder.Encode(PairingUrl);
        var size = m.GetLength(0);

        foreach (var (top, left) in new[] { (0, 0), (0, size - 7), (size - 7, 0) })
        {
            Assert.True(m[top, left] && m[top + 6, left + 6]);       // outer ring corners
            Assert.True(m[top + 3, left + 3]);                       // center
            Assert.False(m[top + 1, left + 1] || m[top + 5, left + 5]); // white ring
        }
        for (var i = 8; i < size - 8; i++)
        {
            Assert.Equal(i % 2 == 0, m[6, i]);   // masking must never touch the timing tracks
            Assert.Equal(i % 2 == 0, m[i, 6]);
        }
        Assert.True(m[size - 8, 8]);             // the always-dark module
    }

    [Fact]
    public void Encode_IsDeterministic()
    {
        var a = QrEncoder.Encode(PairingUrl);
        var b = QrEncoder.Encode(PairingUrl);
        Assert.Equal(a.Cast<bool>(), b.Cast<bool>());
    }
}
