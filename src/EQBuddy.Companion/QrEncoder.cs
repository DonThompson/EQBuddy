namespace EQBuddy.Companion;

/// <summary>
/// A small, dependency-free QR encoder — just enough QR for the pairing URL
/// (byte mode, error-correction level M, versions 1–6, auto mask selection).
///
/// Written for this project against ISO/IEC 18004; the module-layout conventions
/// (format-info bit placement, zigzag data walk, mask formulas) were adapted from
/// Kazuhiko Arase's qrcode-generator (https://github.com/kazuhikoarase/qrcode-generator,
/// MIT license) — see NOTICE. Verified by QrEncoderTests: Reed-Solomon syndromes,
/// BCH-checked format info, structural invariants, and a zxing decode during the spike.
///
/// Output is the bare module matrix, true = dark, no quiet zone — renderers add the
/// 4-module quiet border themselves (QrBitmap does for WPF).
/// </summary>
public static class QrEncoder
{
    // ---- capacity tables, EC level M, versions 1..6 (ISO 18004 table 9) ----
    // Version 6 caps the URL at 106 data bytes; the pairing URL is ~55. Versions ≥ 7
    // would need the version-info block, deliberately out of scope.
    private static readonly int[] DataCodewords = [16, 28, 44, 64, 86, 108];
    private static readonly int[] BlockCount = [1, 1, 1, 2, 2, 4];
    private static readonly int[] EcPerBlock = [10, 16, 26, 18, 24, 16];

    /// <summary>Encode UTF-8 text as a QR module matrix (EC level M).</summary>
    /// <exception cref="ArgumentException">Text too long for version 6-M (106 bytes).</exception>
    public static bool[,] Encode(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var version = SelectVersion(bytes.Length);
        var codewords = BuildCodewords(bytes, version);
        return BuildMatrix(version, codewords);
    }

    /// <summary>Smallest version 1..6 whose byte-mode capacity fits; capacity is
    /// dataCodewords − 2 (4-bit mode + 8-bit count headers round up to 2 bytes).</summary>
    internal static int SelectVersion(int byteCount)
    {
        for (var v = 1; v <= 6; v++)
            if (byteCount <= DataCodewords[v - 1] - 2) return v;
        throw new ArgumentException(
            $"Text is {byteCount} bytes; the pairing QR supports at most {DataCodewords[5] - 2}.");
    }

    // ================= bitstream + Reed-Solomon =================

    private static byte[] BuildCodewords(byte[] data, int version)
    {
        var dataCw = DataCodewords[version - 1];
        var bits = new List<bool>(dataCw * 8);
        AppendBits(bits, 0b0100, 4);               // byte mode
        AppendBits(bits, data.Length, 8);          // count (8 bits for versions 1–9)
        foreach (var b in data) AppendBits(bits, b, 8);
        // Terminator (≤ 4 zero bits), byte-align, then alternating pad bytes.
        for (var i = 0; i < 4 && bits.Count < dataCw * 8; i++) bits.Add(false);
        while (bits.Count % 8 != 0) bits.Add(false);
        for (var pad = true; bits.Count < dataCw * 8; pad = !pad)
            AppendBits(bits, pad ? 0xEC : 0x11, 8);

        var cw = new byte[dataCw];
        for (var i = 0; i < bits.Count; i++)
            if (bits[i]) cw[i / 8] |= (byte)(0x80 >> (i % 8));

        return InterleaveWithEc(cw, version);
    }

