using System.IO.Compression;

namespace Arkki.Pdf.Internal;

/// <summary>
/// Low-level PDF file writer: indirect objects, streams, cross-reference table and trailer.
/// Objects are written straight to the output stream; only byte offsets are kept in memory.
/// </summary>
internal sealed class PdfWriter
{
    private readonly Stream _out;
    private readonly List<long> _offsets = [0];
    private readonly ByteBuf _head = new(64);
    private long _pos;

    public CompressionLevel Compression { get; init; } = CompressionLevel.Optimal;

    /// <summary>Encrypts every string and stream written after it is set; null writes them as they are.</summary>
    public Security.Encryptor? Encryptor { get; set; }

    public PdfWriter(Stream output)
    {
        _out = output;
        // Header plus a binary comment so transports treat the file as binary.
        ReadOnlySpan<byte> header = [(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'7', 10,
            (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, 10];
        Raw(header);
    }

    public int Reserve()
    {
        _offsets.Add(-1);
        return _offsets.Count - 1;
    }

    private void Raw(ReadOnlySpan<byte> data)
    {
        _out.Write(data);
        _pos += data.Length;
    }

    private void Begin(int id)
    {
        _offsets[id] = _pos;
        _head.Reset();
        _head.Int(id).Ascii(" 0 obj\n");
        Raw(_head.Span);
    }

    /// <summary>Writes a non-stream object whose body is already serialized.</summary>
    public void WriteObject(int id, ByteBuf body) => WriteObject(id, body.Span);

    /// <summary>Writes a non-stream object whose body is already serialized; with encryption on, its strings are encrypted.</summary>
    public void WriteObject(int id, ReadOnlySpan<byte> body, bool encrypt = true)
    {
        Begin(id);
        Raw(encrypt && Encryptor is not null ? Encryptor.EncryptStrings(body, id) : body);
        Raw("\nendobj\n"u8);
    }

    /// <summary>Writes a stream from its dictionary (opening "&lt;&lt;" and entries, no /Length and no closing) and its stored data.</summary>
    public void WriteRawStream(int id, ReadOnlySpan<byte> dictHead, ReadOnlySpan<byte> data)
    {
        byte[]? encrypted = Encryptor?.EncryptData(data);
        ReadOnlySpan<byte> payload = encrypted ?? data;
        Begin(id);
        _head.Reset();
        _head.Bytes(Encryptor is not null ? Encryptor.EncryptStrings(dictHead, id, open: true) : dictHead);
        _head.Ascii(" /Length ").Int(payload.Length).Ascii(" >>\nstream\n");
        Raw(_head.Span);
        Raw(payload);
        Raw("\nendstream\nendobj\n"u8);
    }

    /// <summary>
    /// Writes a stream object. <paramref name="dictEntries"/> holds extra dictionary entries
    /// (without the enclosing brackets); /Length and /Filter are added here.
    /// </summary>
    public void WriteStream(int id, string dictEntries, ReadOnlySpan<byte> data, bool compress)
    {
        byte[]? packed = null;
        int packedLength = 0;
        if (compress)
        {
            using var ms = new MemoryStream(Math.Max(64, data.Length / 2));
            using (var z = new ZLibStream(ms, Compression, leaveOpen: true)) z.Write(data);
            packed = ms.GetBuffer();
            packedLength = (int)ms.Length;
        }

        var head = new ByteBuf(64);
        head.Ascii("<< ").Ascii(dictEntries);
        if (compress) head.Ascii(" /Filter /FlateDecode");
        WriteRawStream(id, head.Span, compress ? packed.AsSpan(0, packedLength) : data);
    }

    /// <summary>Writes a stream whose data is already encoded; the dictionary must name the filter.</summary>
    public void WriteEncodedStream(int id, string dictEntries, ReadOnlySpan<byte> encoded)
    {
        var head = new ByteBuf(64);
        head.Ascii("<< ").Ascii(dictEntries);
        WriteRawStream(id, head.Span, encoded);
    }

    public void Finish(int rootId, int infoId, ReadOnlySpan<byte> documentId, int encryptId = 0)
    {
        long xref = _pos;
        var b = new ByteBuf(_offsets.Count * 20 + 128);
        b.Ascii("xref\n0 ").Int(_offsets.Count).Nl();
        b.Ascii("0000000000 65535 f \n");
        Span<char> digits = stackalloc char[10];
        for (int i = 1; i < _offsets.Count; i++)
        {
            if (_offsets[i] < 0) throw new InvalidOperationException($"PDF object {i} was reserved but never written.");
            _offsets[i].TryFormat(digits, out _, "D10");
            foreach (char c in digits) b.Byte((byte)c);
            b.Ascii(" 00000 n \n");
        }
        b.Ascii("trailer\n<< /Size ").Int(_offsets.Count)
            .Ascii(" /Root ").Int(rootId).Ascii(" 0 R /Info ").Int(infoId).Ascii(" 0 R");
        if (encryptId > 0) b.Ascii(" /Encrypt ").Int(encryptId).Ascii(" 0 R");
        b.Ascii(" /ID [<");
        foreach (byte x in documentId) b.Hex8(x);
        b.Ascii("> <");
        foreach (byte x in documentId) b.Hex8(x);
        b.Ascii(">] >>\nstartxref\n").Int(xref).Ascii("\n%%EOF\n");
        Raw(b.Span);
    }
}
