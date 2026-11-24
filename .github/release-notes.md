## Install

```
dotnet add package Arkki.Pdf --version VERSION
dotnet add package Arkki.Pdf.Signing --version VERSION
```

Arkki needs .NET 10 and runs on Windows, Linux and macOS. `Arkki.Pdf` has no dependencies;
`Arkki.Pdf.Signing` adds Microsoft's `System.Security.Cryptography.Pkcs`.

The packages are attached below and published on [nuget.org](https://www.nuget.org/packages/Arkki.Pdf/VERSION).
Each has a SHA-256 checksum and a build-provenance attestation:

```
gh attestation verify Arkki.Pdf.VERSION.nupkg --repo geezery/arkki
```

What changed is in the [changelog](https://github.com/geezery/arkki/blob/main/CHANGELOG.md);
guides are in the [wiki](https://github.com/geezery/arkki/wiki) and on the [homepage](https://geezery.github.io/arkki/).
