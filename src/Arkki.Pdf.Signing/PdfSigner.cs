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
/// Signs a PDF as a PAdES baseline signature: B-B, or B-T when <see cref="PdfSignatureOptions.Timestamp"/> is set.
/// The key is a certificate's private key or an <see cref="IExternalSigner"/> (a smart card, an HSM, a signing
/// service). The signature is invisible, or drawn on the page with <see cref="PdfSignatureOptions.Appearance"/>; it
/// goes into a new field or into an empty signature field the document already has.
/// <para>Every method appends an incremental update, so the original bytes, and any signature already in the file,
/// stay intact. <see cref="AddValidationDataAsync"/> and <see cref="AddDocumentTimestampAsync"/> take a signed file
/// on to B-LT and B-LTA.</para>
/// <para>Whether a viewer shows the signature as trusted depends on the certificate: one from a CA on the Adobe
/// (AATL) or EU trusted lists shows green; a self-issued one is valid but untrusted.</para>
/// </summary>
public static partial class PdfSigner
{
    private const string Data = "1.2.840.113549.1.7.1";

    /// <summary>Signs without a timestamp. For a timestamped signature use <see cref="SignAsync(byte[], X509Certificate2, PdfSignatureOptions?, CancellationToken)"/>.</summary>
    /// <exception cref="ArgumentException">The certificate has no private key, the page does not exist or the field name is taken.</exception>
    /// <exception cref="NotSupportedException">The PDF is encrypted.</exception>
    public static byte[] Sign(byte[] pdf, X509Certificate2 certificate, PdfSignatureOptions? options = null)
    {
        options ??= new PdfSignatureOptions();
        if (options.Timestamp is not null) throw new InvalidOperationException("A timestamped signature calls the authority: use SignAsync.");
        return SignAsync(pdf, certificate, options).GetAwaiter().GetResult(); // completes synchronously without a timestamp
    }

    /// <summary>Signs, and timestamps the signature when <see cref="PdfSignatureOptions.Timestamp"/> is set.</summary>
    public static Task<byte[]> SignAsync(byte[] pdf, X509Certificate2 certificate, PdfSignatureOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(certificate);
        options ??= new PdfSignatureOptions();
        if (!certificate.HasPrivateKey) throw new ArgumentException("The certificate has no private key to sign with.", nameof(certificate));
        string digestOid = DigestOid(options);
        return SignCoreAsync(pdf, options, certificate, docTimestamp: false, EstimateSize(certificate, options),
            signedBytes => CreateSignatureAsync(signedBytes, certificate, digestOid, options, cancellationToken));
    }

    /// <summary>
    /// Signs with a key held outside the process: Arkki prepares the signature and <paramref name="signer"/> signs its
    /// hash. The signature is timestamped when <see cref="PdfSignatureOptions.Timestamp"/> is set.
    /// </summary>
    /// <exception cref="InvalidOperationException">The value the signer returned is not its certificate's signature of the hash.</exception>
    public static Task<byte[]> SignAsync(byte[] pdf, IExternalSigner signer, PdfSignatureOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(signer);
        options ??= new PdfSignatureOptions();
        X509Certificate2 certificate = signer.Certificate ?? throw new ArgumentException("The external signer has no certificate.", nameof(signer));
        string digestOid = DigestOid(options);
        return SignCoreAsync(pdf, options, certificate, docTimestamp: false, EstimateSize(certificate, options),
            signedBytes => CreateExternalSignatureAsync(signedBytes, signer, certificate, digestOid, options, cancellationToken));
    }

    private static string DigestOid(PdfSignatureOptions options) => options.HashAlgorithm.Name switch
    {
        "SHA256" => "2.16.840.1.101.3.4.2.1",
        "SHA384" => "2.16.840.1.101.3.4.2.2",
        "SHA512" => "2.16.840.1.101.3.4.2.3",
        _ => throw new ArgumentException("The digest must be SHA-256, SHA-384 or SHA-512.", nameof(options)),
    };

