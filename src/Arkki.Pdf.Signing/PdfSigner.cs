using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Arkki.Pdf.Internal;
using Arkki.Pdf.Reading;
using Arkki.Pdf.Security;
using Arkki.Pdf.Signing.Internal;

namespace Arkki.Pdf.Signing;

/// <summary>
/// Signs a PDF with a certificate and its private key as a PAdES baseline signature: B-B, or B-T when
/// <see cref="PdfSignatureOptions.Timestamp"/> is set. The signature is appended as an incremental update, so the
/// original bytes, and any signature already in the file, stay intact. The field is invisible.
/// <para>Whether a viewer shows the signature as trusted depends on the certificate: one from a CA on the Adobe
/// (AATL) or EU trusted lists shows green; a self-issued one is valid but untrusted.</para>
/// </summary>
public static class PdfSigner
{
    private const string Data = "1.2.840.113549.1.7.1";

    /// <summary>Signs without a timestamp. For a timestamped signature use <see cref="SignAsync"/>.</summary>
    /// <exception cref="ArgumentException">The certificate has no private key, the page does not exist or the field name is taken.</exception>
    /// <exception cref="NotSupportedException">The PDF is encrypted.</exception>
    public static byte[] Sign(byte[] pdf, X509Certificate2 certificate, PdfSignatureOptions? options = null)
    {
        options ??= new PdfSignatureOptions();
        if (options.Timestamp is not null) throw new InvalidOperationException("A timestamped signature calls the authority: use SignAsync.");
        return SignAsync(pdf, certificate, options).GetAwaiter().GetResult(); // completes synchronously without a timestamp
    }

    /// <summary>Signs, and timestamps the signature when <see cref="PdfSignatureOptions.Timestamp"/> is set.</summary>
    public static async Task<byte[]> SignAsync(byte[] pdf, X509Certificate2 certificate, PdfSignatureOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(certificate);
        options ??= new PdfSignatureOptions();
        if (!certificate.HasPrivateKey) throw new ArgumentException("The certificate has no private key to sign with.", nameof(certificate));
        if (string.IsNullOrWhiteSpace(options.FieldName)) throw new ArgumentException("The signature field needs a name.", nameof(options));
        if (options.FieldName.Contains('.')) throw new ArgumentException("A signature field name cannot contain '.'.", nameof(options));
        if (options.ReservedSize < 0) throw new ArgumentException("The reserved size cannot be negative.", nameof(options));
        string digestOid = options.HashAlgorithm.Name switch
        {
            "SHA256" => "2.16.840.1.101.3.4.2.1",
            "SHA384" => "2.16.840.1.101.3.4.2.2",
            "SHA512" => "2.16.840.1.101.3.4.2.3",
            _ => throw new ArgumentException("The digest must be SHA-256, SHA-384 or SHA-512.", nameof(options)),
        };

        PdfReader reader;
        try { reader = PdfReader.Open(pdf); }
        catch (PdfPasswordException e) { throw new NotSupportedException("Signing an encrypted PDF is not supported.", e); }
        if (reader.IsEncrypted) throw new NotSupportedException("Signing an encrypted PDF is not supported.");
        if ((uint)options.PageIndex >= (uint)reader.PageCount) throw new ArgumentException($"The document has no page {options.PageIndex}.", nameof(options));
        if (reader.GetFormFields().Any(f => f.Name == options.FieldName)) throw new ArgumentException($"The document already has a field named '{options.FieldName}'.", nameof(options));

        DateTimeOffset signingTime = options.SigningTime ?? DateTimeOffset.Now;
        int reserved = options.ReservedSize > 0 ? options.ReservedSize : EstimateSize(certificate, options);
        for (int attempt = 0; ; attempt++)
        {
            var update = BuildUpdate(reader, pdf, options, certificate, signingTime, reserved);
            byte[] cms = await CreateSignatureAsync(update.SignedBytes(), certificate, digestOid, options, cancellationToken).ConfigureAwait(false);
            if (cms.Length <= reserved) return update.Complete(cms);
            if (options.ReservedSize > 0 || attempt > 0)
                throw new InvalidOperationException($"The signature needs {cms.Length} bytes but {reserved} were reserved; raise ReservedSize.");
            reserved = cms.Length + 2048;
        }
    }

