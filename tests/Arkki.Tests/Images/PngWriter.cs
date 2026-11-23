using System.Buffers.Binary;
using System.IO.Compression;

namespace Arkki.Tests;

/// <summary>Writes minimal 8-bit PNG files (colour type 2 or 6) for image tests.</summary>
internal static class PngWriter
{
    public static byte[] Write(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel, bool alpha)
    {
        int channels = alpha ? 4 : 3;
        var raw = new byte[(width * channels + 1) * height];
        for (int y = 0; y < height; y++)
        {
            int row = y * (width * channels + 1);
            for (int x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                int i = row + 1 + x * channels;
                raw[i] = r; raw[i + 1] = g; raw[i + 2] = b;
                if (alpha) raw[i + 3] = a;
            }
        }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Fastest, leaveOpen: true)) zs.Write(raw);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; ihdr[9] = (byte)(alpha ? 6 : 2);

        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10]);
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", z.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        byte[] typed = [.. System.Text.Encoding.ASCII.GetBytes(type), .. data];
        s.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(len, Crc32(typed));
        s.Write(len);
    }

    private static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return ~c;
    }
}
