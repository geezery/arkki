# Encryption and signing

## Encrypt a document

Set `Encryption` before saving:

```csharp
var doc = new PdfDocument();
doc.AddPage();
doc.Encryption = new PdfEncryption(userPassword: "open", ownerPassword: "owner",
    permissions: PdfPermissions.Print | PdfPermissions.Copy);
byte[] bytes = doc.ToArray();

var reader = PdfReader.Open(bytes, password: "open");
```

- The **user password** opens the document. The **owner password** opens it
  with every permission.
- An empty user password lets anyone open the file but keeps the permissions,
  which viewers honour.
- The permissions are `Print`, `Modify`, `Copy`, `Annotate`, `FillForms`,
  `ExtractForAccessibility`, `Assemble`, `PrintHighQuality`, and `All` or
  `None`.

Arkki writes AES-256 (PDF 2.0, revision 6) and reads every standard security
handler revision: RC4 40/128-bit, AES-128 and AES-256.

## Open an encrypted file

Both `PdfReader.Open` and `PdfDocument.Open` take the user or owner password:

```csharp
try
{
    PdfDocument doc = PdfDocument.Open(pdf, password);
    Console.WriteLine(doc.Pages.Count);
}
catch (PdfPasswordException)
{
    Console.WriteLine("That password does not open the file.");
}
```

A reader tells you what it opened:

```csharp
PdfReader reader = PdfReader.Open(pdf, password);
Console.WriteLine($"encrypted {reader.IsEncrypted}, may print {reader.Permissions.HasFlag(PdfPermissions.Print)}");
```

## Sign a document

Signing is in the `Arkki.Pdf.Signing` package. It writes PAdES signatures and
depends only on Microsoft's `System.Security.Cryptography.Pkcs`.

```csharp
X509Certificate2 cert = X509CertificateLoader.LoadPkcs12FromFile("signer.p12", password);
byte[] signed = PdfSigner.Sign(pdf, cert, new PdfSignatureOptions { Reason = "Approved", Location = "Helsinki" });
```

Every signing method appends an incremental update, so the original bytes, and
any signature already in the file, stay intact. RSA and ECDSA keys work, with
SHA-256, SHA-384 or SHA-512. Without an appearance the signature is invisible:
it is in the file and in the viewer's signature panel, but not on a page.

An encrypted PDF cannot be signed: `PdfSigner` throws `NotSupportedException`.

## Visible signatures

Set `Appearance` to draw the signature on its page. The box is given in points
from the page's top-left corner, like everything on the canvas:

```csharp
byte[] signed = PdfSigner.Sign(pdf, cert, new PdfSignatureOptions
{
    Reason = "Approved",
    Location = "Helsinki",
    PageIndex = 0,
    Appearance = new PdfSignatureAppearance { X = 50, Y = 700, Width = 220, Height = 60, Border = true },
});
```

With nothing else set, the box holds a few lines of text: "Digitally signed by"
and the signer's name, the date, and the reason and location when the options
give them.

| `PdfSignatureAppearance` | Meaning |
|---|---|
| `X`, `Y` | The box's left and top edges, in points from the page's left and top edges |
| `Width`, `Height` | The box's size in points. A new signature field needs both. |
| `Text` | Your own text; lines are separated by `\n` and long lines wrap. `null` writes the default description, an empty string writes no text. |
| `Font` | A TrueType or OpenType font file for the text, embedded in the signature. Without it the text is in Helvetica, which covers Western European characters only. |
| `FontSize` | The text size in points; 0 picks the largest size up to 12 at which the text fits the box |
| `Image` | A PNG or JPEG picture (a scanned signature, a logo, a stamp), scaled to fit: the left part of the box when there is text, the whole box when `Text` is empty. A PNG's transparency is kept. |
| `Border` | A thin grey frame around the box |
| `Draw` | Your own drawing, on top of the image and text |

A picture, your own text and a font for characters Helvetica does not have:

