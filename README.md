# KillerPDF

[![oosmetrics](https://api.oosmetrics.com/api/v1/badge/achievement/bf0a2893-0ce5-4fc8-a6cc-084ad0722ed2.svg)](https://oosmetrics.com/repo/SteveTheKiller/KillerPDF)
[![oosmetrics](https://api.oosmetrics.com/api/v1/badge/achievement/dd592456-00ad-445d-b97b-21e44ee4b44e.svg)](https://oosmetrics.com/repo/SteveTheKiller/KillerPDF)

PDF editor for field techs. View, annotate, merge, split, edit text, draw, sign, print, flatten, and open password-protected PDFs without an Adobe subscription or a phone-home. Install or run portable. Single Windows EXE, ~6 MB zipped, no runtime install required.

Part of [killertools.net](https://killertools.net).

## Features

- High-quality rendering via PDFium (Docnet.Core)
- Merge multiple PDFs and split out selected pages, drag-and-drop page reordering
- Inline text editing with font matching against the original document
- Text boxes, freehand drawing, and highlight overlays with adjustable color, size, and opacity
- Draw and save reusable signatures or import a PNG/JPG/BMP image as a signature, click to place anywhere on a page
- Insert images onto any page as resizable annotations — drag the corner handle to scale, burned into the PDF on save
- Right-click sidebar: insert blank page, rotate CW/CCW, move up/down, extract, or delete — works on multi-page selections
- Clickable PDF links and internal cross-references, including TOC back-links
- Multi-page grid view at low zoom levels for context across the whole document
- Zoom preset dropdown with scroll-wheel sync
- Full-text search across the entire document with result highlighting, drag-select to copy text
- Unsaved-changes protection with dirty tracking and title bar indicator
- Close file without quitting (Ctrl+W)
- Print with annotations flattened into the output
- Save Flattened PDF: rasterizes every page at 150 DPI via PDFium into a fully uneditable document
- Password-protected PDF support: prompts for password instead of erroring, decrypted copy held in temp for the session
- Self-installing EXE: running from outside the install path shows an Install / Run Portable dialog; running a newer version shows an Update prompt instead. Installs per-user to %LOCALAPPDATA% (no UAC), registers as PDF file handler, adds Start Menu and optional Desktop shortcuts, uninstalls cleanly via Add/Remove Programs

## Screenshots

![KillerPDF image annotation](screenshots/add_image.png)

![KillerPDF right-click sidebar](screenshots/right_click_sidebar.png)

## Requirements

- Windows 10 or 11 (x64)
- No runtime install. Everything needed is inside the EXE (targets .NET Framework 4.8, which ships with every supported Windows release).

## Download

- Prebuilt binary: <https://github.com/SteveTheKiller/KillerPDF/releases/latest/download/KillerPDF.zip>
- Source (GPL3 corresponding source for this release): <https://github.com/SteveTheKiller/KillerPDF/releases/download/v1.4.0/KillerPDF-1.4.0-src.zip>

## Build from source

```powershell
git clone https://github.com/SteveTheKiller/KillerPDF.git
cd KillerPDF
dotnet publish -c Release
```

Output lands in `bin/Release/net48/publish/`. The publish step produces a single Costura-bundled `KillerPDF.exe` plus a versioned `KillerPDF-<version>-src.zip` for GPL3 source distribution.

Requires the .NET 8 SDK or later to build (even though the output targets .NET Framework 4.8).

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## Why this exists

I hate Adobe. Acrobat is bloated, tries to hijack file associations, wants a subscription to do basic things, and phones home constantly. Most of the "free" alternatives are either ad-riddled, cloud-based, or rebrands of the same PDF engine sold under three different names.

KillerPDF is what I wanted: local-only, portable, no account, no telemetry. The PDF equivalent of Notepad.

## License

GPLv3. See [LICENSE](LICENSE). If you fork, modify, or redistribute KillerPDF, your version must also be released under GPLv3 with source available. No exceptions for commercial rebrands.
