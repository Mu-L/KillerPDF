<p align="center">
  <a href="https://killerpdf.net"><img src="docs/wordmark.png" width="640" alt="KillerPDF wordmark: a free, open-source PDF editor for Windows"></a>
</p>

KillerPDF is a free, open-source PDF editor for Windows. View, annotate, OCR, merge, split, edit text, draw, sign, fill forms, print, flatten, and open password-protected PDFs without an Adobe subscription. Install it or run it portable from the same Windows download. No runtime installation is required, and the app never phones home.

Full how-tos live on the [help page](https://killerpdf.net/help.html); internals, formats, and limits on the [technical page](https://killerpdf.net/technical.html).

## Features

- High-quality PDFium rendering with four view modes (Single, Continuous, Two-Page with a book layout option, Grid), tabbed documents, and a split pane for two documents side by side
- Annotate with inline text editing and font matching, word-wrapping text boxes, drawing, lines, highlights, images, and page-number or watermark stamps. Highlights use the Multiply blend so the text underneath stays readable, and every tab has its own undo and redo history.
- Built-in OCR (Tesseract bundled, no cloud): make searchable PDFs, OCR a page or region to the clipboard, extract all text; extra languages download on demand
- Organize pages: merge, split, insert, rotate, crop, extract, delete, drag-and-drop reordering; drop a folder or `.zip` onto the window to merge its contents
- Export one page or a multi-page selection as PNG or JPEG directly from the Pages panel
- Transform: rotate, scale, flip, deskew by drawing a level line, perspective correction for photographed pages, and a LEVELS section (black point, white point, midtones) for pale scans
- Forms: fill text, checkbox, radio, and comb fields as live controls and save back; digital signatures with a cloud certificate (Certum SimplySign), plus drawn or imported signatures and initials
- Print with a real in-app preview, paper size and source selection, scale / position / margins / pages-per-sheet options at 300 DPI; Save Flattened rasterizes to a fully uneditable PDF
- Full-text search with highlighting, and column-aware text selection that copies multi-column pages in reading order
- Night-mode inversion works independently in each split pane. Thirteen themes, live accent colors, and toolbar styles provide 33 looks, while the resizable sidebar can dock on either side.
- Localized UI in 12 languages (contribute via `TRANSLATING.md`); full keyboard shortcut overlay on F1 with list and visual keyboard views
- Opens password-protected PDFs (prompts instead of erroring) and repairs damaged ones
- Runs portable, or self-installs per-user (no UAC) or machine-wide (`/silent` for scripted deployment); registers as a PDF handler and uninstalls cleanly
- Standards-safe saves: every release is tested against a 2,900-file veraPDF conformance corpus with a zero-regressions requirement. See [validation/RESULTS.md](validation/RESULTS.md).
- Local-only: no account, no telemetry, no phone-home

## Command line

Every core operation also runs headless from a terminal, with meaningful exit codes, even while the app is open:

```powershell
KillerPDF.exe --merge out.pdf a.pdf b.pdf scan.jpg
KillerPDF.exe --extract-pages in.pdf 1-3,5 out.pdf
KillerPDF.exe --split in.pdf pages\
KillerPDF.exe --decrypt locked.pdf open.pdf [--password p]
KillerPDF.exe --to-image in.pdf imgs\ --dpi 300 --format jpg
KillerPDF.exe --flatten in.pdf flat.pdf
KillerPDF.exe --print in.pdf --printer "HP LaserJet" --pages 1-4 --copies 2
KillerPDF.exe --ocr scan.pdf searchable.pdf --lang eng
KillerPDF.exe --batch-resave inDir\ outDir\ --log report.csv
KillerPDF.exe --help
```

Full reference on the [help page](https://killerpdf.net/help.html).

## Screenshots

| | |
| --- | --- |
| ![KillerPDF showing a 111-page camera manual in Grid view with the language flyout open, in the Decay theme with the menu bar hidden](docs/grid-language-flyout.png)<br>**Grid view and twelve languages** - Survey a whole document at once, with the menu bar hidden and the interface switchable between twelve languages. | ![KillerPDF using split-pane view with a 98SE-themed Transform preview open over a scanned camera manual](docs/split-pane-transform.png)<br>**Split panes and Transform** - Work in two independent panes while previewing rotation, scale, flip, skew, perspective, and Levels before applying. |
| ![KillerPDF showing its drawing controls, custom color picker, and interactive form pages in Grid view](docs/annotations-color-picker.png)<br>**Annotation colors and forms** - Draw with exact colors while viewing fillable fields, comb boxes, and the brochure's live form examples. | ![KillerPDF showing its themed image picker with a large thumbnail preview over a two-page document view](docs/image-picker-preview.png)<br>**Image picker and previews** - Browse images, inspect a large preview, and return directly to the open document. |

## Requirements

- Windows 10 or 11 (x64)
- No runtime install. Everything needed is inside the EXE (targets .NET Framework 4.8, which ships with every supported Windows release).

## Download

WinGet:

```powershell
winget install killerpdf
```

Chocolately:

```powershell
choco install killerpdf
```

- Prebuilt binary: <https://github.com/SteveTheKiller/KillerPDF/releases/latest/download/KillerPDF.exe>
- Source (GPL3 corresponding source for this release): <https://github.com/SteveTheKiller/KillerPDF/releases/download/v1.7.5/KillerPDF-1.7.5-src.zip>

## Build from source

```powershell
git clone https://github.com/SteveTheKiller/KillerPDF.git
cd KillerPDF
dotnet publish -c Release
```

Output lands in `bin/Release/net48/publish/`. Normal publishing produces the development single-file build plus a versioned `KillerPDF-<version>-src.zip`. The release pipeline builds a verified multi-file payload and packs it into one portable `KillerPDF.exe`; installed shortcuts launch the inner app directly for faster startup.

Requires the .NET 8 SDK or later to build (even though the output targets .NET Framework 4.8).

The PDF write engine (PdfSharpCore, MIT) is vendored under `third_party/PdfSharpCore/` and builds as part of the solution; it carries six standards-conformance patches, each marked `KillerPDF patch` in the source. Origin commit and details are recorded in `third_party/PdfSharpCore/VENDORED.txt`.

## Translations

UI strings live in `Strings/` (one XAML `ResourceDictionary` per locale). To add or improve a language, see [TRANSLATING.md](TRANSLATING.md). Missing keys fall back to English, so a partial translation is fine.

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## License

GPLv3. See [LICENSE](LICENSE). If you fork, modify, or redistribute KillerPDF, your version must also be released under GPLv3 with source available. No exceptions for commercial rebrands.