```csharp
byte[] signed = PdfSigner.Sign(pdf, cert, new PdfSignatureOptions
{
    Appearance = new PdfSignatureAppearance
    {
        X = 300, Y = 100, Width = 240, Height = 80,
        Image = File.ReadAllBytes("signature.png"),
        Font = File.ReadAllBytes("Inter-Regular.ttf"),
        Text = "Allekirjoittanut Žofia Čapek\nPäiväys 24.11.2026",
    },
});
```

`Draw` hands you a canvas the size of the box, with its origin at the box's
top-left corner. Add the fonts and images you draw with to the document passed
in. Set `Text` to `""` to draw everything yourself:

```csharp
var look = new PdfSignatureAppearance
{
    X = 50, Y = 700, Width = 200, Height = 50,
    Text = "",
    Draw = (doc, canvas) =>
    {
        PdfFont font = doc.AddFont(File.ReadAllBytes("Inter-Regular.ttf"));
        canvas.Text(font, 11, 4, 20, "Approved by the board");
        canvas.Line(0, 49, 200, 49);
    },
};
byte[] signed = PdfSigner.Sign(pdf, cert, new PdfSignatureOptions { Appearance = look });
```

The picture is only that. What proves the signature is its cryptographic value,
which every viewer checks separately; a signature without an appearance is just
as valid, and a box that looks like a signature proves nothing by itself.

## Sign an existing signature field

A document can mark where signatures go. Add empty signature fields when you
create it:

```csharp
var doc = new PdfDocument();
PdfPage page = doc.AddPage();
page.AddSignatureField("approval", x: 60, y: 700, width: 200, height: 60);
byte[] form = doc.ToArray();
```

Then sign the field by its name. This also works for an empty signature field
another program made:

```csharp
byte[] signed = PdfSigner.Sign(pdf, cert, new PdfSignatureOptions { FieldName = "approval" });
```

- No field is added; the existing one is signed and stays on its own page.
- A field that has a box is drawn in. With no `Appearance` it gets the default
  text, and keeps its frame if it had one. With an `Appearance` whose `Width`
  and `Height` are 0, your appearance is drawn in the field's own box.
- An `Appearance` with a `Width` and a `Height` replaces the field's box, which
  moves the field.
- A field that is already signed, or a field of that name that is not a
  signature field, is refused with `ArgumentException`.

When the document has no field of that name, a new one is added on `PageIndex`.
A new field's name cannot contain a dot.

## Certification signatures

A certification signature is the author's signature that also says which later
changes keep the document valid:

```csharp
byte[] certified = PdfSigner.Sign(pdf, cert, new PdfSignatureOptions
{
    Certification = PdfCertificationLevel.FormFilling,
});
```

| `PdfCertificationLevel` | Allowed afterwards |
|---|---|
| `None` | Not a certification: an ordinary approval signature (the default) |
| `NoChanges` | No changes at all |
| `FormFilling` | Filling in form fields and signing |
| `FormFillingAndAnnotations` | Filling in form fields, signing, and adding, changing and removing annotations (comments) |

It must be the document's first signature; on a document that is already signed
`PdfSigner` throws `InvalidOperationException`. Approval signatures can follow
it.

## Keys held elsewhere

When the private key is on a smart card or in a hardware security module, a
cloud key vault or a remote signing service, implement `IExternalSigner`. Arkki
builds the signature and asks only for the one operation that needs the key:

```csharp
sealed class VaultSigner(X509Certificate2 certificate, System.Security.Cryptography.RSA key) : IExternalSigner
{
    // Only the public part is used; the certificate needs no private key.
    public X509Certificate2 Certificate { get; } = certificate;

    public Task<byte[]> SignHashAsync(byte[] hash, System.Security.Cryptography.HashAlgorithmName hashAlgorithm,
        CancellationToken cancellationToken = default)
    {
        // Here the key is local; a real implementation sends the hash to the device or service.
        return Task.FromResult(key.SignHash(hash, hashAlgorithm, System.Security.Cryptography.RSASignaturePadding.Pkcs1));
    }
}
```