    private static void AppendBits(List<bool> bits, int value, int count)
    {
        for (var i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
    }

    /// <summary>Split data into blocks, add EC codewords, interleave per the spec.
    /// All M-level blocks in versions 1–6 are equal-sized; the round-robin below is
    /// still written to tolerate a short last block for future versions.</summary>
    private static byte[] InterleaveWithEc(byte[] data, int version)
    {
        var blocks = BlockCount[version - 1];
        var ecLen = EcPerBlock[version - 1];
        var per = data.Length / blocks;

        var dataBlocks = new byte[blocks][];
        var ecBlocks = new byte[blocks][];
        for (var b = 0; b < blocks; b++)
        {
            dataBlocks[b] = data[(b * per)..((b + 1) * per)];
            ecBlocks[b] = ComputeEc(dataBlocks[b], ecLen);
        }

        var result = new List<byte>(data.Length + blocks * ecLen);
        for (var i = 0; i < per; i++)
            for (var b = 0; b < blocks; b++)
                if (i < dataBlocks[b].Length) result.Add(dataBlocks[b][i]);
        for (var i = 0; i < ecLen; i++)
            for (var b = 0; b < blocks; b++)
                result.Add(ecBlocks[b][i]);
        return [.. result];
    }

    // GF(256) with the QR polynomial x^8+x^4+x^3+x^2+1 (0x11D), generator α = 2.
    private static readonly byte[] GfExp = new byte[512];
    private static readonly byte[] GfLog = new byte[256];

    static QrEncoder()
    {
        var x = 1;
        for (var i = 0; i < 255; i++)
        {
            GfExp[i] = (byte)x;
            GfLog[x] = (byte)i;
            x <<= 1;
            if (x >= 256) x ^= 0x11D;
        }
        for (var i = 255; i < 512; i++) GfExp[i] = GfExp[i - 255];
    }

    private static byte GfMul(byte a, byte b) =>
        a == 0 || b == 0 ? (byte)0 : GfExp[GfLog[a] + GfLog[b]];

    /// <summary>Reed-Solomon EC codewords: remainder of data(x)·x^ecLen mod g(x),
    /// g(x) = ∏(x − α^i) for i = 0..ecLen−1. Internal so tests can syndrome-check it.</summary>
    internal static byte[] ComputeEc(byte[] data, int ecLen)
    {
        // Build the generator polynomial, highest degree first, monic.
        var gen = new byte[] { 1 };
        for (var i = 0; i < ecLen; i++)
        {
            var next = new byte[gen.Length + 1];
            for (var j = 0; j < gen.Length; j++)
            {
                next[j] ^= GfMul(gen[j], 1);              // x · gen
                next[j + 1] ^= GfMul(gen[j], GfExp[i]);   // α^i · gen
            }
            gen = next;
        }

        var rem = new byte[ecLen];
        foreach (var d in data)
        {
            var factor = (byte)(d ^ rem[0]);
            Array.Copy(rem, 1, rem, 0, ecLen - 1);
            rem[ecLen - 1] = 0;
            if (factor == 0) continue;
            for (var j = 0; j < ecLen; j++)
                rem[j] ^= GfMul(gen[j + 1], factor);
        }
        return rem;
    }

    // ================= matrix =================

    private static bool[,] BuildMatrix(int version, byte[] codewords)
    {
        var size = 17 + 4 * version;
        var modules = new bool[size, size];
        var isFunction = new bool[size, size];

        DrawFunctionPatterns(version, size, modules, isFunction);

        // Try all 8 masks on real copies; lowest penalty wins (ISO 18004 §8.8.2).
        bool[,]? best = null;
        var bestPenalty = int.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            var m = (bool[,])modules.Clone();
            PlaceData(m, isFunction, codewords, mask, size);
            DrawFormatInfo(m, size, mask);
            var p = Penalty(m, size);
            if (p < bestPenalty) { bestPenalty = p; best = m; }
        }
        return best!;
    }

    private static void DrawFunctionPatterns(int version, int size, bool[,] mod, bool[,] fun)
    {
        // Finder patterns + separators; drawing an 8×8 reserved region per corner
        // covers the one-module separator, with the finder's 7×7 rings inside.
        foreach (var (top, left) in new[] { (0, 0), (0, size - 7), (size - 7, 0) })
        {
            for (var r = top - 1; r <= top + 7; r++)
                for (var c = left - 1; c <= left + 7; c++)
                {
                    if (r < 0 || r >= size || c < 0 || c >= size) continue;
                    var dr = r - top; var dc = c - left;
                    var inFinder = dr is >= 0 and <= 6 && dc is >= 0 and <= 6;
                    var dark = inFinder &&
                        (dr == 0 || dr == 6 || dc == 0 || dc == 6 ||
                         (dr is >= 2 and <= 4 && dc is >= 2 and <= 4));
                    mod[r, c] = dark;
                    fun[r, c] = true;
                }
        }

        // Timing patterns: row 6 and column 6, dark on even coordinates.
        for (var i = 8; i < size - 8; i++)
        {
            mod[6, i] = i % 2 == 0; fun[6, i] = true;
            mod[i, 6] = i % 2 == 0; fun[i, 6] = true;
        }

        // Alignment pattern (versions 2–6 have exactly one, centered at (size-7, size-7)).
        if (version >= 2)
        {
            var ctr = size - 7;
            for (var r = ctr - 2; r <= ctr + 2; r++)
                for (var c = ctr - 2; c <= ctr + 2; c++)
                {
                    var d = Math.Max(Math.Abs(r - ctr), Math.Abs(c - ctr));
                    mod[r, c] = d != 1;
                    fun[r, c] = true;
                }
        }

        // Reserve the format-info strips (filled per-mask later) and the dark module.
        for (var i = 0; i <= 8; i++)
        {
            if (i != 6) { fun[8, i] = true; fun[i, 8] = true; }
            if (i < 8) { fun[8, size - 1 - i] = true; fun[size - 1 - i, 8] = true; }
        }
        mod[size - 8, 8] = true; fun[size - 8, 8] = true;
    }

