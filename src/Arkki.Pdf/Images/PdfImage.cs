using System.Buffers.Binary;
using System.IO.Compression;
using Arkki.Pdf.Internal;

namespace Arkki.Pdf;

/// <summary>
/// A raster image. JPEG data is passed through untouched (DCTDecode); PNG data is decoded once
/// and stored losslessly, with the alpha channel as a soft mask. Instances are immutable: load a
/// logo once at start-up and reuse it in every document, from any thread. The decoded and
/// compressed form is kept, so reuse costs nothing per document.
/// </summary>
public sealed class PdfImage
{
    private readonly string _entries;   // dictionary entries after /Height: colour space, bits, filter
    private readonly byte[] _data;      // encoded samples (JPEG file bytes for DCTDecode)
    private readonly ImageEncoder.Encoded? _mask;

    private PdfImage(int width, int height, string entries, byte[] data, ImageEncoder.Encoded? mask)
    {
        Width = width; Height = height;
        _entries = entries; _data = data; _mask = mask;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Size of the encoded image data in bytes, soft mask included: what embedding it adds to a file.</summary>
    public int EncodedLength => _data.Length + (_mask?.Data.Length ?? 0);

    /// <summary>Loads a PNG or JPEG image.</summary>
    public static PdfImage Load(byte[] data) => Load(data, 0, 0);

    /// <summary>
    /// Loads a PNG or JPEG image, shrinking a PNG that is larger than
    /// <paramref name="maxPixelWidth"/> × <paramref name="maxPixelHeight"/> (0 means no limit).
    /// Pick the limits from the printed size: a 60 mm wide logo needs about 710 pixels at 300 dpi.
    /// JPEG data is never re-encoded, so a JPEG keeps its size.
    /// </summary>
    public static PdfImage Load(byte[] data, int maxPixelWidth, int maxPixelHeight)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPixelHeight);
        try
        {
            if (data.Length > 8 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G') return LoadPng(data, maxPixelWidth, maxPixelHeight);
            if (data.Length > 4 && data[0] == 0xFF && data[1] == 0xD8) return LoadJpeg(data);
        }
        catch (Exception e) when (e is ArgumentException || e is IndexOutOfRangeException || e is OverflowException || (e is IOException && e is not InvalidDataException))
        {
            throw new InvalidDataException("The image file is truncated or damaged.", e);
        }
        throw new NotSupportedException("Unsupported image format. Supported formats: PNG, JPEG.");
    }

    internal void Write(PdfWriter w, int id)
    {
        string dict = $"/Type /XObject /Subtype /Image /Width {Width} /Height {Height} {_entries}";
        if (_mask is { } mask)
        {
            int maskId = w.Reserve();
            w.WriteEncodedStream(maskId, $"/Type /XObject /Subtype /Image /Width {Width} /Height {Height} {mask.Entries}", mask.Data);
            dict += $" /SMask {maskId} 0 R";
        }
        w.WriteEncodedStream(id, dict, _data);
    }

    // ---- JPEG ----------------------------------------------------------------------------