```csharp
byte[] signed = await PdfSigner.SignAsync(pdf, signer, new PdfSignatureOptions { Reason = "Approved" });
```

- An RSA key returns a PKCS #1 v1.5 signature, as `RSA.SignHash` gives with
  `RSASignaturePadding.Pkcs1`.
- An elliptic-curve key returns an ECDSA signature, either as the two numbers
  r and s side by side (IEEE P1363, what `ECDsa.SignHash` and most key vaults
  return) or as a DER sequence.
- The hash arrives already computed, with the algorithm of
  `PdfSignatureOptions.HashAlgorithm`.
- The value that comes back is checked against the certificate before the file
  is written. A wrong key, padding or format throws
  `InvalidOperationException` instead of giving a file every viewer rejects.

Timestamps and visible appearances work the same way as with a local key.

## Timestamps (PAdES B-T)

A timestamp from an RFC 3161 authority proves when the document was signed:

```csharp
byte[] stamped = await PdfSigner.SignAsync(pdf, cert, new PdfSignatureOptions
{
    Timestamp = new HttpTimestampClient(httpClient, new Uri("https://tsa.example.com")),
});
```

`HttpTimestampClient` talks to an authority over HTTP. To reach one another way,
implement `ITimestampClient`. A timestamped signature calls the authority, so
it needs `SignAsync`; `Sign` refuses it.

## Long-term signatures (PAdES B-LT and B-LTA)

A signature is validated with its certificates and with what their issuers say
about revocation. Those servers do not last as long as a contract does, so a
long-term signature carries the answers in the file.

**B-LT.** Sign with a timestamp first, then add the validation data:

```csharp
var revocation = new HttpRevocationClient(httpClient);
byte[] longTerm = await PdfSigner.AddValidationDataAsync(stamped, revocation, issuers);
```

- For every certificate the signatures and their timestamps rely on, Arkki
  fetches an OCSP response or, failing that, a revocation list, and stores them
  with the certificates in the file's document security store.
- `issuers` are the issuer certificates the signatures do not embed:
  intermediates and roots. Every certificate's issuer must be among the
  embedded ones or these.
- `HttpRevocationClient` reads the addresses from the certificates themselves
  (their authority information access and CRL distribution point extensions).
  To reach the issuers another way (a cache, a proxy, files), implement
  `IRevocationClient`.
- Data the file already has is not fetched again.
- It throws `InvalidOperationException` when the file has no signature, an
  issuer certificate is missing, or neither an OCSP response nor a revocation
  list signed by the issuer could be had for a certificate.

**B-LTA.** A document timestamp covers the whole file as it is now, signatures
and validation data included:

```csharp
var authority = new HttpTimestampClient(httpClient, new Uri("https://tsa.example.com"));
byte[] archived = await PdfSigner.AddDocumentTimestampAsync(longTerm, authority);
```

The timestamp goes into an invisible field named `Timestamp1`, `Timestamp2`
and so on, or the name you give. Adding another before the timestamp
authority's certificate expires keeps the file valid for as long as that is
repeated.

## Options

| `PdfSignatureOptions` | Meaning |
|---|---|
| `FieldName` | The signature field's name, `Signature1` by default. An empty signature field of that name is signed; otherwise a new field is added, and the name must be free, so give a second signature another name. |
| `Appearance` | Draws the signature on the page. Without it a new field is invisible, and an existing field that has a box gets the default appearance. |
| `Certification` | Makes this a certification signature |
| `Reason`, `Location`, `ContactInfo` | Shown by viewers |
| `SignerName` | The signer's name; the certificate's common name when not set |
| `SigningTime` | The claimed signing time; now when not set |
| `PageIndex` | The page a new signature field is placed on, zero-based. An existing field stays on its own page. |
| `HashAlgorithm` | SHA-256 (default), SHA-384 or SHA-512 |
| `ExtraCertificates` | Intermediate certificates to embed, so a verifier can build the chain |
| `Timestamp` | An RFC 3161 client; setting it makes the signature B-T |
| `ReservedSize` | Bytes reserved for the signature container; 0 picks a size |

