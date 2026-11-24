# Changelog

All notable changes to Arkki. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
project uses [semantic versioning](https://semver.org/).

## [1.0.0-rc.1] - 2026-11-24

The first release candidate. Arkki is a general-purpose PDF library for .NET 10 in pure managed code.

### Added

- **Creating documents**: a writer with deterministic output; a paginating layout engine (text blocks with
  mixed styles, left/right/centred/justified alignment, soft-hyphen and pluggable hyphenation, lists, tables
  with repeated headers, column spans, cell fills and borders, rows, columns, boxes, padding, keep-together,
  keep-with-next and page breaks); headers and footers with page numbers; an immediate-mode canvas.
- **Paper sizes**: A0–A7, B4, B5, C4–C6, DL, Letter, Legal, Tabloid, Executive, landscape and custom sizes.
- **Fonts**: TrueType embedding with subsetting; OpenType CFF fonts embedded whole; ToUnicode maps so text
  can be copied and searched.
- **Images**: PNG (with transparency as a soft mask) and JPEG, stored in the smallest lossless form and
  embedded once per document.
- **Barcodes**: Code 128, EAN-13 and QR code as vectors, with an extension point for other symbologies.
- **Reading**: cross-reference tables and streams, object streams, incremental updates, repair of damaged
  cross-references, text extraction, metadata, form fields, encrypted files (RC4 and AES, all standard
  security handler revisions).
- **Editing**: open a PDF and stamp over its pages, insert, remove, move and rotate pages, merge files,
  extract and split pages.
- **Forms**: read, fill and create text fields, check boxes, radio button groups, combo boxes and list boxes
  (single and multiple choice); fields are filled by name and get appearances that show in every viewer. A dotted
  name ("customer.name") is written as a field tree.
- **Links, bookmarks and attachments**: URL and in-document links, a nested outline, embedded files.
- **Encryption**: AES-256 (revision 6) with user and owner passwords and permissions.
- **Signing** (`Arkki.Pdf.Signing`): PAdES B-B and B-T signatures as incremental updates, RFC 3161
  timestamps through `ITimestampClient` / `HttpTimestampClient`, and a verifier for integrity, coverage,
  PAdES attributes, timestamps and the certificate chain.

### Security and robustness

- Damaged or hostile files end in `InvalidDataException`, `PdfPasswordException` or `NotSupportedException`;
  the reader is fuzz-tested with mutated files and hand-made traps (deep nesting, cycles, oversized counts,
  decompression bombs: a stream decoding past 256 MB is refused).
- Public methods validate their arguments.

### Known limits

- Not included: tagged PDF / PDF/UA, PDF/A, right-to-left and complex-script shaping, rendering to images,
  SVG, CFF font subsetting, visible signature appearances, long-term validation (PAdES B-LT/B-LTA),
  revocation checking, and signing with external devices or services.
- A `PdfDocument` or `PdfReader` is used by one thread at a time; fonts and images can be shared.

[1.0.0-rc.1]: https://github.com/geezery/arkki/releases/tag/v1.0.0-rc.1
