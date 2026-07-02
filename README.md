# Tideway.PdfTools

In-memory PDF merge and password encryption for .NET, backed by the native [qpdf](https://qpdf.readthedocs.io/) library. No temp files — `byte[]` in, `byte[]` out.

```csharp
using Tideway.PdfTools;

byte[] merged     = PdfTools.Merge(pdfA, pdfB);              // pages of B appended to A
byte[] combined   = PdfTools.Merge(new[] { a, b, c });       // n-way merge, in order
byte[] locked     = PdfTools.Encrypt(pdf, "password");       // AES-256 (R6) password protection
byte[] unlocked   = PdfTools.Decrypt(locked, "password");    // remove password protection
bool   locked3    = PdfTools.IsEncrypted(pdf);               // incl. password-protected files
byte[] fastWeb    = PdfTools.Linearize(pdf);                 // "fast web view" for browser streaming
byte[] chapter    = PdfTools.ExtractPages(pdf, 3, 7);        // pages 3-7 (1-based, inclusive)
List<byte[]> each = PdfTools.SplitPages(pdf);                // one single-page PDF per page
int pages         = PdfTools.PageCount(pdf);
```

Failures (corrupt input, password-protected input) throw `QpdfException` carrying qpdf's full error text. qpdf warnings on slightly malformed PDFs are tolerated.

The raw qpdf C API surface is exposed as `Qpdf` for operations the wrapper doesn't cover.

## Native library

This package P/Invokes `libqpdf`, resolved in this order:

1. **`QPDF_LIBRARY_PATH` environment variable** — full path to the shared library; overrides everything.
2. **NuGet runtime assets** — `runtimes/{rid}/native/` binaries bundled in the package, when present.
3. **System-installed qpdf** — probed under the usual per-platform names, including versioned sonames (`libqpdf.so.28`–`30`, `libqpdf.28-30.dylib`, `qpdf28-30.dll`) and Homebrew/MacPorts locations (`/opt/homebrew/lib`, `/usr/local/lib`, `/opt/local/lib`).

Installing qpdf per platform:

| Platform | Install |
|----------|---------|
| macOS | `brew install qpdf` |
| Debian/Ubuntu (incl. containers) | `apt-get install libqpdf-dev` (the runtime package alone ships only versioned sonames, which the resolver also finds) |
| Windows | qpdf release zip from https://github.com/qpdf/qpdf/releases — put `qpdf*.dll` (and its companion DLLs) next to the app or on `PATH` |

### Bundling native binaries in the package

To make the package self-contained, drop platform builds into `runtimes/` before packing:

```
runtimes/win-x64/native/qpdf.dll
runtimes/linux-x64/native/libqpdf.so
runtimes/linux-arm64/native/libqpdf.so
runtimes/osx-x64/native/libqpdf.dylib
runtimes/osx-arm64/native/libqpdf.dylib
```

The .NET host then loads the right one automatically per platform/architecture. Note that libqpdf dynamically links libjpeg and zlib on most distro builds — bundled binaries should be built with static dependencies (or shipped with their companion libraries) to load reliably everywhere.

### CI-built native binaries

The `Build qpdf Natives` workflow (`.github/workflows/build-qpdf-natives.yml`) produces exactly these binaries: qpdf built from source for all five RIDs with zlib and libjpeg-turbo statically linked in and qpdf's own native crypto provider (no OpenSSL/GnuTLS), verified per platform to have no dynamic dependencies beyond OS defaults, plus qpdf's Apache-2.0 license alongside.

1. Run the workflow manually (GitHub → Actions → *Build qpdf Natives* → *Run workflow*); optionally override the qpdf release tag (default `v12.3.2`).
2. Download the combined `qpdf-runtimes` artifact from the run.
3. Extract it into `runtimes/` so the files land at `runtimes/{rid}/native/…`.
4. `dotnet pack PDFTools.csproj -c Release`.

The run also uploads a `tideway-pdftools-nupkg` artifact — the package already packed with the binaries in place — if you'd rather skip steps 3–4. Nothing is pushed to a feed and no binaries are committed to git.
