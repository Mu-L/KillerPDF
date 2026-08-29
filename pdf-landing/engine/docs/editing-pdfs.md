# Editing PDFs

Use the incremental editors to change an existing document while retaining its original byte prefix and adding one bounded revision.

## Why incremental editing matters

An incremental update appends changed objects, a new cross-reference section, and a new trailer. The original bytes remain intact. This is important for preservation-sensitive workflows, audit trails, and signed revision analysis.

Use a deterministic full rewrite when you intentionally want a normalized document instead.

## Change pages

```csharp
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Editing;

PdfDocument document = PdfDocument.Open(File.ReadAllBytes("input.pdf"));

byte[] updated = new PdfIncrementalPageEditor(document)
    .RotateClockwise(0)
    .MovePage(2, 0)
    .AddBlankPage(612, 792)
    .Build();

File.WriteAllBytes("updated.pdf", updated);
```

Page operations include insertion, import, removal, reordering, rotation, page boxes, user units, transitions, thumbnails, content replacement, and content appending.

## Update document features

`PdfIncrementalPageEditor` also changes metadata, output intents, page layout, viewer preferences, open actions, named destinations, page labels, attachments, bookmarks, and AcroForm fields.

```csharp
byte[] updated = new PdfIncrementalPageEditor(document)
    .SetTextFieldValue("Customer.Name", "Ada Lovelace")
    .SetCheckBoxValue("Terms.Accepted", true)
    .Build();
```

## Edit annotations

Use `PdfIncrementalAnnotationEditor` for page annotations and links:

```csharp
var editor = new PdfIncrementalAnnotationEditor(document);

byte[] updated = editor
    .AddTextNote(0, 72, 700, "Reviewed")
    .Build();
```

The annotation editor supports text notes, text markup, free text, lines, shapes, ink, image stamps, redaction marks, file attachments, and links.

## Import safely

Page imports copy the complete dependent object graph with collision handling. The editor refuses imports that depend on unsupported global state rather than producing a document with silently damaged forms, navigation, optional content, or accessibility structure.

## Write to a new destination

Keep the source and output paths separate until `Build` succeeds. Replace the original file only after your application has completed its own validation and durable write procedure.
