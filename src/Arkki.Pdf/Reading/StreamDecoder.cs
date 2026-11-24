using System.IO.Compression;

namespace Arkki.Pdf.Reading;

/// <summary>
/// Decodes stream data for the reader's own use (cross-reference and object streams). Imported
/// streams are never decoded: they are copied in their stored form.
/// </summary>
internal static class StreamDecoder
{
    public static byte[] Decode(PdfStream stream, Func<PdfValue?, PdfValue?> resolve)
    {
        PdfValue? filter = resolve(stream.Get("Filter"));
        PdfValue? parms = resolve(stream.Get("DecodeParms"));
        List<PdfValue> filters = filter switch
        {
            null or PdfNull => [],
            PdfArray a => a.Items,
            _ => [filter],
        };
        byte[] data = stream.Data.ToArray();
        for (int i = 0; i < filters.Count; i++)
        {
            string name = resolve(filters[i]) is PdfName n ? n.Encoded : "";
            PdfDict? p = (parms is PdfArray pa ? (i < pa.Items.Count ? resolve(pa.Items[i]) : null) : parms) as PdfDict;
            data = name switch
            {
                "FlateDecode" or "Fl" => Predict(Inflate(data), p, resolve),
                _ => throw new NotSupportedException($"Stream filter /{name} is not supported here."),
            };
        }
        return data;
    }

    /// <summary>The most one stream may decode to; more is treated as a decompression bomb.</summary>
    internal const int MaxDecodedLength = 256 * 1024 * 1024;

    private static byte[] Inflate(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var output = new MemoryStream(Math.Min(data.Length * 4 + 64, 1 << 20));
        bool bomb = false;
        try
        {
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            var buffer = new byte[81920];
            int read;
            while ((read = z.Read(buffer)) > 0)
            {
                if (output.Length + read > MaxDecodedLength)
                {
                    bomb = true;
                    throw new InvalidDataException($"A stream decodes to more than {MaxDecodedLength / (1024 * 1024)} MB; it is refused as a decompression bomb.");
                }
                output.Write(buffer, 0, read);
            }
        }
        catch (IOException e) when (!bomb)
        {
            // Truncated or slightly corrupt data: keep what was decoded (readers commonly do).
            if (output.Length == 0) throw new InvalidDataException("A compressed stream is damaged.", e);
        }
        return output.ToArray();
    }

    private static int Int(PdfDict? d, string key, int fallback, Func<PdfValue?, PdfValue?> resolve) =>
        d is not null && resolve(d.Get(key)) is PdfNumber n ? n.IntValue : fallback;

    private static byte[] Predict(byte[] data, PdfDict? parms, Func<PdfValue?, PdfValue?> resolve)
    {
        int predictor = Int(parms, "Predictor", 1, resolve);
        if (predictor < 2) return data;
        int colors = Int(parms, "Colors", 1, resolve);
        int bits = Int(parms, "BitsPerComponent", 8, resolve);
        int columns = Int(parms, "Columns", 1, resolve);
        int bpp = Math.Max(1, (colors * bits + 7) / 8);
        int rowLength = (colors * bits * columns + 7) / 8;

        if (predictor == 2)
        {
            if (bits != 8) throw new NotSupportedException("TIFF predictor is supported for 8-bit components only.");
            var outTiff = (byte[])data.Clone();
            for (int row = 0; row + rowLength <= outTiff.Length; row += rowLength)
                for (int i = bpp; i < rowLength; i++) outTiff[row + i] += outTiff[row + i - bpp];
            return outTiff;
        }

        // PNG predictors: every row starts with its filter type byte.
        int rows = data.Length / (rowLength + 1);
        var result = new byte[rows * rowLength];
        var prev = new byte[rowLength];
        for (int r = 0; r < rows; r++)
        {
            int src = r * (rowLength + 1);
            byte type = data[src];
            Span<byte> cur = result.AsSpan(r * rowLength, rowLength);
            data.AsSpan(src + 1, rowLength).CopyTo(cur);
            for (int i = 0; i < rowLength; i++)
            {
                int left = i >= bpp ? cur[i - bpp] : 0, up = prev[i], upLeft = i >= bpp ? prev[i - bpp] : 0;
                cur[i] += type switch
                {
                    1 => (byte)left,
                    2 => (byte)up,
                    3 => (byte)((left + up) / 2),
                    4 => (byte)Paeth(left, up, upLeft),
                    _ => 0,
                };
            }
            cur.CopyTo(prev);
        }
        return result;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
