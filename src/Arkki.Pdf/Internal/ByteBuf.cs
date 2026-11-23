using System.Buffers.Text;

namespace Arkki.Pdf.Internal;

/// <summary>Growable byte buffer with allocation-free ASCII/number writers for PDF syntax.</summary>
internal sealed class ByteBuf
{
    private byte[] _b;
    private int _n;

    public ByteBuf(int capacity = 256) => _b = new byte[capacity];

    public int Length => _n;
    public ReadOnlySpan<byte> Span => _b.AsSpan(0, _n);
    public void Reset() => _n = 0;

    private void Ensure(int extra)
    {
        if (_n + extra > _b.Length) Array.Resize(ref _b, Math.Max(_b.Length * 2, _n + extra));
    }

    public ByteBuf Byte(byte v) { Ensure(1); _b[_n++] = v; return this; }
    public ByteBuf Sp() => Byte((byte)' ');
    public ByteBuf Nl() => Byte((byte)'\n');

    public ByteBuf Bytes(ReadOnlySpan<byte> v)
    {
        Ensure(v.Length);
        v.CopyTo(_b.AsSpan(_n));
        _n += v.Length;
        return this;
    }

    /// <summary>Writes a string whose characters are all in the 0..255 range, one byte each.</summary>
    public ByteBuf Ascii(string s)
    {
        Ensure(s.Length);
        for (int i = 0; i < s.Length; i++) _b[_n++] = (byte)s[i];
        return this;
    }

    public ByteBuf Int(long v)
    {
        Ensure(20);
        Utf8Formatter.TryFormat(v, _b.AsSpan(_n), out int w);
        _n += w;
        return this;
    }

    /// <summary>Writes a real number with at most 4 decimals and no trailing zeros.</summary>
    public ByteBuf Num(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
        double r = Math.Round(v, 4);
        if (r == Math.Floor(r) && Math.Abs(r) < 1e15) return Int((long)r);
        Span<byte> tmp = stackalloc byte[40];
        Utf8Formatter.TryFormat(r, tmp, out int n, new System.Buffers.StandardFormat('F', 4));
        while (tmp[n - 1] == (byte)'0') n--;
        if (tmp[n - 1] == (byte)'.') n--;
        return Bytes(tmp[..n]);
    }

    private const string HexDigits = "0123456789ABCDEF";

    public ByteBuf Hex16(int v)
    {
        Ensure(4);
        _b[_n++] = (byte)HexDigits[(v >> 12) & 15];
        _b[_n++] = (byte)HexDigits[(v >> 8) & 15];
        _b[_n++] = (byte)HexDigits[(v >> 4) & 15];
        _b[_n++] = (byte)HexDigits[v & 15];
        return this;
    }

    public ByteBuf Hex8(int v)
    {
        Ensure(2);
        _b[_n++] = (byte)HexDigits[(v >> 4) & 15];
        _b[_n++] = (byte)HexDigits[v & 15];
        return this;
    }

    /// <summary>Writes a PDF text string as a UTF-16BE hex string with BOM, valid for any Unicode content.</summary>
    public ByteBuf TextString(string s)
    {
        Ascii("<FEFF");
        foreach (char ch in s) Hex16(ch);
        return Byte((byte)'>');
    }
}