    private static PdfImage LoadJpeg(byte[] d)
    {
        int pos = 2;
        bool adobe = false;
        while (pos + 9 < d.Length)
        {
            if (d[pos] != 0xFF) { pos++; continue; }
            int marker = d[pos + 1];
            if (marker == 0xFF) { pos++; continue; }
            if (marker is 0x01 or (>= 0xD0 and <= 0xD9)) { pos += 2; continue; }
            int len = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(pos + 2));
            if (marker == 0xEE) adobe = true;
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                int height = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(pos + 5));
                int width = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(pos + 7));
                int components = d[pos + 9];
                string cs = components switch { 1 => "DeviceGray", 3 => "DeviceRGB", 4 => "DeviceCMYK", _ => throw new NotSupportedException("Unsupported JPEG component count.") };
                string decode = components == 4 && adobe ? " /Decode [1 0 1 0 1 0 1 0]" : "";
                return new PdfImage(width, height, $"/ColorSpace /{cs} /BitsPerComponent 8{decode} /Filter /DCTDecode", d, null);
            }
            pos += 2 + len;
        }
        throw new InvalidDataException("JPEG has no frame header.");
    }

    // ---- PNG -----------------------------------------------------------------------------

    private static PdfImage LoadPng(byte[] d, int maxWidth, int maxHeight)
    {
        int width = 0, height = 0, depth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null, trns = null;
        using var idat = new MemoryStream();
        int pos = 8;
        while (pos + 8 <= d.Length)
        {
            int len = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(pos));
            uint type = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(pos + 4));
            var chunk = d.AsSpan(pos + 8, len);
            switch (type)
            {
                case 0x49484452: // IHDR
                    width = (int)BinaryPrimitives.ReadUInt32BigEndian(chunk);
                    height = (int)BinaryPrimitives.ReadUInt32BigEndian(chunk[4..]);
                    depth = chunk[8]; colorType = chunk[9]; interlace = chunk[12];
                    break;
                case 0x504C5445: palette = chunk.ToArray(); break; // PLTE
                case 0x74524E53: trns = chunk.ToArray(); break;    // tRNS
                case 0x49444154: idat.Write(chunk); break;         // IDAT
            }
            if (type == 0x49454E44) break; // IEND
            pos += 12 + len;
        }
        if (width == 0) throw new InvalidDataException("PNG has no IHDR chunk.");
        if (interlace != 0) throw new NotSupportedException("Interlaced PNG images are not supported.");

        int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new InvalidDataException("Invalid PNG color type.") };
        int bpp = Math.Max(1, channels * depth / 8);
        int stride = (width * channels * depth + 7) / 8;

        var raw = new byte[(stride + 1) * height];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.ReadExactly(raw);

        // Undo the per-row prediction filters in place.
        var prev = new byte[stride];
        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            int filter = raw[y * (stride + 1)];
            var src = raw.AsSpan(y * (stride + 1) + 1, stride);
            var dst = pixels.AsSpan(y * stride, stride);
            for (int x = 0; x < stride; x++)
            {
                int a = x >= bpp ? dst[x - bpp] : 0, b = prev[x], c = x >= bpp ? prev[x - bpp] : 0;
                int predictor = filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) >> 1,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException("Invalid PNG filter type."),
                };
                dst[x] = (byte)(src[x] + predictor);
            }
            dst.CopyTo(prev);
        }

        bool rgb = colorType is 2 or 3 or 6;
        bool hasAlpha = colorType is 4 or 6 || (colorType == 3 && trns is not null);
        int outChannels = rgb ? 3 : 1;
        var color = new byte[width * height * outChannels];
        byte[]? alpha = hasAlpha ? new byte[width * height] : null;

        for (int y = 0; y < height; y++)
        {
            var row = pixels.AsSpan(y * stride, stride);
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                switch (colorType)
                {
                    case 0:
                        color[i] = Sample(row, x, depth, scale: true);
                        break;
                    case 2:
                        for (int k = 0; k < 3; k++) color[i * 3 + k] = Sample(row, x * 3 + k, depth, scale: true);
                        break;
                    case 3:
                        int index = Sample(row, x, depth, scale: false);
                        if (palette is not null && index * 3 + 2 < palette.Length)
                            palette.AsSpan(index * 3, 3).CopyTo(color.AsSpan(i * 3));
                        if (alpha is not null) alpha[i] = index < trns!.Length ? trns[index] : (byte)255;
                        break;
                    case 4:
                        color[i] = Sample(row, x * 2, depth, scale: true);
                        alpha![i] = Sample(row, x * 2 + 1, depth, scale: true);
                        break;
                    case 6:
                        for (int k = 0; k < 3; k++) color[i * 3 + k] = Sample(row, x * 4 + k, depth, scale: true);
                        alpha![i] = Sample(row, x * 4 + 3, depth, scale: true);
                        break;
                }
            }
        }

        // An alpha channel that is fully opaque carries no information; skip the soft mask.
        if (alpha is not null && alpha.AsSpan().IndexOfAnyExcept((byte)255) < 0) alpha = null;
        (color, alpha, width, height) = ImageEncoder.Downscale(color, alpha, width, height, outChannels, maxWidth, maxHeight);
        ImageEncoder.Encoded body = ImageEncoder.EncodeColor(color, width, height, outChannels);
        ImageEncoder.Encoded? mask = alpha is null ? null : ImageEncoder.EncodeAlpha(alpha, width, height);
        return new PdfImage(width, height, body.Entries, body.Data, mask);
    }

    private static byte Sample(ReadOnlySpan<byte> row, int index, int depth, bool scale)
    {
        switch (depth)
        {
            case 8: return row[index];
            case 16: return row[index * 2];
            default:
                int bit = index * depth;
                int v = (row[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
                return (byte)(scale ? v * 255 / ((1 << depth) - 1) : v);
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