## Verify signatures

```csharp
foreach (PdfSignatureInfo s in PdfSignatureVerifier.Verify(pdf, trustedRoots))
    Console.WriteLine($"{s.FieldName}: intact {s.IsIntact}, whole {s.CoversWholeDocument}, {s.Level}, trusted {s.ChainTrusted}, revocation {s.Revocation}");
```

`Verify` works with the file's own data only; nothing is fetched. Document
timestamps are listed with the signatures.

| `PdfSignatureInfo` | Meaning |
|---|---|
| `IsIntact` | The signed bytes have not changed and the signature value matches the embedded certificate |
| `CoversWholeDocument` | Nothing was added to the file after this signature (later signatures and validation data are added after it) |
| `Level` | `BaselineB`, `BaselineT`, `BaselineLT`, `BaselineLTA`, or `None` when it is not a PAdES signature, is a document timestamp, or its value is broken |
| `HasTimestamp`, `TimestampValid`, `TimestampTime` | The signature timestamp; for a document timestamp, the time it proves the file existed at |
| `IsDocumentTimestamp` | Not anyone's signature, but an authority's proof of when the file existed |
| `ChainTrusted` | Whether the certificate chains to one of the roots you gave; `null` when you gave none |
| `Revocation` | What revocation information says about the certificates the signature relies on; see below |
| `IsVisible` | The signature has a box on a page |
| `Certification` | For a certification signature, the changes it allows afterwards |
| `Certificate`, `SignerName`, `Reason`, `Location`, `ClaimedSigningTime` | What the signature says about itself; for a document timestamp `Certificate` is the authority's |
| `Problem` | Why the signature is not intact, not trusted or revoked, when it is |

The levels above B-T:

- `BaselineLT`: B-T plus validation data in the file. The chains of the signer
  and of the timestamp authority reach a root, and the file's own revocation
  lists and OCSP responses say every certificate on them was good.
- `BaselineLTA`: B-LT plus a valid document timestamp added after the
  signature.

### Revocation

| `PdfRevocationStatus` | Meaning |
|---|---|
| `NotChecked` | There was nothing to check with: the file carries no revocation information and none was fetched |
| `Good` | Every certificate below the root was good when the signature was made |
| `Revoked` | A certificate had been revoked when the signature was made; `Problem` names it |
| `Unknown` | For some certificate no answer was found, or the chain does not reach a root |

To fetch what the file does not carry, use `VerifyAsync` with a revocation
client:

```csharp
var options = new PdfVerificationOptions
{
    TrustedRoots = trustedRoots,
    Revocation = new HttpRevocationClient(httpClient),
};
foreach (PdfSignatureInfo s in await PdfSignatureVerifier.VerifyAsync(pdf, options))
    Console.WriteLine($"{s.FieldName}: {s.Revocation} {s.Problem}");
```

`PdfVerificationOptions.Certificates` takes issuer certificates the file does
not embed. Fetched answers decide `Revocation`, but not the level: only data in
the file makes a signature long-term.

A certificate revoked *after* a time that a timestamp proves was still good
when it signed, so the signature counts as good. Without a timestamp the
claimed signing time proves nothing, so a later revocation counts.

## What "trusted" means in a viewer

A viewer shows a signature as trusted when the certificate comes from a CA it
trusts (for Adobe, the AATL or EU trusted lists). A certificate you issued
yourself gives a valid but untrusted signature.

## Not included

- RSA-PSS signatures from an external signer (it signs with PKCS #1 v1.5)
- Signature policies (PAdES-EPES) and checks of certificate policies
- Judging how fresh revocation information is
- VRI dictionaries in the document security store
- Locking fields with a signature (FieldMDP)
- Signing encrypted PDFs
- Checking that changes made after a certification stay within what it allows
