# Arkki

A PDF library for .NET 10 in pure managed C# (MPL-2.0): a paginating layout engine, a reader, editing
and merging, forms, AES-256 encryption, and PAdES signatures in a second package. No native code, and no
dependencies in the core package.

Read before changing behaviour: `CONTRIBUTING.md` (house rules), `docs/DESIGN.md` (how the parts fit),
`SECURITY.md` (claims the code must keep), `README.md` (its samples are compiled by a test).

## Layout

| Path | What |
|---|---|
| `Arkki.slnx`, `Directory.Build.props` | Solution; shared build settings, the version and NuGet package metadata. |
| `src/Arkki.Pdf/` | The core package, by feature: `Document/`, `Drawing/`, `Fonts/`, `Images/`, `Barcodes/`, `Layout/`, `Forms/`, `Reading/`, `Merging/`, `Security/`, `Internal/` (writer, serializer). |
| `src/Arkki.Pdf.Signing/` | PAdES signing and verification; depends on `System.Security.Cryptography.Pkcs` only. |
| `tests/Arkki.Tests/` | A console test program (no test framework): `Program.cs` calls each suite's `Run()`; `Assert.cs` has `Check`, `Equal`, `Throws`; `External.cs` asks qpdf, poppler, pypdf and pyHanko when they are installed. Suites mirror the source folders. |
| `tests/Arkki.Tests/Data/Pdf/` | Files from other producers, described in that folder's README. |
| `samples/` | A Finnish Finvoice invoice with a bank barcode and credit transfer form: an example, not part of the library. |
| `site/`, `docs/assets/` | Homepage (one static HTML file, self-hosted fonts) and the logo, icon and banner; deployed by `.github/workflows/pages.yml`. |
| `docs/wiki/` | GitHub wiki pages, synced by `.github/workflows/wiki.yml`. |
| `.github/workflows/` | `ci.yml` (build, test and pack on Linux, Windows and macOS), `release.yml` (on a `v*` tag), `pages.yml`, `wiki.yml`. |

## Commands

| Command | What it does |
|---|---|
| `dotnet build Arkki.slnx -c Release -warnaserror` | Build everything; warnings fail, including missing XML documentation on public members. |
| `dotnet run -c Release --project tests/Arkki.Tests` | Run every test; exits non-zero when one fails and prints `N passed, 0 failed, M external checks skipped`. |
| `dotnet pack src/Arkki.Pdf -c Release -o artifacts` | Pack a package (also `src/Arkki.Pdf.Signing`). |
| `ARKKI_FUZZ_ITERATIONS`, `ARKKI_FUZZ_SEED` | Environment variables that widen the fuzz run. |

On Windows the external validators are usually missing, so their checks are skipped and only CI's Linux
job runs them: push and read that job before calling a change to written PDF bytes done. Git for
Windows ships Xpdf's `pdftotext`; `External.cs` ignores tools that are not poppler's.

## Conventions

- **House rules** (`CONTRIBUTING.md`): no dependencies in `Arkki.Pdf`; managed code only; identical on
  Windows, Linux and macOS; deterministic output; a bad file ends in `InvalidDataException`,
  `PdfPasswordException` or `NotSupportedException` and never hangs; bad arguments fail at once.
- One public type per file, files grouped by feature, split before about 400 lines. Namespaces are
  coarser than folders (`Arkki.Pdf`, `.Fonts`, `.Barcodes`, `.Layout`, `.Reading`).
- Canvas coordinates have the origin at the top-left with y down, in points; `PdfPage.ToPdfRect`
  converts to PDF space.
- Objects are written through `ByteBuf` and `PdfWriter`; strings go through `PdfSerializer.WriteString`
  so encryption sees them. Imported objects are copied by `PageImporter`, with `Overrides` for edits.
- Every public member has `///` documentation; comments explain why, in plain English.
- New behaviour has a test in the matching suite, and a new suite is added to `Program.cs`.
- Docs stay true: a feature or limit that changes updates `README.md`, `CHANGELOG.md`, `docs/wiki/` and,
  when it shows there, `site/index.html` and `site/llms.txt`.
- A release is a `v*` tag (`RELEASING.md`); the package version comes from the tag.
- Commit in logical pieces, each with a clear message in English.
