# Contributing to Arkki

Thanks for stopping by. Arkki is a PDF library that stays small enough to read, and a few rules keep
it that way.

## The one question

Every idea gets the same question: **does a document need it?** Start from the PDF you have to
produce or read, not from the PDF specification.

- "Tables need a footer row that repeats on each page": yes, that's a document.
- "A form I received has radio buttons I can't fill": yes, with the file attached.
- "Add an HTML to PDF converter": no. That is a browser engine, and Arkki has no engines underneath.
- "Add a setting for the object numbering": no. Pick the best one and send that.

Not sure? Open an issue before writing code. A two-line issue is a lot cheaper than a
two-thousand-line pull request we have to say no to. [`docs/DESIGN.md`](docs/DESIGN.md) explains how
the parts fit and why.

## House rules

1. **No dependencies in `Arkki.Pdf`.** .NET's own libraries only. `Arkki.Pdf.Signing` may use
   Microsoft's `System.Security.Cryptography.Pkcs` and nothing else.
2. **Managed code only.** No native binaries, no P/Invoke, no `unsafe`, no `System.Drawing`.
3. **The same on Windows, Linux and macOS.** No system fonts, no culture-dependent formatting, no
   file-system or network access from the library: it works on the bytes it is given.
4. **Deterministic output.** The same input gives the same bytes, so no clocks, random numbers or
   dictionary order may leak into a file.
5. **A bad file is an exception, never a hang.** The reader fails with `InvalidDataException`,
   `PdfPasswordException` or `NotSupportedException`. Anything that loops over a file's data has a limit.
6. **A bad argument fails at once**, with `ArgumentException` or one of its kin and a message that says
   what to pass.

## Build and test

You need the .NET 10 SDK.

```sh
dotnet build Arkki.slnx -c Release -warnaserror
dotnet run -c Release --project tests/Arkki.Tests
```

The tests are a plain console program: it prints what failed and exits with a non-zero code. They run
anywhere. Where `qpdf`, poppler (`pdftotext`, `pdfinfo`, `pdftoppm`, `pdfsig`, `pdffonts`) and Python
with `pypdf` and `pyHanko` are installed, every generated file is also checked by them; CI installs
them on Linux, so a pull request gets their opinion even if your machine has none of them.

## Code style

- C# with nullable reference types on and warnings as errors.
- Files are grouped by feature, one public type per file; split a file before it grows past about 400
  lines. Tests mirror the source folders under `tests/Arkki.Tests`.
- Every public member has XML documentation that says what it does in plain words. The documentation
  ships in the package.
- Comments explain *why* (a viewer's quirk, a clause of the specification), not what the next line does.
- New behaviour comes with a test. A fix to the reader comes with the smallest file that showed the
  problem, under `tests/Arkki.Tests/Data/Pdf`, and a line about it in that folder's README. Never commit
  a file with personal or confidential data.
- A change people will notice goes into [`CHANGELOG.md`](CHANGELOG.md), and `README.md` stays true:
  its code samples are compiled and run by `tests/Arkki.Tests/ReadmeTests.cs`.

## Releases

Maintainers: see [RELEASING.md](RELEASING.md). In short, push a `v*` tag.

## License

By contributing you agree that your contribution is licensed under the Mozilla Public License 2.0.
