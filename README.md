# KillerPDF

Portable PDF editor for field techs. View, annotate, merge, split, edit text, draw, sign, and print without an Adobe subscription or a phone-home. Single Windows app, ~10 MB zipped.

Part of [killertools.net](https://killertools.net).

## Features

- High-quality rendering via PDFium (Docnet.Core)
- Merge multiple PDFs and split out selected pages, drag-and-drop page reordering
- Inline text editing with font matching against the original document
- Text boxes, freehand drawing, and highlight overlays with adjustable color, size, and opacity
- Draw and save reusable signatures, click to place anywhere on a page
- Full-text search with result highlighting, drag-select to copy text
- Print with annotations flattened into the output
- No installer, no admin rights, no config files, no account

## Screenshots

![KillerPDF main view](screenshots/main-view.png)

![Highlighting and annotation](screenshots/highlight.png)

## Requirements

- Windows 10 or 11 (x64)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## Download

Prebuilt binaries: <https://dl.killertools.net/KillerPDF.zip>

## Build from source

```powershell
git clone https://github.com/SteveTheKiller/KillerPDF.git
cd KillerPDF
dotnet publish -c Release -r win-x64 --self-contained false
```

Output lands in `bin/Release/net8.0-windows/win-x64/publish/`.

## Why this exists

I hate Adobe. Acrobat is bloated, tries to hijack file associations, wants a subscription to do basic things, and phones home constantly. Most of the "free" alternatives are either ad-riddled, cloud-based, or rebrands of the same PDF engine sold under three different names.

KillerPDF is what I wanted: local-only, portable, no account, no telemetry. The PDF equivalent of Notepad.

## License

GPLv3. See [LICENSE](LICENSE). If you fork, modify, or redistribute KillerPDF, your version must also be released under GPLv3 with source available. No exceptions for commercial rebrands.
