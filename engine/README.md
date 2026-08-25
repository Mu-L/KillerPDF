# KillerPDF Engine

KillerPDF Engine is an independent, UI-free .NET library for reading, validating, authoring, structurally editing, signing, encrypting, and writing PDF files. It was created to give KillerPDF a modern PDF 2.0 and PDF/A foundation, but its public API is designed for reuse by other applications.

The engine is under active development for KillerPDF 1.8.0. Its APIs are documented and extensively tested, but the first public package has not been released yet.

## Five-minute start

The current development build targets .NET 10. Add a project reference while working from this repository:

```xml
<ProjectReference Include="path\to\KillerPDF\engine\KillerPdf.Engine\KillerPdf.Engine.csproj" />
```

Create a PDF 2.0 document:

```csharp
using KillerPdf.Engine.Authoring;

byte[] pdf = new PdfDocumentBuilder()
    .SetMetadata(new PdfDocumentMetadata
    {
        Title = "Hello from KillerPDF Engine",
        Author = "Example application",
        Language = "en-US"
    })
    .AddBlankPage(612, 792)
    .Build();

File.WriteAllBytes("hello.pdf", pdf);
```

Open and deterministically rewrite an existing document:

```csharp
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Writing;

byte[] source = File.ReadAllBytes("input.pdf");
PdfDocument document = PdfDocument.Open(source);
byte[] rewritten = PdfDocumentWriter.Write(document);

File.WriteAllBytes("output.pdf", rewritten);
```

## What it covers

- PDF syntax, objects, streams, classic cross-reference tables, cross-reference streams, object streams, trailers, and incremental revisions
- Deterministic full rewrites and byte-preserving incremental updates
- PDF 2.0 document authoring with pages, content streams, graphics state, fonts, images, color spaces, shadings, patterns, transparency, and resources
- Navigation, bookmarks, named destinations, page labels, viewer preferences, transitions, optional content, and attachments
- Visual annotations, text markup, links, replies, popups, redactions, file attachments, and annotation editing
- AcroForm creation and editing for text fields, checkboxes, radio buttons, choice fields, push buttons, and signature fields
- Tagged PDF and PDF/UA-2 structure authoring and editing
- PDF/A-4, PDF/A-4e, and PDF/A-4f authoring safeguards
- RC4, AES-128, and AES-256 password security, crypt filters, authenticated imports, incremental updates, and rewrites
- Detached CMS signatures, certification permissions, field locks, seed constraints, signature discovery, cryptographic verification, and signed-revision analysis
- Structural diagnostics, bounded parsing, implementation limits, round-trip validation, and fail-closed import validation

## What it does not do

KillerPDF Engine is a document engine, not a renderer or desktop framework. It does not render pages, provide UI controls, or perform text extraction. KillerPDF currently uses PDFium for rendering and PdfPig for text extraction outside this library.

## Repository layout

```text
engine/
  KillerPdf.Engine/          Reusable library
  KillerPdf.Engine.Tests/    Unit and regression tests
  KillerPdf.Engine.Corpus/   Corpus gates and standards smoke generators
  docs/                      Architecture records
  CHANGELOG.md               Engine-only release history
  README.md                  This developer entry point
```

The engine remains in the KillerPDF monorepo so engine changes, application integration, tests, and corpus gates can evolve atomically. Its dependency boundary is deliberately independent: the library does not reference WPF, KillerPDF application code, PDFium, PdfPig, PdfSharpCore, or PDFsharp.

## Build and test

From the repository root:

```powershell
dotnet build engine\KillerPdf.Engine\KillerPdf.Engine.csproj -c Release
dotnet test engine\KillerPdf.Engine.Tests\KillerPdf.Engine.Tests.csproj -c Release
```

The project treats compiler warnings as errors and generates XML API documentation during normal builds.

## Validation

The current development gate includes:

- 1,404 engine tests
- A strict Release build with zero warnings
- A 2,907-file incremental structural corpus gate
- A 2,907-file selected-page import corpus gate with zero unexpected failures
- qpdf structural validation and veraPDF PDF/A-4 and PDF/UA-2 smoke validation for generated fixtures
- OpenSSL verification for real detached CMS signature fixtures

Corpus files are intentionally malformed or nonconforming in many cases. A refusal is expected when the source is structurally unsafe, credential-protected, or depends on unsupported global state. The gate distinguishes those intentional boundaries from unexpected engine failures.

## Design principles

- Preserve existing bytes when an operation can be represented as an incremental revision.
- Fail closed when required structure cannot be interpreted or preserved safely.
- Emit deterministic output so regressions are reproducible.
- Enforce explicit implementation limits before allocating or serializing unbounded structures.
- Keep public APIs typed and reusable instead of exposing KillerPDF application state.
- Treat conformance as validator-backed behavior, not a label inferred from the PDF header.

The original architecture decision is recorded in [ADR-001](docs/architecture/ADR-001-pdf-engine-boundary.md).

## Development status

The current version is `1.8.0-alpha.1`. The reusable engine foundation and API documentation are complete, while integration into the KillerPDF desktop application is the next major phase. PdfSharpCore still powers the existing application pipeline until each integration surface is replaced and verified.

See the [engine changelog](CHANGELOG.md) for detailed capability history.

## License

KillerPDF Engine is currently licensed under GPLv3 as part of the KillerPDF repository. See the repository [LICENSE](../LICENSE).