    /// <summary>Places the signature dictionary, has the container made over the signed bytes, and writes it in.</summary>
    private static async Task<byte[]> SignCoreAsync(byte[] pdf, PdfSignatureOptions options, X509Certificate2? certificate, bool docTimestamp,
        int estimate, Func<byte[], Task<byte[]>> createContainer)
    {
        if (string.IsNullOrWhiteSpace(options.FieldName)) throw new ArgumentException("The signature field needs a name.", nameof(options));
        if (options.ReservedSize < 0) throw new ArgumentException("The reserved size cannot be negative.", nameof(options));
        if (!Enum.IsDefined(options.Certification)) throw new ArgumentException("Unknown certification level.", nameof(options));

        PdfReader reader;
        try { reader = PdfReader.Open(pdf); }
        catch (PdfPasswordException e) { throw new NotSupportedException("Signing an encrypted PDF is not supported.", e); }
        if (reader.IsEncrypted) throw new NotSupportedException("Signing an encrypted PDF is not supported.");
        if ((uint)options.PageIndex >= (uint)reader.PageCount) throw new ArgumentException($"The document has no page {options.PageIndex}.", nameof(options));

        DateTimeOffset signingTime = options.SigningTime ?? DateTimeOffset.Now;
        int reserved = options.ReservedSize > 0 ? options.ReservedSize : estimate;
        for (int attempt = 0; ; attempt++)
        {
            Placeholder placed = Place(reader, pdf, options, certificate, signingTime, reserved, docTimestamp);
            byte[] container = await createContainer(placed.SignedBytes()).ConfigureAwait(false);
            if (container.Length <= reserved) return placed.Complete(container);
            if (options.ReservedSize > 0 || attempt > 0)
                throw new InvalidOperationException($"The signature needs {container.Length} bytes but {reserved} were reserved; raise ReservedSize.");
            reserved = container.Length + 2048;
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
            byte[] token = await RequestTimestampAsync(options.Timestamp, SHA256.HashData(info.GetSignature()), ct).ConfigureAwait(false);
            info.AddUnsignedAttribute(new AsnEncodedData(Ess.TimestampTokenOid, token));
        }
        return cms.Encode();
    }

