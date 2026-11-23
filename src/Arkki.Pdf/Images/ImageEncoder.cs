using System.IO.Compression;

namespace Arkki.Pdf;

/// <summary>
/// Turns decoded 8-bit pixels into the smallest lossless PDF image encoding it can find:
/// grey instead of RGB when every pixel is grey, an indexed palette when there are at most 256
/// colours, and Flate with per-row PNG prediction when that compresses better than plain Flate.
/// </summary>
internal static class ImageEncoder
{
    internal readonly record struct Encoded(string Entries, byte[] Data);

    /// <summary>Encodes colour samples (1 or 3 channels per pixel) as an image XObject body.</summary>
    public static Encoded EncodeColor(byte[] samples, int width, int height, int channels)
    {
        if (channels == 3 && IsGrey(samples))
        {
            samples = ToGrey(samples);
            channels = 1;
        }
        if (channels == 3 && TryIndex(samples, out byte[] palette, out byte[] indices))
        {
            // Prediction does not help palette indices; plain Flate it is.
            var hex = new System.Text.StringBuilder(palette.Length * 2);
            foreach (byte b in palette) hex.Append(b.ToString("X2"));
            string cs = $"/ColorSpace [/Indexed /DeviceRGB {palette.Length / 3 - 1} <{hex}>]";
            return new Encoded($"{cs} /BitsPerComponent 8 /Filter /FlateDecode", Deflate(indices));
        }
        string space = channels == 3 ? "/ColorSpace /DeviceRGB" : "/ColorSpace /DeviceGray";
        return Compress(samples, width, height, channels, space);
    }

    /// <summary>Encodes an alpha channel as a soft mask body.</summary>
    public static Encoded EncodeAlpha(byte[] alpha, int width, int height) =>
        Compress(alpha, width, height, 1, "/ColorSpace /DeviceGray");

    private static Encoded Compress(byte[] samples, int width, int height, int channels, string space)
    {
        byte[] plain = Deflate(samples);
        byte[] predicted = Deflate(PngPredict(samples, width, height, channels));
        if (predicted.Length < plain.Length)
            return new Encoded($"{space} /BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors {channels} /Columns {width} >>", predicted);
        return new Encoded($"{space} /BitsPerComponent 8 /Filter /FlateDecode", plain);
    }

    private static bool IsGrey(byte[] rgb)
    {
        for (int i = 0; i + 2 < rgb.Length; i += 3)
            if (rgb[i] != rgb[i + 1] || rgb[i] != rgb[i + 2]) return false;
        return true;
    }

    private static byte[] ToGrey(byte[] rgb)
    {
        var grey = new byte[rgb.Length / 3];
        for (int i = 0; i < grey.Length; i++) grey[i] = rgb[i * 3];
        return grey;
    }

    private static bool TryIndex(byte[] rgb, out byte[] palette, out byte[] indices)
    {
        var map = new Dictionary<int, byte>(257);
        indices = new byte[rgb.Length / 3];
        for (int i = 0; i < indices.Length; i++)
        {
            int key = rgb[i * 3] << 16 | rgb[i * 3 + 1] << 8 | rgb[i * 3 + 2];
            if (!map.TryGetValue(key, out byte index))
            {
                if (map.Count == 256) { palette = []; indices = []; return false; }
                index = (byte)map.Count;
                map[key] = index;
            }
            indices[i] = index;
        }
        palette = new byte[map.Count * 3];
        foreach (var (key, index) in map)
        {
            palette[index * 3] = (byte)(key >> 16);
            palette[index * 3 + 1] = (byte)(key >> 8);
            palette[index * 3 + 2] = (byte)key;
        }
        return true;
    }

    /// <summary>Applies PNG row filters, choosing per row the one with the smallest sum of absolute residuals.</summary>
    private static byte[] PngPredict(byte[] samples, int width, int height, int bpp)
    {
        int stride = width * bpp;
        var output = new byte[(stride + 1) * height];
        var candidate = new byte[5][];
        for (int f = 0; f < 5; f++) candidate[f] = new byte[stride];
        var zero = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> cur = samples.AsSpan(y * stride, stride);
            ReadOnlySpan<byte> up = y > 0 ? samples.AsSpan((y - 1) * stride, stride) : zero;
            int best = 0;
            long bestScore = long.MaxValue;
            for (int f = 0; f < 5; f++)
            {
                byte[] c = candidate[f];
                long score = 0;
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? cur[x - bpp] : 0, b = up[x], d = x >= bpp ? up[x - bpp] : 0;
                    int predictor = f switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) >> 1, _ => Paeth(a, b, d) };
                    byte v = (byte)(cur[x] - predictor);
                    c[x] = v;
                    score += v < 128 ? v : 256 - v;
                }
                if (score < bestScore) { bestScore = score; best = f; }
            }
            output[y * (stride + 1)] = (byte)best;
            candidate[best].CopyTo(output, y * (stride + 1) + 1);
        }
        return output;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    internal static byte[] Deflate(byte[] raw)
    {
        using var ms = new MemoryStream(raw.Length / 4 + 64);
        using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(raw);
        return ms.ToArray();
    }

    /// <summary>
    /// Shrinks an image to fit within <paramref name="maxWidth"/> × <paramref name="maxHeight"/> pixels
    /// with an area-averaging filter. Colour is weighted by alpha, so transparent pixels do not
    /// darken the edges of a logo.
    /// </summary>
    public static (byte[] Color, byte[]? Alpha, int Width, int Height) Downscale(
        byte[] color, byte[]? alpha, int width, int height, int channels, int maxWidth, int maxHeight)
    {
        double scale = Math.Min(1.0, Math.Min(maxWidth > 0 ? (double)maxWidth / width : 1, maxHeight > 0 ? (double)maxHeight / height : 1));
        if (scale >= 1) return (color, alpha, width, height);
        int w = Math.Max(1, (int)Math.Round(width * scale)), h = Math.Max(1, (int)Math.Round(height * scale));
        var outColor = new byte[w * h * channels];
        byte[]? outAlpha = alpha is null ? null : new byte[w * h];
        var acc = new double[channels];
        for (int oy = 0; oy < h; oy++)
        {
            int y0 = oy * height / h, y1 = Math.Max(y0 + 1, (oy + 1) * height / h);
            for (int ox = 0; ox < w; ox++)
            {
                int x0 = ox * width / w, x1 = Math.Max(x0 + 1, (ox + 1) * width / w);
                Array.Clear(acc);
                double weight = 0, alphaSum = 0;
                int count = 0;
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        int i = y * width + x;
                        double a = alpha is null ? 1 : alpha[i] / 255.0;
                        for (int k = 0; k < channels; k++) acc[k] += color[i * channels + k] * a;
                        weight += a;
                        alphaSum += alpha is null ? 0 : alpha[i];
                        count++;
                    }
                }
                int o = oy * w + ox;
                for (int k = 0; k < channels; k++)
                    outColor[o * channels + k] = weight > 0 ? (byte)Math.Round(acc[k] / weight) : (byte)0;
                if (outAlpha is not null) outAlpha[o] = (byte)Math.Round(alphaSum / count);
            }
        }
        return (outColor, outAlpha, w, h);
    }
}
