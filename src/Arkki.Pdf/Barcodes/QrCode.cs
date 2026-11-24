using System.Text;

namespace Arkki.Pdf.Barcodes;

/// <summary>How much of a QR code may be damaged and still read; higher levels make a larger symbol.</summary>
public enum QrErrorCorrection
{
    /// <summary>About 7 % may be damaged.</summary>
    L,
    /// <summary>About 15 % may be damaged.</summary>
    M,
    /// <summary>About 25 % may be damaged.</summary>
    Q,
    /// <summary>About 30 % may be damaged.</summary>
    H,
}

/// <summary>
/// QR code encoder (ISO/IEC 18004), versions 1 to 40. The text goes in one segment: numeric or
/// alphanumeric mode when every character allows it, otherwise UTF-8 bytes. The smallest version
/// that holds the text at the requested error correction level is used, with the mask that the
/// standard's penalty rules score best.
/// </summary>
public sealed class QrCode(QrErrorCorrection level = QrErrorCorrection.M) : IBarcodeSymbology
{
    private const string Alphanumeric = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    // Error correction codewords per block and number of blocks, by level (L, M, Q, H) and version.
    private static readonly byte[][] EcPerBlock =
    [
        [0, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        [0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28],
        [0, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        [0, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
    ];

    private static readonly byte[][] Blocks =
    [
        [0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25],
        [0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49],
        [0, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68],
        [0, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81],
    ];

    /// <summary>The error correction level: how much of the symbol may be damaged and still read.</summary>
    public QrErrorCorrection Level { get; } = level;

    /// <inheritdoc />
    public BarcodeMatrix Encode(string text)
    {
        var (mode, data, count) = Segment(text);
        int lvl = (int)Level;
        int version = 1;
        for (; ; version++)
        {
            if (version > 40) throw new ArgumentException("Text is too long for a QR code at this error correction level.", nameof(text));
            int capacity = DataCodewords(version, lvl) * 8;
            if (4 + CountBits(mode, version) + data.Count <= capacity) break;
        }

        var bits = new BitList();
        bits.Add(mode switch { 0 => 1, 1 => 2, _ => 4 }, 4);
        bits.Add(count, CountBits(mode, version));
        bits.AddAll(data);
        int capacityBits = DataCodewords(version, lvl) * 8;
        bits.Add(0, Math.Min(4, capacityBits - bits.Count));
        bits.Add(0, (8 - bits.Count % 8) % 8);
        for (int pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11) bits.Add(pad, 8);

        byte[] codewords = AddErrorCorrection(bits.ToBytes(), version, lvl);
        return new Symbol(version).Build(codewords, lvl);
    }

    // Mode 0 numeric, 1 alphanumeric, 2 byte. Returns the encoded data bits and the character count.
    private static (int Mode, BitList Data, int Count) Segment(string text)
    {
        var bits = new BitList();
        if (text.Length > 0 && text.All(char.IsAsciiDigit))
        {
            for (int i = 0; i < text.Length; i += 3)
            {
                int n = Math.Min(3, text.Length - i);
                bits.Add(int.Parse(text.AsSpan(i, n)), n * 3 + 1);
            }
            return (0, bits, text.Length);
        }
        if (text.All(c => Alphanumeric.Contains(c)))
        {
            for (int i = 0; i + 1 < text.Length; i += 2)
                bits.Add(Alphanumeric.IndexOf(text[i]) * 45 + Alphanumeric.IndexOf(text[i + 1]), 11);
            if (text.Length % 2 == 1) bits.Add(Alphanumeric.IndexOf(text[^1]), 6);
            return (1, bits, text.Length);
        }
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        foreach (byte b in utf8) bits.Add(b, 8);
        return (2, bits, utf8.Length);
    }

    private static int CountBits(int mode, int version)
    {
        int band = version <= 9 ? 0 : version <= 26 ? 1 : 2;
        return mode switch
        {
            0 => new[] { 10, 12, 14 }[band],
            1 => new[] { 9, 11, 13 }[band],
            _ => new[] { 8, 16, 16 }[band],
        };
    }

    private static int RawCodewords(int version)
    {
        int modules = (16 * version + 128) * version + 64;
        if (version >= 2)
        {
            int align = version / 7 + 2;
            modules -= (25 * align - 10) * align - 55;
            if (version >= 7) modules -= 36;
        }
        return modules / 8;
    }

    private static int DataCodewords(int version, int lvl) =>
        RawCodewords(version) - EcPerBlock[lvl][version] * Blocks[lvl][version];

    /// <summary>Splits data into blocks, appends Reed-Solomon codewords to each and interleaves them.</summary>
    private static byte[] AddErrorCorrection(byte[] data, int version, int lvl)
    {
        int blockCount = Blocks[lvl][version], ecLength = EcPerBlock[lvl][version];
        int raw = RawCodewords(version);
        int shortBlocks = blockCount - raw % blockCount, shortLength = raw / blockCount;
        byte[] generator = ReedSolomon.Generator(ecLength);

        // Every block is stored at the long length; a short block has a gap at its last data
        // position, which interleaving skips.
        var blocks = new byte[blockCount][];
        for (int i = 0, k = 0; i < blockCount; i++)
        {
            int dataLength = shortLength - ecLength + (i < shortBlocks ? 0 : 1);
            byte[] block = data.AsSpan(k, dataLength).ToArray();
            k += dataLength;
            blocks[i] = new byte[shortLength + 1];
            block.CopyTo(blocks[i], 0);
            ReedSolomon.Remainder(block, generator).CopyTo(blocks[i], shortLength + 1 - ecLength);
        }

        var result = new byte[raw];
        int n = 0;
        for (int i = 0; i <= shortLength; i++)
            for (int j = 0; j < blockCount; j++)
                if (i != shortLength - ecLength || j >= shortBlocks) result[n++] = blocks[j][i];
        return result;
    }

    /// <summary>Module grid of one symbol while it is being built.</summary>
    private sealed class Symbol
    {
        private readonly int _version, _size;
        private readonly bool[,] _dark, _function;

        public Symbol(int version)
        {
            _version = version;
            _size = version * 4 + 17;
            _dark = new bool[_size, _size];
            _function = new bool[_size, _size];
        }

        public BarcodeMatrix Build(byte[] codewords, int lvl)
        {
            DrawFunctionPatterns();
            PlaceCodewords(codewords);

            int bestMask = 0;
            long bestPenalty = long.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask);
                DrawFormat(lvl, mask);
                long penalty = Penalty();
                if (penalty < bestPenalty) { bestPenalty = penalty; bestMask = mask; }
                ApplyMask(mask);
            }
            ApplyMask(bestMask);
            DrawFormat(lvl, bestMask);

            var m = new BarcodeMatrix(_size, _size, quietZone: 4);
            for (int y = 0; y < _size; y++)
                for (int x = 0; x < _size; x++) m[x, y] = _dark[y, x];
            return m;
        }

        private void Set(int x, int y, bool dark)
        {
            _dark[y, x] = dark;
            _function[y, x] = true;
        }

        private void DrawFunctionPatterns()
        {
            for (int i = 0; i < _size; i++)
            {
                Set(6, i, i % 2 == 0);
                Set(i, 6, i % 2 == 0);
            }
            DrawFinder(3, 3);
            DrawFinder(_size - 4, 3);
            DrawFinder(3, _size - 4);

            int[] align = AlignmentPositions();
            int last = align.Length - 1;
            for (int i = 0; i < align.Length; i++)
                for (int j = 0; j < align.Length; j++)
                    if (!(i == 0 && j == 0 || i == 0 && j == last || i == last && j == 0))
                        DrawAlignment(align[i], align[j]);

            DrawFormat(0, 0); // reserves the format areas; overwritten once the mask is chosen
            if (_version >= 7)
            {
                int rem = _version;
                for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                int bits = _version << 12 | rem;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = (bits >> i & 1) != 0;
                    int a = _size - 11 + i % 3, b = i / 3;
                    Set(a, b, bit);
                    Set(b, a, bit);
                }
            }
        }

        private void DrawFinder(int cx, int cy)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= _size || y < 0 || y >= _size) continue;
                    int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(x, y, d != 2 && d != 4);
                }
        }

        private void DrawAlignment(int cx, int cy)
        {
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    Set(cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }

        private int[] AlignmentPositions()
        {
            if (_version == 1) return [];
            int count = _version / 7 + 2;
            int step = _version == 32 ? 26 : (_version * 4 + count * 2 + 1) / (count * 2 - 2) * 2;
            var result = new int[count];
            result[0] = 6;
            for (int i = count - 1, pos = _size - 7; i >= 1; i--, pos -= step) result[i] = pos;
            return result;
        }

        private void DrawFormat(int lvl, int mask)
        {
            int levelBits = lvl switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };
            int data = levelBits << 3 | mask, rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;
            bool Bit(int i) => (bits >> i & 1) != 0;

            for (int i = 0; i <= 5; i++) Set(8, i, Bit(i));
            Set(8, 7, Bit(6));
            Set(8, 8, Bit(7));
            Set(7, 8, Bit(8));
            for (int i = 9; i < 15; i++) Set(14 - i, 8, Bit(i));

            for (int i = 0; i < 8; i++) Set(_size - 1 - i, 8, Bit(i));
            for (int i = 8; i < 15; i++) Set(8, _size - 15 + i, Bit(i));
            Set(8, _size - 8, true);
        }

        private void PlaceCodewords(byte[] data)
        {
            int i = 0;
            for (int right = _size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                bool upward = ((right + 1) & 2) == 0;
                for (int v = 0; v < _size; v++)
                {
                    int y = upward ? _size - 1 - v : v;
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        if (_function[y, x] || i >= data.Length * 8) continue;
                        _dark[y, x] = (data[i >> 3] >> (7 - (i & 7)) & 1) != 0;
                        i++;
                    }
                }
            }
        }

        private void ApplyMask(int mask)
        {
            for (int y = 0; y < _size; y++)
                for (int x = 0; x < _size; x++)
                {
                    if (_function[y, x]) continue;
                    bool flip = mask switch
                    {
                        0 => (x + y) % 2 == 0,
                        1 => y % 2 == 0,
                        2 => x % 3 == 0,
                        3 => (x + y) % 3 == 0,
                        4 => (x / 3 + y / 2) % 2 == 0,
                        5 => x * y % 2 + x * y % 3 == 0,
                        6 => (x * y % 2 + x * y % 3) % 2 == 0,
                        _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                    };
                    if (flip) _dark[y, x] = !_dark[y, x];
                }
        }

        /// <summary>The standard's penalty score: long runs, 2×2 blocks, finder-like patterns, colour balance.</summary>
        private long Penalty()
        {
            long penalty = 0;
            int dark = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int a = 0; a < _size; a++)
                {
                    int run = 0;
                    bool previous = false;
                    for (int b = 0; b < _size; b++)
                    {
                        bool c = pass == 0 ? _dark[a, b] : _dark[b, a];
                        if (b > 0 && c == previous) run++;
                        else
                        {
                            if (run >= 5) penalty += run - 2;
                            run = 1;
                            previous = c;
                        }
                        if (b + 10 < _size && IsFinderLike(a, b, pass == 1)) penalty += 40;
                    }
                    if (run >= 5) penalty += run - 2;
                }
            }
            for (int y = 0; y < _size; y++)
                for (int x = 0; x < _size; x++)
                {
                    if (_dark[y, x]) dark++;
                    if (x + 1 < _size && y + 1 < _size && _dark[y, x] == _dark[y, x + 1] && _dark[y, x] == _dark[y + 1, x] && _dark[y, x] == _dark[y + 1, x + 1])
                        penalty += 3;
                }
            int total = _size * _size;
            penalty += (int)(Math.Abs(dark * 100.0 / total - 50) / 5) * 10;
            return penalty;
        }

        private static readonly bool[] FinderLike = [true, false, true, true, true, false, true, false, false, false, false];

        private bool IsFinderLike(int line, int start, bool vertical)
        {
            bool forward = true, backward = true;
            for (int k = 0; k < 11; k++)
            {
                bool c = vertical ? _dark[start + k, line] : _dark[line, start + k];
                if (c != FinderLike[k]) forward = false;
                if (c != FinderLike[10 - k]) backward = false;
            }
            return forward || backward;
        }
    }

    private static class ReedSolomon
    {
        private static int Multiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= (y >> i & 1) * x;
            }
            return z;
        }

        /// <summary>Generator polynomial with roots α^0 … α^(degree−1), leading coefficient left out.</summary>
        public static byte[] Generator(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = (byte)Multiply(result[j], root);
                    if (j + 1 < degree) result[j] ^= result[j + 1];
                }
                root = Multiply(root, 2);
            }
            return result;
        }

        public static byte[] Remainder(byte[] data, byte[] generator)
        {
            var result = new byte[generator.Length];
            foreach (byte b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[^1] = 0;
                for (int i = 0; i < result.Length; i++) result[i] ^= (byte)Multiply(generator[i], factor);
            }
            return result;
        }
    }

    private sealed class BitList
    {
        private readonly List<bool> _bits = [];

        public int Count => _bits.Count;

        public void Add(int value, int length)
        {
            for (int i = length - 1; i >= 0; i--) _bits.Add((value >> i & 1) != 0);
        }

        public void AddAll(BitList other) => _bits.AddRange(other._bits);

        public byte[] ToBytes()
        {
            var bytes = new byte[_bits.Count / 8];
            for (int i = 0; i < bytes.Length * 8; i++)
                if (_bits[i]) bytes[i >> 3] |= (byte)(0x80 >> (i & 7));
            return bytes;
        }
    }
}
