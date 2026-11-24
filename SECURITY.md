# Security

Arkki reads files that come from anywhere and writes files that people sign and trust, so here is
exactly what it does, what it does not do, and how to check both.

## The short version

- **Managed code only.** No native binaries, no `unsafe` code, no P/Invoke. A hostile PDF cannot reach
  memory it should not: the worst it gets is an exception.
- **No files, no network, no processes.** The library works on the bytes and streams you hand it.
  `Arkki.Pdf` never opens a file, a socket or a process by itself, and it never looks for system fonts.
  The only network calls in the project are in `Arkki.Pdf.Signing`, and only when you ask for them:
  `HttpTimestampClient` posts a timestamp request to the authority *you* give it, and `HttpRevocationClient`
  asks the OCSP responders and revocation list addresses named in the certificates being checked. Both use the
  `HttpClient` *you* give them, so timeouts, proxies and which hosts may be reached stay yours to decide.
- **No dependencies in the core.** `Arkki.Pdf` references nothing but .NET. `Arkki.Pdf.Signing` adds
  Microsoft's `System.Security.Cryptography.Pkcs` and nothing else.
- **No telemetry.** Nothing is collected, logged or sent.

## Reading untrusted files

A file that cannot be read ends in one of three exceptions and nothing else: `InvalidDataException`
(damaged), `PdfPasswordException` (wrong password) or `NotSupportedException` (a feature Arkki does not
read). The reader is built for files made to hurt: objects nested too deeply are refused, reference
cycles are followed once, counts and lengths larger than the file can hold are refused, and a stream
that decodes past 256 MB (a decompression bomb) is refused.

The test suite mutates every sample file thousands of times on each run and adds hand-made traps
(`tests/Arkki.Tests/Robustness`). If you serve many users, still run PDF parsing with the memory and
time limits you would give any parser of untrusted input.

## Encryption

Arkki writes AES-256 (PDF 2.0, security handler revision 6) and nothing weaker. It *reads* the older
schemes too (RC4 40/128-bit and AES-128) because old files exist, but those are weak by today's
standards: do not count on them to protect a file. A user password protects a PDF only as well as the
password is strong, and permissions (print, copy) are a request to the viewer, not a lock.

## Signatures

`PdfSignatureVerifier` reports whether the signed bytes are intact, whether the signature covers the
whole file, its PAdES level, its timestamp and whether the certificate chains to the roots you pass.
It checks revocation with the OCSP responses and revocation lists the file carries, and `VerifyAsync`
fetches what is missing when you give it a client. A list or response counts only when the certificate's
issuer (or a responder the issuer delegated) signed it. What the verifier does **not** judge: how fresh
revocation information is, certificate policies, and whether changes made after a certification signature
stay within what it allows. A certificate revoked after a time that a valid timestamp proves is reported
as good, because the signature was made while it was; without a timestamp the claimed time proves nothing,
so a later revocation counts.

A visible signature's picture is only a picture: anyone can draw one. Trust what the verifier (or a
viewer's signature panel) says, never the box on the page.

Addresses in a certificate are data from whoever made the file. `HttpRevocationClient` follows only
`http` and `https` addresses, but on a server that verifies files from strangers, give it an `HttpClient`
that cannot reach your internal network. Keep signing keys out of source control; the tests make their own
throw-away certificates.

## Check it yourself

**No file, network or native access in the core.** Search the source; nothing turns up:

```sh
grep -rnE "File\.|Directory\.|HttpClient|Socket|Process\.|DllImport|LibraryImport|unsafe" src/Arkki.Pdf
```

**The package is the code.** Every release is built by GitHub Actions from the tagged source, and each
package comes with a build-provenance attestation and a SHA-256 checksum:

```sh
gh attestation verify Arkki.Pdf.<version>.nupkg --repo geezery/arkki
sha256sum -c Arkki.Pdf.<version>.nupkg.sha256
```

The packages carry Source Link, so a debugger steps into the exact source of the release.

## Supported versions

Only the latest release gets fixes.

## Reporting a vulnerability

If you find a file that hangs the reader, makes it use unbounded memory or fail with an exception other
than the three above, or a flaw in encryption, signing or verification, please report it privately
through [GitHub's private vulnerability reporting](https://github.com/geezery/arkki/security/advisories/new)
and not in a public issue. Attach the smallest file that shows it. You'll get an answer within a few
days, and credit in the release notes if you'd like it.