    private static async Task<byte[]> CreateExternalSignatureAsync(byte[] signedBytes, IExternalSigner signer, X509Certificate2 certificate,
        string digestOid, PdfSignatureOptions options, CancellationToken ct)
    {
        HashAlgorithmName hash = options.HashAlgorithm;
        byte[] attributes = CmsBuilder.SignedAttributes(Hash(hash, signedBytes), certificate);
        byte[] toSign = Hash(hash, attributes);
        byte[] raw = await signer.SignHashAsync(toSign, hash, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The external signer returned no signature.");
        var (algorithm, signature) = CmsBuilder.Normalize(certificate, hash, raw);
        // A wrong key, padding or format would make a file every viewer rejects: catch it here, where it can be explained.
        if (!CmsBuilder.Verify(certificate, hash, toSign, algorithm, signature))
            throw new InvalidOperationException("The external signer's value is not its certificate's signature of the hash. "
                + "RSA keys must sign with PKCS #1 v1.5 padding; check that the key matches the certificate.");
        byte[]? token = options.Timestamp is null ? null
            : await RequestTimestampAsync(options.Timestamp, SHA256.HashData(signature), ct).ConfigureAwait(false);
        return CmsBuilder.Build(certificate, options.ExtraCertificates, digestOid, attributes, algorithm, signature, token);
    }

    private static byte[] Hash(HashAlgorithmName algorithm, byte[] data) => algorithm.Name switch
    {
        "SHA384" => SHA384.HashData(data),
        "SHA512" => SHA512.HashData(data),
        _ => SHA256.HashData(data),
    };

    /// <summary>Asks the authority for an RFC 3161 timestamp of a SHA-256 hash and returns the token (a CMS container).</summary>
    private static async Task<byte[]> RequestTimestampAsync(ITimestampClient authority, byte[] sha256, CancellationToken ct)
    {
        var request = Rfc3161TimestampRequest.CreateFromHash(sha256, HashAlgorithmName.SHA256,
            requestSignerCertificates: true, nonce: RandomNumberGenerator.GetBytes(8));
        byte[] response = await authority.RequestAsync(request.Encode(), ct).ConfigureAwait(false);
        try { return request.ProcessResponse(response, out _).AsSignedCms().Encode(); }
        catch (CryptographicException e) { throw new InvalidOperationException("The time-stamping authority's response was refused: " + e.Message, e); }
    }

    // ---- the incremental update ------------------------------------------------------------

    /// <summary>A file with a signature dictionary whose byte range and container are still placeholders.</summary>
    private sealed class Placeholder(byte[] file, long contentsStart, long contentsEnd, long byteRangeAt, int byteRangeLength)
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

        public byte[] Complete(byte[] container)
        {
            // Hex digits go between the angle brackets; the rest of the reserved space stays zeros.
            int at = (int)contentsStart + 1;
            foreach (byte b in container)
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

    private static Placeholder Place(PdfReader reader, byte[] original, PdfSignatureOptions options, X509Certificate2? certificate,
        DateTimeOffset signingTime, int reserved, bool docTimestamp)
    {
        var update = new IncrementalUpdate(reader, original);
        int sigId = update.Reserve();
        var sigRef = new PdfOutputRef(sigId);
        string signerName = options.SignerName ?? certificate?.GetNameInfo(X509NameType.SimpleName, forIssuer: false) ?? "";

        IReadOnlyList<PdfFormField> fields = reader.GetFormFields();
        bool signedBefore = fields.Any(f => f.Kind == PdfFieldKind.Signature && reader.Resolve((reader.GetObject(f.ObjectNumber) as PdfDict)?.Get("V")) is PdfDict);
        if (options.Certification != PdfCertificationLevel.None && signedBefore)
            throw new InvalidOperationException("A certification signature must be the document's first signature, and this document is already signed.");

        int Appearance(PdfSignatureAppearance look, double width, double height) =>
            AppearanceBuilder.Build(update, look, width, height, DefaultText(signerName, signingTime, options));

        PdfFormField? existing = fields.FirstOrDefault(f => f.Name == options.FieldName);
        if (existing is not null)
        {
            // Sign the empty signature field the document already has.
            if (existing.Kind != PdfFieldKind.Signature || reader.GetObject(existing.ObjectNumber) is not PdfDict field)
                throw new ArgumentException($"The document already has a field named '{options.FieldName}'.", nameof(options));
            if (reader.Resolve(field.Get("V")) is not (null or PdfNull))
                throw new ArgumentException($"The signature field '{options.FieldName}' is already signed.", nameof(options));
            int widgetId = existing.Widgets.Count > 0 ? existing.Widgets[0] : existing.ObjectNumber;
            PdfDict widget = widgetId == existing.ObjectNumber ? field : reader.GetObject(widgetId) as PdfDict ?? field;
            double[] rect = reader.ReadRect(widget.Get("Rect")) ?? [0, 0, 0, 0];
            double width = Math.Abs(rect[2] - rect[0]), height = Math.Abs(rect[3] - rect[1]);

            // A field with a box is drawn in, with the default look unless another is given; one without stays invisible.
            // The default look keeps the frame of a field that had one.
            bool framed = reader.Resolve(widget.Get("MK")) is PdfDict mk && reader.Resolve(mk.Get("BC")) is PdfArray { Items.Count: > 0 };
            PdfSignatureAppearance? look = options.Appearance
                ?? (width > 0 && height > 0 && !docTimestamp ? new PdfSignatureAppearance { Border = framed } : null);
            PdfDict changed = widget;
            if (look is { Width: > 0, Height: > 0 })
            {
                ReaderPage page = PageOf(reader, widget, widgetId) ?? reader.Pages[options.PageIndex];
                (width, height) = (look.Width, look.Height);
                changed = IncrementalUpdate.With(changed, "Rect", RectArray(page, look));
            }
            if (look is not null)
            {
                if (width <= 0 || height <= 0) throw new ArgumentException($"The signature field '{options.FieldName}' has no box: give the appearance a Width and a Height.", nameof(options));
                changed = IncrementalUpdate.With(changed, "AP", new PdfDict([new("N", new PdfOutputRef(Appearance(look, width, height)))]));
            }
            changed = IncrementalUpdate.With(changed, "F", new PdfNumber("132")); // print, locked
            if (widgetId == existing.ObjectNumber) update.Put(widgetId, 0, IncrementalUpdate.With(changed, "V", sigRef));
            else
            {
                update.Put(existing.ObjectNumber, 0, IncrementalUpdate.With(field, "V", sigRef));
                update.Put(widgetId, 0, changed);
            }
            SetForm(update, null);
        }
        else
        {
            if (options.FieldName.Contains('.')) throw new ArgumentException("A new signature field's name cannot contain '.'.", nameof(options));
            ReaderPage page = reader.Pages[options.PageIndex];
            PdfSignatureAppearance? look = docTimestamp ? null : options.Appearance;
            if (look is not null && (look.Width <= 0 || look.Height <= 0))
                throw new ArgumentException("A visible signature needs a box: give the appearance a Width and a Height.", nameof(options));

            int widgetId = update.Reserve();
            var widgetRef = new PdfOutputRef(widgetId);
            var widget = new ByteBuf(256);
            widget.Ascii("<< /Type /Annot /Subtype /Widget /FT /Sig /T ");
            PdfSerializer.WriteString(widget, PdfSerializer.TextBytes(options.FieldName), hex: false);
            widget.Ascii(" /V ").Int(sigId).Ascii(" 0 R /Rect ");
            if (look is null) widget.Ascii("[0 0 0 0]");
            else
            {
                PdfSerializer.Write(widget, RectArray(page, look), IncrementalUpdate.MapRef);
                widget.Ascii(" /AP << /N ").Int(Appearance(look, look.Width, look.Height)).Ascii(" 0 R >>");
            }
            widget.Ascii(" /F 132 /P ").Ascii(IncrementalUpdate.MapRef(page.Ref)).Ascii(" >>");
            update.Put(widgetId, widget.Span);

            // The page lists the widget among its annotations.
            PdfValue? annots = page.Dict.Get("Annots");
            if (annots is PdfRef annotsRef && reader.Resolve(annotsRef) is PdfArray annotsArray)
                update.Put(annotsRef.Number, annotsRef.Generation, new PdfArray([.. annotsArray.Items, widgetRef]));
            else
            {
                var items = annots is PdfArray direct ? direct.Items : [];
                update.Put(page.Ref.Number, page.Ref.Generation, IncrementalUpdate.With(page.Dict, "Annots", new PdfArray([.. items, widgetRef])));
            }
            SetForm(update, widgetRef);
        }

        if (options.Certification != PdfCertificationLevel.None)
            update.SetCatalog(IncrementalUpdate.With(update.Catalog, "Perms", new PdfDict([new("DocMDP", sigRef)])));

        // The signature dictionary, with placeholders for the byte range and the container.
        const int byteRangeLength = 60;
        var sig = new ByteBuf(reserved * 2 + 512);
        sig.Ascii(docTimestamp
            ? "<< /Type /DocTimeStamp /Filter /Adobe.PPKLite /SubFilter /ETSI.RFC3161 /ByteRange "
            : "<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /ETSI.CAdES.detached /ByteRange ");
        int byteRangeOffset = sig.Length;
        sig.Ascii(new string(' ', byteRangeLength)).Ascii(" /Contents ");
        int contentsOffset = sig.Length;
        sig.Byte((byte)'<');
        for (int i = 0; i < reserved * 2; i++) sig.Byte((byte)'0');
        sig.Byte((byte)'>');
        if (!docTimestamp)
        {
            sig.Ascii(" /M ");
            PdfSerializer.WriteString(sig, Encoding.ASCII.GetBytes(FormatDate(signingTime)), hex: false);
            Text(sig, "Name", signerName);
            Text(sig, "Reason", options.Reason);
            Text(sig, "Location", options.Location);
            Text(sig, "ContactInfo", options.ContactInfo);
            if (options.Certification != PdfCertificationLevel.None)
                sig.Ascii(" /Reference [<< /Type /SigRef /TransformMethod /DocMDP /TransformParams << /Type /TransformParams /P ")
                    .Int((int)options.Certification).Ascii(" /V /1.2 >> >>]");
        }
        sig.Ascii(" >>");
        update.Put(sigId, sig.Span);

        byte[] file = update.Write(out var bodyStart);
        long contentsStart = bodyStart[sigId] + contentsOffset;
        long contentsEnd = contentsStart + reserved * 2 + 2;
        return new Placeholder(file, contentsStart, contentsEnd, bodyStart[sigId] + byteRangeOffset, byteRangeLength);
    }

    /// <summary>
    /// The form says the document holds signatures (SigFlags 3: signatures exist, append only) and lists
    /// <paramref name="newField"/> when the signature brought its own field.
    /// </summary>
    private static void SetForm(IncrementalUpdate update, PdfOutputRef? newField)
    {
        PdfReader reader = update.Reader;
        PdfValue? acroForm = update.Catalog.Get("AcroForm");
        PdfDict form = reader.Resolve(acroForm) as PdfDict ?? new PdfDict([]);
        if (newField is not null)
        {
            PdfValue? fields = form.Get("Fields");
            if (fields is PdfRef fieldsRef && reader.Resolve(fieldsRef) is PdfArray fieldsArray)
                update.Put(fieldsRef.Number, fieldsRef.Generation, new PdfArray([.. fieldsArray.Items, newField]));
            else
                form = IncrementalUpdate.With(form, "Fields", new PdfArray([.. fields is PdfArray f ? f.Items : [], newField]));
        }
        form = IncrementalUpdate.With(form, "SigFlags", new PdfNumber("3"));
        if (acroForm is PdfRef formRef) update.Put(formRef.Number, formRef.Generation, form);
        else update.SetCatalog(IncrementalUpdate.With(update.Catalog, "AcroForm", form));
    }

    /// <summary>The page a widget lies on: the one its /P names, or the one whose annotations list it.</summary>
    private static ReaderPage? PageOf(PdfReader reader, PdfDict widget, int widgetId)
    {
        if (widget.Get("P") is PdfRef p && reader.Pages.FirstOrDefault(x => x.Ref.Number == p.Number) is { } named) return named;
        return reader.Pages.FirstOrDefault(x => reader.Resolve(x.Dict.Get("Annots")) is PdfArray a && a.Items.Any(i => i is PdfRef r && r.Number == widgetId));
    }

    /// <summary>The appearance's box, given from the page's top-left corner, as a PDF rectangle.</summary>
    private static PdfArray RectArray(ReaderPage page, PdfSignatureAppearance look)
    {
        double left = Math.Min(page.Box[0], page.Box[2]) + look.X, top = Math.Max(page.Box[1], page.Box[3]) - look.Y;
        static PdfValue N(double v) => new PdfNumber(v.ToString("0.###", CultureInfo.InvariantCulture));
        return new PdfArray([N(left), N(top - look.Height), N(left + look.Width), N(top)]);
    }

    private static string DefaultText(string signerName, DateTimeOffset time, PdfSignatureOptions options)
    {
        var text = new StringBuilder("Digitally signed by ").Append(signerName);
        text.Append("\nDate: ").Append(time.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(options.Reason)) text.Append("\nReason: ").Append(options.Reason);
        if (!string.IsNullOrEmpty(options.Location)) text.Append("\nLocation: ").Append(options.Location);
        return text.ToString();
    }

    private static void Text(ByteBuf b, string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        b.Ascii(" /").Ascii(key).Sp();
        PdfSerializer.WriteString(b, PdfSerializer.TextBytes(value), hex: false);
    }

    internal static string FormatDate(DateTimeOffset t)
    {
        TimeSpan o = t.Offset;
        string zone = o == TimeSpan.Zero ? "Z" : $"{(o < TimeSpan.Zero ? '-' : '+')}{Math.Abs(o.Hours):00}'{Math.Abs(o.Minutes):00}'";
        return "D:" + t.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + zone;
    }
}