    private static int EstimateSize(X509Certificate2 certificate, PdfSignatureOptions options)
    {
        int size = 2048 + certificate.RawData.Length;
        if (options.ExtraCertificates is not null) foreach (var c in options.ExtraCertificates) size += c.RawData.Length;
        if (options.Timestamp is not null) size += 6144;
        return size;
    }

    private static async Task<byte[]> CreateSignatureAsync(byte[] signedBytes, X509Certificate2 certificate, string digestOid,
        PdfSignatureOptions options, CancellationToken ct)
    {
        var cms = new SignedCms(new ContentInfo(new Oid(Data), signedBytes), detached: true);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate)
        {
            DigestAlgorithm = new Oid(digestOid),
            IncludeOption = X509IncludeOption.EndCertOnly,
        };
        if (options.ExtraCertificates is not null) signer.Certificates.AddRange(options.ExtraCertificates);
        // PAdES baseline: signing-certificate-v2 is required; the claimed time is the /M entry, never a signing-time attribute.
        signer.SignedAttributes.Add(Ess.SigningCertificateV2(certificate));
        cms.ComputeSignature(signer, silent: true);

        if (options.Timestamp is not null)
        {
            SignerInfo info = cms.SignerInfos[0];
            var request = Rfc3161TimestampRequest.CreateFromSignerInfo(info, HashAlgorithmName.SHA256,
                requestSignerCertificates: true, nonce: RandomNumberGenerator.GetBytes(8));
            byte[] response = await options.Timestamp.RequestAsync(request.Encode(), ct).ConfigureAwait(false);
            Rfc3161TimestampToken token;
            try { token = request.ProcessResponse(response, out _); }
            catch (CryptographicException e) { throw new InvalidOperationException("The time-stamping authority's response was refused: " + e.Message, e); }
            info.AddUnsignedAttribute(new AsnEncodedData(Ess.TimestampTokenOid, token.AsSignedCms().Encode()));
        }
        return cms.Encode();
    }

    // ---- the incremental update ------------------------------------------------------------

    private sealed class Update(byte[] file, long contentsStart, long contentsEnd, long byteRangeAt, int byteRangeLength)
    {
        private long[] Range => [0, contentsStart, contentsEnd, file.Length - contentsEnd];

        public byte[] SignedBytes()
        {
            WriteByteRange();
            var data = new byte[contentsStart + (file.Length - contentsEnd)];
            file.AsSpan(0, (int)contentsStart).CopyTo(data);
            file.AsSpan((int)contentsEnd).CopyTo(data.AsSpan((int)contentsStart));
            return data;
        }

        public byte[] Complete(byte[] cms)
        {
            // Hex digits go between the angle brackets; the rest of the reserved space stays zeros.
            int at = (int)contentsStart + 1;
            foreach (byte b in cms)
            {
                file[at++] = Hex(b >> 4);
                file[at++] = Hex(b & 15);
            }
            return file;
        }

        private static byte Hex(int v) => (byte)(v < 10 ? '0' + v : 'A' + v - 10);

        private void WriteByteRange()
        {
            string text = "[" + string.Join(' ', Range.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "]";
            if (text.Length > byteRangeLength) throw new InvalidOperationException("The byte range does not fit its placeholder.");
            Encoding.ASCII.GetBytes(text.PadRight(byteRangeLength)).CopyTo(file, byteRangeAt);
        }
    }

    private static Update BuildUpdate(PdfReader reader, byte[] original, PdfSignatureOptions options, X509Certificate2 certificate,
        DateTimeOffset signingTime, int reserved)
    {
        PdfDict trailer = reader.Trailer;
        int size = (trailer.Get("Size") as PdfNumber)?.IntValue ?? throw new InvalidDataException("The trailer has no /Size.");
        if (trailer.Get("Root") is not PdfRef rootRef) throw new InvalidDataException("The trailer has no /Root reference.");
        PdfDict catalog = reader.Catalog ?? throw new InvalidDataException("The document catalog is missing.");
        ReaderPage page = reader.Pages[options.PageIndex];

        string MapRef(PdfRef r) => $"{r.Number} {r.Generation} R";
        var changed = new SortedDictionary<int, (int Generation, byte[] Body)>();
        void Put(int id, int generation, PdfValue value)
        {
            var b = new ByteBuf(256);
            PdfSerializer.Write(b, value, MapRef);
            changed[id] = (generation, b.Span.ToArray());
        }

        int sigId = size, widgetId = size + 1;
        var widgetRef = new PdfOutputRef(widgetId);

        // The page lists the widget among its annotations.
        PdfValue? annots = page.Dict.Get("Annots");
        if (annots is PdfRef annotsRef && reader.Resolve(annotsRef) is PdfArray annotsArray)
            Put(annotsRef.Number, annotsRef.Generation, new PdfArray([.. annotsArray.Items, widgetRef]));
        else
        {
            var items = annots is PdfArray direct ? direct.Items : [];
            Put(page.Ref.Number, page.Ref.Generation, With(page.Dict, "Annots", new PdfArray([.. items, widgetRef])));
        }

        // The form lists the field and says the document holds signatures (SigFlags 3: signatures exist, append only).
        PdfValue? acroForm = catalog.Get("AcroForm");
        PdfDict form = reader.Resolve(acroForm) as PdfDict ?? new PdfDict([]);
        PdfValue? fields = form.Get("Fields");
        if (fields is PdfRef fieldsRef && reader.Resolve(fieldsRef) is PdfArray fieldsArray)
            Put(fieldsRef.Number, fieldsRef.Generation, new PdfArray([.. fieldsArray.Items, widgetRef]));
        else
            form = With(form, "Fields", new PdfArray([.. fields is PdfArray f ? f.Items : [], widgetRef]));
        form = With(form, "SigFlags", new PdfNumber("3"));
        if (acroForm is PdfRef formRef) Put(formRef.Number, formRef.Generation, form);
        else Put(rootRef.Number, rootRef.Generation, With(catalog, "AcroForm", form));

        var widget = new ByteBuf(256);
        widget.Ascii("<< /Type /Annot /Subtype /Widget /FT /Sig /T ");
        PdfSerializer.WriteString(widget, PdfSerializer.TextBytes(options.FieldName), hex: false);
        widget.Ascii(" /V ").Int(sigId).Ascii(" 0 R /Rect [0 0 0 0] /F 132 /P ").Ascii(MapRef(page.Ref)).Ascii(" >>");
        changed[widgetId] = (0, widget.Span.ToArray());

        // The signature dictionary, with placeholders for the byte range and the container.
        const int byteRangeLength = 60;
        var sig = new ByteBuf(reserved * 2 + 512);
        sig.Ascii("<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /ETSI.CAdES.detached /ByteRange ");
        int byteRangeOffset = sig.Length;
        sig.Ascii(new string(' ', byteRangeLength)).Ascii(" /Contents ");
        int contentsOffset = sig.Length;
        sig.Byte((byte)'<');
        for (int i = 0; i < reserved * 2; i++) sig.Byte((byte)'0');
        sig.Byte((byte)'>');
        sig.Ascii(" /M ");
        PdfSerializer.WriteString(sig, Encoding.ASCII.GetBytes(FormatDate(signingTime)), hex: false);
        string name = options.SignerName ?? certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        Text(sig, "Name", name);
        Text(sig, "Reason", options.Reason);
        Text(sig, "Location", options.Location);
        Text(sig, "ContactInfo", options.ContactInfo);
        sig.Ascii(" >>");
        changed[sigId] = (0, sig.Span.ToArray());

        // Append the objects, the cross-reference section and the trailer.
        using var output = new MemoryStream(original.Length + sig.Length + 4096);
        output.Write(original);
        if (original.Length > 0 && original[^1] != '\n') output.WriteByte((byte)'\n');
        var offsets = new SortedDictionary<int, (long Offset, int Generation)>();
        long sigBodyStart = 0;
        foreach (var (id, (generation, body)) in changed)
        {
            offsets[id] = (output.Position, generation);
            Ascii(output, $"{id} {generation} obj\n");
            if (id == sigId) sigBodyStart = output.Position;
            output.Write(body);
            Ascii(output, "\nendobj\n");
        }

        long previous = StartXref(original);
        bool streamXref = IsXrefStream(original, previous);
        int newSize = size + 2;
        string trailerEntries = $"/Root {MapRef(rootRef)}"
            + (trailer.Get("Info") is PdfRef info ? $" /Info {MapRef(info)}" : "")
            + IdEntry(trailer)
            + $" /Prev {previous}";
        long xrefOffset = output.Position;
        if (streamXref) WriteXrefStream(output, offsets, newSize, trailerEntries, xrefOffset);
        else
        {
            var b = new StringBuilder("xref\n");
            foreach (var run in Runs(offsets.Keys))
            {
                b.Append(CultureInfo.InvariantCulture, $"{run[0]} {run.Count}\n");
                foreach (int id in run) b.Append(CultureInfo.InvariantCulture, $"{offsets[id].Offset:D10} {offsets[id].Generation:D5} n \n");
            }
            b.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {newSize} {trailerEntries} >>\n");
            Ascii(output, b.ToString());
        }
        Ascii(output, $"startxref\n{xrefOffset}\n%%EOF\n");

        byte[] file = output.ToArray();
        long contentsStart = sigBodyStart + contentsOffset;
        long contentsEnd = contentsStart + reserved * 2 + 2;
        return new Update(file, contentsStart, contentsEnd, sigBodyStart + byteRangeOffset, byteRangeLength);
    }

    private static void WriteXrefStream(MemoryStream output, SortedDictionary<int, (long Offset, int Generation)> offsets, int size, string trailerEntries, long xrefOffset)
    {
        // The update of a file whose last section is a cross-reference stream is one as well. Its own entry is included.
        int xrefId = size;
        offsets[xrefId] = (xrefOffset, 0);
        var data = new MemoryStream();
        var index = new StringBuilder();
        foreach (var run in Runs(offsets.Keys))
        {
            index.Append(CultureInfo.InvariantCulture, $"{run[0]} {run.Count} ");
            foreach (int id in run)
            {
                long o = offsets[id].Offset;
                data.WriteByte(1);
                for (int s = 24; s >= 0; s -= 8) data.WriteByte((byte)(o >> s));
                data.WriteByte((byte)(offsets[id].Generation >> 8));
                data.WriteByte((byte)offsets[id].Generation);
            }
        }
        Ascii(output, $"{xrefId} 0 obj\n<< /Type /XRef /Size {size + 1} /Index [{index.ToString().TrimEnd()}] /W [1 4 2] {trailerEntries} /Length {data.Length} >>\nstream\n");
        output.Write(data.ToArray());
        Ascii(output, "\nendstream\nendobj\n");
    }

    private static IEnumerable<List<int>> Runs(IEnumerable<int> sortedIds)
    {
        var run = new List<int>();
        foreach (int id in sortedIds)
        {
            if (run.Count > 0 && id != run[^1] + 1) { yield return run; run = []; }
            run.Add(id);
        }
        if (run.Count > 0) yield return run;
    }

    private static string IdEntry(PdfDict trailer)
    {
        if (trailer.Get("ID") is PdfArray { Items: [PdfString first, PdfString second] })
            return $" /ID [<{Convert.ToHexString(first.Bytes)}> <{Convert.ToHexString(second.Bytes)}>]";
        string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return $" /ID [<{id}> <{id}>]";
    }

    internal static long StartXref(byte[] file)
    {
        int from = Math.Max(0, file.Length - 1024);
        int at = file.AsSpan(from).LastIndexOf("startxref"u8);
        if (at < 0) throw new InvalidDataException("The file has no startxref; it cannot take an incremental update.");
        int i = from + at + 9;
        while (i < file.Length && (file[i] is (byte)' ' or (byte)'\r' or (byte)'\n')) i++;
        long value = 0;
        while (i < file.Length && file[i] is >= (byte)'0' and <= (byte)'9') value = value * 10 + (file[i++] - '0');
        if (value <= 0 || value >= file.Length) throw new InvalidDataException("The startxref offset lies outside the file.");
        return value;
    }

    private static bool IsXrefStream(byte[] file, long offset) => !file.AsSpan((int)offset).StartsWith("xref"u8);

    private static PdfDict With(PdfDict dict, string key, PdfValue value)
    {
        var entries = dict.Entries.Where(e => e.Key != key).ToList();
        entries.Add(new(key, value));
        return new PdfDict(entries);
    }

    private static void Text(ByteBuf b, string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        b.Ascii(" /").Ascii(key).Sp();
        PdfSerializer.WriteString(b, PdfSerializer.TextBytes(value), hex: false);
    }

    private static void Ascii(Stream s, string text) => s.Write(Encoding.ASCII.GetBytes(text));

    internal static string FormatDate(DateTimeOffset t)
    {
        TimeSpan o = t.Offset;
        string zone = o == TimeSpan.Zero ? "Z" : $"{(o < TimeSpan.Zero ? '-' : '+')}{Math.Abs(o.Hours):00}'{Math.Abs(o.Minutes):00}'";
        return "D:" + t.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + zone;
    }
}
