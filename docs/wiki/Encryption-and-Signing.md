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

The signature is an invisible field appended as an incremental update, so
earlier signatures stay valid. RSA and ECDSA keys work, with SHA-256, SHA-384
or SHA-512.

### With a timestamp (PAdES B-T)

A timestamp from an RFC 3161 authority proves when the document was signed:

```csharp
byte[] stamped = await PdfSigner.SignAsync(pdf, cert, new PdfSignatureOptions
{
    Timestamp = new HttpTimestampClient(httpClient, new Uri("https://tsa.example.com")),
});
```

`HttpTimestampClient` talks to an authority over HTTP. To reach one another way,
implement `ITimestampClient`.

### Options

| `PdfSignatureOptions` | Meaning |
|---|---|
| `FieldName` | The signature field's name, `Signature1` by default. It must not already exist in the document, so give the second signature another name. |
| `Reason`, `Location`, `ContactInfo` | Shown by viewers |
| `SignerName` | The signer's name; the certificate's common name when not set |
| `SigningTime` | The claimed signing time; now when not set |
| `PageIndex` | The page the invisible field is placed on, zero-based |
| `HashAlgorithm` | SHA-256 (default), SHA-384 or SHA-512 |
| `ExtraCertificates` | Intermediate certificates to embed, so a verifier can build the chain |
| `Timestamp` | An RFC 3161 client; setting it makes the signature B-T |

## Verify signatures

```csharp
foreach (PdfSignatureInfo s in PdfSignatureVerifier.Verify(pdf, trustedRoots))
    Console.WriteLine($"{s.FieldName}: intact {s.IsIntact}, whole {s.CoversWholeDocument}, {s.Level}, trusted {s.ChainTrusted}");
```

| `PdfSignatureInfo` | Meaning |
|---|---|
| `IsIntact` | The signed bytes have not changed |
| `CoversWholeDocument` | Nothing was added to the file after this signature |
| `Level` | `BaselineB`, `BaselineT`, or `None` when it is not a PAdES signature or its value is broken |
| `HasTimestamp`, `TimestampValid`, `TimestampTime` | The signature timestamp |
| `ChainTrusted` | Whether the certificate chains to one of the roots you gave |
| `Certificate`, `SignerName`, `Reason`, `Location`, `ClaimedSigningTime` | What the signature says about itself |
| `Problem` | What was wrong, when something was |

## What "trusted" means in a viewer

A viewer shows a signature as trusted when the certificate comes from a CA it
trusts (for Adobe, the AATL or EU trusted lists). A certificate you issued
yourself gives a valid but untrusted signature.

## Not included

Visible signature appearances, long-term validation data (B-LT/B-LTA),
revocation checking, and signing with an external device or service.