    private static void PlaceData(bool[,] mod, bool[,] fun, byte[] codewords, int mask, int size)
    {
        // The zigzag walk: column pairs right-to-left, alternating up/down, skipping
        // the vertical timing column. Leftover modules (the spec's "remainder bits")
        // read as zero bits, which is exactly what the loop produces past the data.
        var bitIndex = 0;
        var totalBits = codewords.Length * 8;
        var upward = true;
        for (var right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5; // hop the timing column
            for (var i = 0; i < size; i++)
            {
                var row = upward ? size - 1 - i : i;
                for (var c = 0; c < 2; c++)
                {
                    var col = right - c;
                    if (fun[row, col]) continue;
                    var dark = bitIndex < totalBits &&
                        ((codewords[bitIndex / 8] >> (7 - bitIndex % 8)) & 1) != 0;
                    bitIndex++;
                    if (MaskBit(mask, row, col)) dark = !dark;
                    mod[row, col] = dark;
                }
            }
            upward = !upward;
        }
    }

    private static bool MaskBit(int mask, int i, int j) => mask switch
    {
        0 => (i + j) % 2 == 0,
        1 => i % 2 == 0,
        2 => j % 3 == 0,
        3 => (i + j) % 3 == 0,
        4 => (i / 2 + j / 3) % 2 == 0,
        5 => i * j % 2 + i * j % 3 == 0,
        6 => (i * j % 2 + i * j % 3) % 2 == 0,
        7 => (i * j % 3 + (i + j) % 2) % 2 == 0,
        _ => throw new ArgumentOutOfRangeException(nameof(mask)),
    };

    /// <summary>The 15 format bits (EC level + mask, BCH(15,5)-protected, XOR-masked).
    /// Internal so tests can verify the BCH remainder independently.</summary>
    internal static int FormatBits(int mask)
    {
        const int EcLevelM = 0b00; // L=01, M=00, Q=11, H=10
        var data = (EcLevelM << 3) | mask;
        var rem = data << 10;
        for (var i = 14; i >= 10; i--)
            if (((rem >> i) & 1) != 0) rem ^= 0x537 << (i - 10);
        return ((data << 10) | rem) ^ 0x5412;
    }

    private static void DrawFormatInfo(bool[,] mod, int size, int mask)
    {
        var bits = FormatBits(mask);
        for (var i = 0; i < 15; i++)
        {
            var dark = ((bits >> i) & 1) != 0;
            // First copy, around the top-left finder (skipping the timing row/col).
            if (i < 6) mod[i, 8] = dark;
            else if (i < 8) mod[i + 1, 8] = dark;
            else if (i == 8) mod[8, 7] = dark;
            else mod[8, 14 - i] = dark;
            // Second copy, split under the top-right / beside the bottom-left finders.
            if (i < 8) mod[8, size - 1 - i] = dark;
            else mod[size - 15 + i, 8] = dark;
        }
    }

    // ---- mask penalty, ISO 18004 §8.8.2 rules N1..N4 ----

    private static int Penalty(bool[,] m, int size)
    {
        var penalty = 0;

        // N1: runs of ≥5 same-colored modules, rows and columns.
        for (var axis = 0; axis < 2; axis++)
            for (var i = 0; i < size; i++)
            {
                var run = 1;
                for (var j = 1; j < size; j++)
                {
                    var cur = axis == 0 ? m[i, j] : m[j, i];
                    var prev = axis == 0 ? m[i, j - 1] : m[j - 1, i];
                    if (cur == prev) { if (++run == 5) penalty += 3; else if (run > 5) penalty++; }
                    else run = 1;
                }
            }

        // N2: 2×2 blocks of one color.
        for (var r = 0; r < size - 1; r++)
            for (var c = 0; c < size - 1; c++)
                if (m[r, c] == m[r, c + 1] && m[r, c] == m[r + 1, c] && m[r, c] == m[r + 1, c + 1])
                    penalty += 3;

        // N3: finder-like 1:1:3:1:1 pattern with 4 light modules on either side.
        Span<bool> line = stackalloc bool[size];
        for (var axis = 0; axis < 2; axis++)
            for (var i = 0; i < size; i++)
            {
                for (var j = 0; j < size; j++) line[j] = axis == 0 ? m[i, j] : m[j, i];
                for (var j = 0; j + 7 <= size; j++)
                {
                    if (!(line[j] && !line[j + 1] && line[j + 2] && line[j + 3] && line[j + 4]
                          && !line[j + 5] && line[j + 6])) continue;
                    var lightBefore = j >= 4 && !line[j - 4] && !line[j - 3] && !line[j - 2] && !line[j - 1];
                    var lightAfter = j + 11 <= size && !line[j + 7] && !line[j + 8] && !line[j + 9] && !line[j + 10];
                    if (lightBefore || lightAfter) penalty += 40;
                }
            }

        // N4: dark-module balance, 10 points per 5% step away from 50%.
        var dark = 0;
        foreach (var b in m) if (b) dark++;
        var percent = dark * 100 / (size * size);
        penalty += Math.Abs(percent - 50) / 5 * 10;

        return penalty;
    }
}
