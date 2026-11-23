using System.Text;

namespace Arkki.Pdf.Reading;

/// <summary>
/// Tokenizer and object parser over the bytes of a PDF file (ISO 32000-1, section 7.2–7.3).
/// Stream data is not copied: a parsed stream points into the file's buffer.
/// </summary>
internal sealed class PdfParser(ReadOnlyMemory<byte> data)
{
    private const int MaxDepth = 256;

    public ReadOnlyMemory<byte> Data { get; } = data;
    public int Position { get; set; }

    /// <summary>Resolves an indirect /Length while a stream is parsed; null when lengths cannot be resolved.</summary>
    public Func<PdfRef, PdfValue?>? ResolveLength { get; init; }

    private ReadOnlySpan<byte> Span => Data.Span;

    public static bool IsWhite(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;
    public static bool IsDelimiter(byte b) => b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';
    private static bool IsRegular(byte b) => !IsWhite(b) && !IsDelimiter(b);

    public void SkipWhiteAndComments()
    {
        ReadOnlySpan<byte> s = Span;
        while (Position < s.Length)
        {
            byte b = s[Position];
            if (IsWhite(b)) { Position++; continue; }
            if (b == '%')
            {
                while (Position < s.Length && s[Position] is not (10 or 13)) Position++;
                continue;
            }
            break;
        }
    }

    /// <summary>Reads a run of regular characters (a keyword or number) without consuming anything else.</summary>
    public string ReadToken()
    {
        SkipWhiteAndComments();
        ReadOnlySpan<byte> s = Span;
        int start = Position;
        while (Position < s.Length && IsRegular(s[Position])) Position++;
        return Encoding.ASCII.GetString(s[start..Position]);
    }

    public bool TryReadKeyword(string keyword)
    {
        int saved = Position;
        if (ReadToken() == keyword) return true;
        Position = saved;
        return false;
    }

    public void ExpectKeyword(string keyword)
    {
        int at = Position;
        string t = ReadToken();
        if (t != keyword) throw new InvalidDataException($"Expected '{keyword}' at byte {at}, found '{t}'.");
    }

    public static bool IsInteger(string token) =>
        token.Length > 0 && token.Length < 11 && token.All(char.IsAsciiDigit);

    /// <summary>Reads "n g obj" and returns the object number and generation.</summary>
    public (int Number, int Generation) ReadObjectHeader()
    {
        int at = Position;
        string n = ReadToken(), g = ReadToken(), kw = ReadToken();
        if (!IsInteger(n) || !IsInteger(g) || kw != "obj")
            throw new InvalidDataException($"Expected an object header at byte {at}.");
        return (int.Parse(n), int.Parse(g));
    }

    /// <summary>Parses one object. Inside an indirect object a dictionary followed by "stream" becomes a <see cref="PdfStream"/>.</summary>
    public PdfValue ReadObject(bool allowStream = false, int depth = 0)
    {
        if (depth > MaxDepth) throw new InvalidDataException("Objects are nested too deeply.");
        SkipWhiteAndComments();
        ReadOnlySpan<byte> s = Span;
        if (Position >= s.Length) throw new InvalidDataException("Unexpected end of file.");
        byte b = s[Position];
        switch (b)
        {
            case (byte)'/':
                return new PdfName(ReadNameBody());
            case (byte)'(':
                return new PdfString(ReadLiteralString(), hex: false);
            case (byte)'[':
            {
                Position++;
                var items = new List<PdfValue>();
                while (true)
                {
                    SkipWhiteAndComments();
                    if (Position >= Span.Length) throw new InvalidDataException("Unterminated array.");
                    if (Span[Position] == ']') { Position++; break; }
                    items.Add(ReadObject(false, depth + 1));
                }
                return new PdfArray(items);
            }
            case (byte)'<':
                if (Position + 1 < s.Length && s[Position + 1] == '<')
                {
                    var dict = ReadDictionary(depth);
                    return allowStream ? MaybeStream(dict) : new PdfDict(dict);
                }
                return new PdfString(ReadHexString(), hex: true);
        }

        string token = ReadToken();
        if (token.Length == 0)
        {
            Position++; // a stray delimiter such as ')' or '>'; skip it rather than loop forever
            return PdfNull.Instance;
        }
        switch (token)
        {
            case "null": return PdfNull.Instance;
            case "true": return PdfBool.True;
            case "false": return PdfBool.False;
        }
        if (IsInteger(token))
        {
            // "n g R" is a reference; look ahead without committing.
            int saved = Position;
            string g = ReadToken();
            if (IsInteger(g) && ReadToken() == "R") return new PdfRef(int.Parse(token), int.Parse(g));
            Position = saved;
            return new PdfNumber(token);
        }
        if (token[0] is '+' or '-' or '.' || char.IsAsciiDigit(token[0])) return new PdfNumber(NormalizeNumber(token));
        // An unknown keyword (for example "endobj" of a malformed object): treat as null.
        return PdfNull.Instance;
    }

    private static string NormalizeNumber(string token)
    {
        // Tolerate common malformed numbers such as "--1" or "1.2.3" by keeping the valid prefix.
        int i = 0;
        while (i < token.Length && token[i] is '+' or '-') i++;
        string sign = token.Contains('-') && i > 0 && token[i - 1] == '-' ? "-" : "";
        int start = i; bool dot = false;
        while (i < token.Length && (char.IsAsciiDigit(token[i]) || (token[i] == '.' && !dot))) { dot |= token[i] == '.'; i++; }
        string digits = token[start..i];
        return digits.Length == 0 || digits == "." ? "0" : sign + digits;
    }

    private List<KeyValuePair<string, PdfValue>> ReadDictionary(int depth)
    {
        Position += 2;
        var entries = new List<KeyValuePair<string, PdfValue>>();
        while (true)
        {
            SkipWhiteAndComments();
            ReadOnlySpan<byte> s = Span;
            if (Position >= s.Length) throw new InvalidDataException("Unterminated dictionary.");
            if (s[Position] == '>' && Position + 1 < s.Length && s[Position + 1] == '>') { Position += 2; break; }
            if (s[Position] != '/')
            {
                // Garbage where a key should be: skip one value to resynchronise.
                ReadObject(false, depth + 1);
                continue;
            }
            string key = ReadNameBody();
            SkipWhiteAndComments();
            if (Position < Span.Length && Span[Position] == '>' && Position + 1 < Span.Length && Span[Position + 1] == '>')
            {
                entries.Add(new(key, PdfNull.Instance));
                continue;
            }
            PdfValue value = ReadObject(false, depth + 1);
            // A later duplicate key wins, as in most readers.
            int existing = entries.FindIndex(e => e.Key == key);
            if (existing >= 0) entries[existing] = new(key, value);
            else entries.Add(new(key, value));
        }
        return entries;
    }

    private PdfValue MaybeStream(List<KeyValuePair<string, PdfValue>> dict)
    {
        int saved = Position;
        if (ReadToken() != "stream") { Position = saved; return new PdfDict(dict); }
        ReadOnlySpan<byte> s = Span;
        // The keyword is followed by CRLF or LF (a lone CR is tolerated).
        if (Position < s.Length && s[Position] == 13) Position++;
        if (Position < s.Length && s[Position] == 10) Position++;
        int start = Position;

        int length = -1;
        PdfValue? lengthValue = dict.FirstOrDefault(e => e.Key == "Length").Value;
        if (lengthValue is PdfRef r && ResolveLength is not null) lengthValue = ResolveLength(r);
        if (lengthValue is PdfNumber n) length = n.IntValue;

        if (length >= 0 && start + length <= s.Length && EndstreamFollows(start + length))
        {
            Position = start + length;
        }
        else
        {
            // Missing or wrong /Length: find "endstream" and trim the end-of-line before it.
            int end = s[start..].IndexOf("endstream"u8);
            if (end < 0) throw new InvalidDataException($"Stream at byte {start} has no 'endstream'.");
            length = end;
            if (length > 0 && s[start + length - 1] == 10) length--;
            if (length > 0 && s[start + length - 1] == 13) length--;
            Position = start + end;
        }
        var data = Data.Slice(start, length);
        SkipWhiteAndComments();
        TryReadKeyword("endstream");
        return new PdfStream(dict, data);
    }

    private bool EndstreamFollows(int at)
    {
        ReadOnlySpan<byte> s = Span;
        while (at < s.Length && IsWhite(s[at])) at++;
        return s[at..].StartsWith("endstream"u8);
    }

    /// <summary>Reads a name after its slash and returns it in encoded form.</summary>
    private string ReadNameBody()
    {
        ReadOnlySpan<byte> s = Span;
        Position++; // '/'
        int start = Position;
        while (Position < s.Length && IsRegular(s[Position])) Position++;
        return Encoding.Latin1.GetString(s[start..Position]);
    }

    private byte[] ReadLiteralString()
    {
        ReadOnlySpan<byte> s = Span;
        Position++; // '('
        var result = new List<byte>(32);
        int depth = 1;
        while (Position < s.Length)
        {
            byte b = s[Position++];
            if (b == '\\')
            {
                if (Position >= s.Length) break;
                byte e = s[Position++];
                switch (e)
                {
                    case (byte)'n': result.Add(10); break;
                    case (byte)'r': result.Add(13); break;
                    case (byte)'t': result.Add(9); break;
                    case (byte)'b': result.Add(8); break;
                    case (byte)'f': result.Add(12); break;
                    case 13:
                        if (Position < s.Length && s[Position] == 10) Position++;
                        break; // line continuation
                    case 10: break;
                    case >= (byte)'0' and <= (byte)'7':
                    {
                        int v = e - '0';
                        for (int k = 0; k < 2 && Position < s.Length && s[Position] is >= (byte)'0' and <= (byte)'7'; k++)
                            v = v * 8 + (s[Position++] - '0');
                        result.Add((byte)v);
                        break;
                    }
                    default: result.Add(e); break;
                }
                continue;
            }
            if (b == '(') depth++;
            else if (b == ')' && --depth == 0) break;
            result.Add(b);
        }
        return [.. result];
    }

    private byte[] ReadHexString()
    {
        ReadOnlySpan<byte> s = Span;
        Position++; // '<'
        var result = new List<byte>(16);
        int high = -1;
        while (Position < s.Length)
        {
            byte b = s[Position++];
            if (b == '>') break;
            int v = HexValue(b);
            if (v < 0) continue;
            if (high < 0) high = v;
            else { result.Add((byte)(high * 16 + v)); high = -1; }
        }
        if (high >= 0) result.Add((byte)(high * 16));
        return [.. result];
    }

    private static int HexValue(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => -1,
    };
}
