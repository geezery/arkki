# Reader and merge test files

`arkki-2pages.pdf` is a two-page document written by Arkki (Liberation Sans, a header with the
page number, "First page" and "Second page"). The other files are made from it with common tools,
so the reader is tested against what other producers write:

| File | Made with | What it exercises |
|---|---|---|
| `objstm.pdf` | `qpdf --object-streams=generate` | cross-reference stream, object streams |
| `uncompressed.pdf` | `qpdf --stream-data=uncompress --compress-streams=n` | streams without filters |
| `encrypted.pdf` | `qpdf --encrypt user owner 256` | rejection of encrypted files |
| `ghostscript.pdf` | `gs -sDEVICE=pdfwrite` | Ghostscript output |
| `libreoffice.pdf` | `soffice --convert-to pdf` of a short text with Finnish letters | LibreOffice output |
| `incremental.pdf` | a new Info object and an appended cross-reference section with `/Prev` | incremental update |
| `broken-xref.pdf` | `startxref` pointing past the end of the file | cross-reference reconstruction |
| `broken-objstm.pdf` | `objstm.pdf` with a wrong `startxref` | reconstruction with object streams |
| `link.pdf` | pypdf: a link annotation on page 1 pointing to page 2 | references to pages that are not imported |
