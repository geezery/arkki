# FAQ

**What is Arkki?**
A PDF library for .NET in pure managed C#: it creates documents with a
paginating layout engine, opens and edits existing files, fills and creates
forms, encrypts, and signs. The name is Finnish for a sheet of paper.

**What licence is it under, and can I use it in a closed-source app?**
Arkki is under the Mozilla Public License 2.0, which lets you use it in
closed-source and commercial applications; if you change Arkki's own source
files and distribute the result, those files stay under the MPL and their
source must be made available. That is a summary and not legal advice: the
[licence text](https://github.com/geezery/arkki/blob/main/LICENSE) is what
counts. The Liberation Sans fonts under `samples/fonts` are licensed separately
under the SIL Open Font License 1.1.

**Which .NET does it need?**
.NET 10.

**Which platforms?**
Windows, Linux and macOS. Everything is managed code on .NET's own APIs: no
System.Drawing, no native binaries, no browser engine.

**What does it depend on?**
The core package, `Arkki.Pdf`, has no dependencies. `Arkki.Pdf.Signing` adds
only Microsoft's `System.Security.Cryptography.Pkcs`.

**Why doesn't it use the fonts installed on the machine?**
Because then the same code would make different documents on different
machines. Fonts are loaded from bytes you pass in and embedded in the file, so
a document looks the same wherever it is made and wherever it is opened. Ship
the font files with your application.

**Is the output really the same every time?**
Yes. Output is deterministic: the same input gives the same bytes.

**Is it thread-safe?**
Fonts (`TrueTypeFont`) and images (`PdfImage`) are immutable and safe to share
across threads. A `PdfDocument` or a `PdfReader` is used by one thread at a
time. Build documents in parallel freely, one document per thread.

**How fast is it?**
A 50 000-row table lays out to 1 021 pages in about 1.2 s and 1.5 MB on a
two-core cloud VM. The three-page sample invoice renders in about 5 ms at
roughly 100 kB including the logo and two subset fonts.

**Can it convert HTML to PDF?**
No. There is no browser engine in Arkki. You build the document from layout
elements; see [[Layout]].

**What is not included?**
Tagged PDF / PDF/UA, PDF/A, right-to-left and complex-script shaping (Arabic,
Indic), rendering pages to images, SVG, and CFF font subsetting. In forms: push
buttons, JavaScript actions and calculated fields. In signing: visible
signature appearances, long-term validation data (B-LT/B-LTA), revocation
checking, and signing with an external device or service.

**Which form fields can it handle?**
Text fields, check boxes, radio buttons, combo boxes and list boxes are read,
filled and created; see [[Forms]].

**Which encryption does it write and read?**
It writes AES-256 (PDF 2.0, revision 6) and reads every standard security
handler revision: RC4 40/128-bit, AES-128 and AES-256. See
[[Encryption and Signing]].

**Will Adobe Reader show my signature as trusted?**
When the certificate comes from a CA the viewer trusts (for Adobe, the AATL or
EU trusted lists). A certificate you issued yourself gives a valid but
untrusted signature.

**Is it safe to open files from strangers?**
The reader is hardened for that. A file that cannot be read fails with
`InvalidDataException`, `PdfPasswordException` or `NotSupportedException`,
never with another exception or a hang. The test suite mutates every sample
file thousands of times and tries hand-made traps: deep nesting, reference
cycles, huge counts and lengths, and a decompression bomb (streams decoding
past 256 MB are refused).

**Does it ship hyphenation dictionaries?**
No. Soft hyphens and a compound word's own hyphen are break points, and you can
plug in language rules with `IHyphenator`.

**Is there a complete example?**
The [samples folder](https://github.com/geezery/arkki/tree/main/samples) shows
a complete Finnish Finvoice invoice with the bank barcode and the credit
transfer form. It is an example to copy, not part of the library.

**How do I build and test it myself?**

```
dotnet build Arkki.slnx -c Release
dotnet run -c Release --project tests/Arkki.Tests
```

**Where do I report a bug or a security problem?**
Bugs go to [issues](https://github.com/geezery/arkki/issues). For a
vulnerability, use
[private vulnerability reporting](https://github.com/geezery/arkki/security/advisories/new)
and not a public issue.
