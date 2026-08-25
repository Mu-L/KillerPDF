using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Editing;
using KillerPdf.Engine.Writing;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingImage = System.Drawing.Image;

namespace KillerPDF.Services;

/// <summary>Bridges completed application state into The KillerPDF.Engine during migration.</summary>
internal static class PdfEngineIntegration
{
    internal sealed record FormEdits(
        IReadOnlyDictionary<string, string> TextValues,
        IReadOnlyDictionary<string, string> ChoiceValues,
        IReadOnlyDictionary<string, bool> CheckBoxValues,
        IReadOnlyDictionary<string, string> RadioValues,
        IReadOnlyDictionary<string, double> TextFontSizes);

    /// <summary>Applies a complete pending form-edit batch as one incremental revision.</summary>
    internal static void ApplyFormValues(string path, FormEdits edits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.TextValues.Count == 0 && edits.ChoiceValues.Count == 0
            && edits.CheckBoxValues.Count == 0 && edits.RadioValues.Count == 0)
            return;
        PdfDocument document = PdfDocument.Open(File.ReadAllBytes(path));
        var editor = new PdfIncrementalPageEditor(document);
        foreach ((string name, string value) in edits.TextValues.OrderBy(item => item.Key))
            editor.SetTextFieldValue(name, value, fontSize:
                edits.TextFontSizes.TryGetValue(name, out double size) ? size : null);
        foreach ((string name, string value) in edits.ChoiceValues.OrderBy(item => item.Key))
            editor.SetChoiceFieldValue(name, value);
        foreach ((string name, bool value) in edits.CheckBoxValues.OrderBy(item => item.Key))
            editor.SetCheckBoxValue(name, value);
        foreach ((string name, string value) in edits.RadioValues.OrderBy(item => item.Key))
            editor.SetRadioButtonValue(name, value.TrimStart('/'));
        ReplaceWithBuiltResult(path, editor.Build());
    }

    /// <summary>Authenticates and fully rewrites a PDF without password encryption.</summary>
    internal static void RemoveEncryption(
        string sourcePath, string destinationPath, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(password);
        PdfDocument document = PdfDocument.Open(File.ReadAllBytes(sourcePath), password);
        byte[] result = PdfDocumentWriter.Write(document,
            new PdfDocumentWriteOptions { RemoveEncryption = true });
        ReplaceWithBuiltResult(destinationPath, result);
    }

    /// <summary>Merges complete PDF documents while preserving the first document byte prefix.</summary>
    internal static byte[] MergeDocuments(IReadOnlyList<byte[]> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
            throw new ArgumentException("At least one PDF document is required.", nameof(sources));

        PdfDocument document = PdfDocument.Open(sources[0]);
        var editor = new PdfIncrementalPageEditor(document);
        for (int index = 1; index < sources.Count; index++)
            editor.AddImportedDocument(PdfDocument.Open(sources[index]));
        return editor.Build();
    }

    /// <summary>Merges PDF documents and image frames through one engine page tree.</summary>
    internal static byte[] MergeFiles(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
            throw new ArgumentException("At least one input file is required.", nameof(paths));

        bool firstIsPdf = string.Equals(Path.GetExtension(paths[0]), ".pdf",
            StringComparison.OrdinalIgnoreCase);
        PdfDocument target = firstIsPdf
            ? PdfDocument.Open(File.ReadAllBytes(paths[0]))
            : PdfDocument.Open(new PdfDocumentBuilder().Build());
        var editor = new PdfIncrementalPageEditor(target);
        foreach (string path in paths.Skip(firstIsPdf ? 1 : 0))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (string.Equals(Path.GetExtension(path), ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                editor.AddImportedDocument(PdfDocument.Open(File.ReadAllBytes(path)));
                continue;
            }
            AppendImageFrames(editor, path);
        }
        return editor.Build();
    }

    /// <summary>Merges every readable PDF or image input and skips invalid entries.</summary>
    internal static byte[] MergeReadableFiles(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());
        var editor = new PdfIncrementalPageEditor(empty);
        foreach (string path in paths)
        {
            try
            {
                if (string.Equals(Path.GetExtension(path), ".pdf",
                        StringComparison.OrdinalIgnoreCase))
                    editor.AddImportedDocument(PdfDocument.Open(File.ReadAllBytes(path)));
                else
                    AppendImageFrames(editor, path);
            }
            catch
            {
                // Folder and archive imports deliberately retain every readable entry.
            }
        }
        if (editor.PageCount == 0)
            throw new InvalidOperationException("No readable PDF or image pages were found.");
        return editor.Build();
    }

    private static void AppendImageFrames(PdfIncrementalPageEditor editor, string path)
    {
        using DrawingImage source = DrawingImage.FromFile(path);
        var dimension = new System.Drawing.Imaging.FrameDimension(source.FrameDimensionsList[0]);
        int frameCount = Math.Max(1, source.GetFrameCount(dimension));
        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            source.SelectActiveFrame(dimension, frameIndex);
            int width = source.Width;
            int height = source.Height;
            double dpiX = source.HorizontalResolution;
            double dpiY = source.VerticalResolution;
            if (dpiX is < 24 or > 4800) dpiX = 96;
            if (dpiY is < 24 or > 4800) dpiY = 96;
            double pageWidth = width * 72.0 / dpiX;
            double pageHeight = height * 72.0 / dpiY;
            double shrink = Math.Min(1, 14400.0 / Math.Max(pageWidth, pageHeight));
            pageWidth *= shrink;
            pageHeight *= shrink;
            double grow = Math.Max(1, 3.0 / Math.Min(pageWidth, pageHeight));
            pageWidth *= grow;
            pageHeight *= grow;

            using var bitmap = new DrawingBitmap(width, height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (DrawingGraphics graphics = DrawingGraphics.FromImage(bitmap))
                graphics.DrawImage(source, 0, 0, width, height);
            byte[] rgba = CopyRgba(bitmap);
            PdfImage image = PdfImage.FromRgba(width, height, rgba);
            editor.AddPage(pageWidth, pageHeight,
                new PdfContentStreamBuilder().DrawImage(
                    image, 0, 0, pageWidth, pageHeight));
        }
    }

    private static byte[] CopyRgba(DrawingBitmap bitmap)
    {
        var rectangle = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        System.Drawing.Imaging.BitmapData data = bitmap.LockBits(rectangle,
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            byte[] rgba = new byte[checked(bitmap.Width * bitmap.Height * 4)];
            byte[] row = new byte[Math.Abs(data.Stride)];
            for (int y = 0; y < bitmap.Height; y++)
            {
                IntPtr rowAddress = IntPtr.Add(data.Scan0, y * data.Stride);
                System.Runtime.InteropServices.Marshal.Copy(rowAddress, row, 0, row.Length);
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int source = x * 4;
                    int target = (y * bitmap.Width + x) * 4;
                    rgba[target] = row[source + 2];
                    rgba[target + 1] = row[source + 1];
                    rgba[target + 2] = row[source];
                    rgba[target + 3] = row[source + 3];
                }
            }
            return rgba;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    internal sealed record RasterPage(
        int PixelWidth, int PixelHeight, double PageWidth, double PageHeight,
        ReadOnlyMemory<byte> BgraPixels);

    /// <summary>Authors a flattened PDF from opaque or alpha-bearing PDFium BGRA pages.</summary>
    internal static byte[] CreateRasterDocument(IReadOnlyList<RasterPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0)
            throw new ArgumentException("At least one raster page is required.", nameof(pages));
        var builder = new PdfDocumentBuilder();
        foreach (RasterPage page in pages)
        {
            if (page.PixelWidth <= 0 || page.PixelHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(pages),
                    "Raster page dimensions must be positive.");
            int required = checked(page.PixelWidth * page.PixelHeight * 4);
            if (page.BgraPixels.Length != required)
                throw new ArgumentException(
                    "A raster page does not contain the required BGRA pixel count.", nameof(pages));
            byte[] rgba = page.BgraPixels.ToArray();
            for (int pixel = 0; pixel < rgba.Length; pixel += 4)
                (rgba[pixel], rgba[pixel + 2]) = (rgba[pixel + 2], rgba[pixel]);
            PdfImage image = PdfImage.FromRgba(page.PixelWidth, page.PixelHeight, rgba);
            builder.AddPage(page.PageWidth, page.PageHeight,
                new PdfContentStreamBuilder().DrawImage(
                    image, 0, 0, page.PageWidth, page.PageHeight));
        }
        return builder.Build();
    }

    /// <summary>Reads crop-aware page dimensions and native rotations.</summary>
    internal static IReadOnlyList<PdfPageInformation> ReadPageInformation(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return PdfPageInformation.Read(PdfDocument.Open(File.ReadAllBytes(path)));
    }

    /// <summary>Extracts selected pages into a new PDF in the supplied order.</summary>
    internal static byte[] ExtractPages(byte[] source, IReadOnlyList<int> pageIndices)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pageIndices);
        if (pageIndices.Count == 0)
            throw new ArgumentException("At least one page is required.", nameof(pageIndices));

        PdfDocument sourceDocument = PdfDocument.Open(source);
        PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());
        var editor = new PdfIncrementalPageEditor(empty);
        foreach (int pageIndex in pageIndices)
            editor.AddImportedPage(sourceDocument, pageIndex);
        return editor.Build();
    }

    /// <summary>Splits a PDF into one independently valid PDF per source page.</summary>
    internal static IReadOnlyList<byte[]> SplitPages(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        PdfDocument sourceDocument = PdfDocument.Open(source);
        int pageCount = new PdfIncrementalPageEditor(sourceDocument).PageCount;
        var results = new byte[pageCount][];
        for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            PdfDocument empty = PdfDocument.Open(new PdfDocumentBuilder().Build());
            results[pageIndex] = new PdfIncrementalPageEditor(empty)
                .AddImportedPage(sourceDocument, pageIndex)
                .Build();
        }
        return results;
    }

    internal readonly record struct PageRectangle(
        double X, double Y, double Width, double Height);

    internal sealed record ImportedDocument(
        string Path, IReadOnlyList<int> PageRotations);

    /// <summary>Validates that the engine can open a document for page-copy operations.</summary>
    internal static void ValidateDocument(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _ = new PdfIncrementalPageEditor(
            PdfDocument.Open(File.ReadAllBytes(path))).PageCount;
    }

    /// <summary>
    /// Writes the application's effective page rotations as the final incremental revision.
    /// The source file is replaced only after the engine has built the complete result.
    /// </summary>
    internal static void ApplyPageRotations(
        string path, IReadOnlyDictionary<int, int> rotations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(rotations);
        if (rotations.Count == 0) return;

        byte[] source = File.ReadAllBytes(path);
        PdfDocument document = PdfDocument.Open(source);
        var editor = new PdfIncrementalPageEditor(document);
        foreach ((int pageIndex, int rotation) in rotations.OrderBy(item => item.Key))
            editor.SetRotation(pageIndex, rotation);

        ReplaceWithBuiltResult(path, editor.Build());
    }

    /// <summary>Creates a rendering copy with every native page rotation set to zero.</summary>
    internal static void CreateZeroRotationCopy(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        PdfDocument document = PdfDocument.Open(File.ReadAllBytes(sourcePath));
        var editor = new PdfIncrementalPageEditor(document);
        for (int pageIndex = 0; pageIndex < editor.PageCount; pageIndex++)
            editor.SetRotation(pageIndex, 0);
        ReplaceWithBuiltResult(destinationPath, editor.Build());
    }

    /// <summary>Writes complete descriptive document metadata incrementally.</summary>
    internal static void ApplyDocumentMetadata(string path, PdfDocumentMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(metadata);
        PdfDocument document = PdfDocument.Open(File.ReadAllBytes(path));
        byte[] result = new PdfIncrementalPageEditor(document)
            .SetMetadata(metadata)
            .Build();
        ReplaceWithBuiltResult(path, result);
    }

    /// <summary>
    /// Writes visible crop and matching trim boundaries as the final incremental revision.
    /// A null rectangle removes both boundaries so the page falls back to its media box.
    /// </summary>
    internal static void ApplyCropBoxes(
        string path, IReadOnlyDictionary<int, PageRectangle?> crops)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(crops);
        if (crops.Count == 0) return;

        byte[] source = File.ReadAllBytes(path);
        PdfDocument document = PdfDocument.Open(source);
        var editor = new PdfIncrementalPageEditor(document);
        foreach ((int pageIndex, PageRectangle? crop) in crops.OrderBy(item => item.Key))
        {
            if (crop is PageRectangle box)
            {
                editor.SetCropBox(pageIndex, box.X, box.Y, box.Width, box.Height);
                editor.SetPageBox(pageIndex, PdfPageBox.Trim,
                    box.X, box.Y, box.Width, box.Height);
            }
            else
            {
                editor.ClearPageBox(pageIndex, PdfPageBox.Crop);
                editor.ClearPageBox(pageIndex, PdfPageBox.Trim);
            }
        }

        ReplaceWithBuiltResult(path, editor.Build());
    }

    /// <summary>Removes pages as one byte-preserving incremental revision.</summary>
    internal static void RemovePages(string path, IReadOnlyCollection<int> pageIndices)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(pageIndices);
        int[] removed = pageIndices.Distinct().OrderByDescending(index => index).ToArray();
        if (removed.Length == 0) return;

        byte[] source = File.ReadAllBytes(path);
        PdfDocument document = PdfDocument.Open(source);
        var editor = new PdfIncrementalPageEditor(document);
        foreach (int pageIndex in removed) editor.RemovePage(pageIndex);
        ReplaceWithBuiltResult(path, editor.Build());
    }

    /// <summary>Renumbers application rotation state after pages are removed.</summary>
    internal static void RemapRotationsAfterPageRemoval(
        Dictionary<int, int> rotations, IReadOnlyCollection<int> pageIndices)
    {
        ArgumentNullException.ThrowIfNull(rotations);
        ArgumentNullException.ThrowIfNull(pageIndices);
        int[] removed = pageIndices.Distinct().OrderBy(index => index).ToArray();
        if (removed.Length == 0) return;

        var remapped = new Dictionary<int, int>();
        foreach ((int oldIndex, int rotation) in rotations.OrderBy(item => item.Key))
        {
            if (Array.BinarySearch(removed, oldIndex) >= 0) continue;
            int shift = removed.Count(index => index < oldIndex);
            remapped[oldIndex - shift] = rotation;
        }
        rotations.Clear();
        foreach ((int pageIndex, int rotation) in remapped)
            rotations[pageIndex] = rotation;
    }

    /// <summary>Moves one page to its final position in a byte-preserving revision.</summary>
    internal static void MovePage(string path, int sourceIndex, int destinationIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] source = File.ReadAllBytes(path);
        PdfDocument document = PdfDocument.Open(source);
        byte[] result = new PdfIncrementalPageEditor(document)
            .MovePage(sourceIndex, destinationIndex)
            .Build();
        ReplaceWithBuiltResult(path, result);
    }

    /// <summary>Moves rotation state with a reordered page.</summary>
    internal static void RemapRotationsAfterPageMove(
        Dictionary<int, int> rotations, int sourceIndex, int destinationIndex)
    {
        ArgumentNullException.ThrowIfNull(rotations);
        if (sourceIndex == destinationIndex) return;
        var ordered = Enumerable.Range(0, rotations.Count)
            .Select(index => rotations[index])
            .ToList();
        int moved = ordered[sourceIndex];
        ordered.RemoveAt(sourceIndex);
        ordered.Insert(destinationIndex, moved);
        rotations.Clear();
        for (int index = 0; index < ordered.Count; index++)
            rotations[index] = ordered[index];
    }

    /// <summary>Inserts a blank page at its final zero-based position.</summary>
    internal static void InsertBlankPage(
        string path, int pageIndex, double width, double height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] source = File.ReadAllBytes(path);
        PdfDocument document = PdfDocument.Open(source);
        byte[] result = new PdfIncrementalPageEditor(document)
            .InsertBlankPage(pageIndex, width, height)
            .Build();
        ReplaceWithBuiltResult(path, result);
    }

    /// <summary>Creates a zero-rotation entry and shifts later page rotation state.</summary>
    internal static void RemapRotationsAfterPageInsertion(
        Dictionary<int, int> rotations, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(rotations);
        var ordered = Enumerable.Range(0, rotations.Count)
            .Select(index => rotations[index])
            .ToList();
        ordered.Insert(pageIndex, 0);
        rotations.Clear();
        for (int index = 0; index < ordered.Count; index++)
            rotations[index] = ordered[index];
    }

    /// <summary>Deep-copies one page directly after its source page.</summary>
    internal static void DuplicatePage(string path, int pageIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] sourceBytes = File.ReadAllBytes(path);
        PdfDocument target = PdfDocument.Open(sourceBytes);
        PdfDocument source = PdfDocument.Open(sourceBytes);
        byte[] result = new PdfIncrementalPageEditor(target)
            .InsertImportedPage(pageIndex + 1, source, pageIndex)
            .SetRotation(pageIndex + 1, 0)
            .Build();
        ReplaceWithBuiltResult(path, result);
    }

    /// <summary>Duplicates the source page's effective application rotation.</summary>
    internal static void RemapRotationsAfterPageDuplication(
        Dictionary<int, int> rotations, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(rotations);
        var ordered = Enumerable.Range(0, rotations.Count)
            .Select(index => rotations[index])
            .ToList();
        ordered.Insert(pageIndex + 1, ordered[pageIndex]);
        rotations.Clear();
        for (int index = 0; index < ordered.Count; index++)
            rotations[index] = ordered[index];
    }

    /// <summary>Replaces one page with the first page of an authored PDF.</summary>
    internal static void ReplacePage(string path, int pageIndex, string replacementPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementPath);
        PdfDocument target = PdfDocument.Open(File.ReadAllBytes(path));
        PdfDocument replacement = PdfDocument.Open(File.ReadAllBytes(replacementPath));
        var replacementEditor = new PdfIncrementalPageEditor(replacement);
        if (replacementEditor.PageCount < 1)
            throw new ArgumentException("The replacement document must contain a page.",
                nameof(replacementPath));

        byte[] result = new PdfIncrementalPageEditor(target)
            .RemovePage(pageIndex)
            .InsertImportedPage(pageIndex, replacement, 0)
            .SetRotation(pageIndex, 0)
            .Build();
        ReplaceWithBuiltResult(path, result);
    }

    /// <summary>Resets the replaced page's application rotation.</summary>
    internal static void RemapRotationsAfterPageReplacement(
        Dictionary<int, int> rotations, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(rotations);
        if (!rotations.ContainsKey(pageIndex))
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        rotations[pageIndex] = 0;
    }

    /// <summary>Creates a new document from selected working-document pages.</summary>
    internal static void ExtractPages(
        string sourcePath, string destinationPath, IReadOnlyList<int> pageIndices,
        IReadOnlyDictionary<int, int> rotations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(pageIndices);
        ArgumentNullException.ThrowIfNull(rotations);
        if (pageIndices.Count == 0)
            throw new ArgumentException("At least one page must be extracted.", nameof(pageIndices));

        PdfDocument source = PdfDocument.Open(File.ReadAllBytes(sourcePath));
        PdfDocument empty = PdfDocument.Open(new KillerPdf.Engine.Authoring.PdfDocumentBuilder().Build());
        var editor = new PdfIncrementalPageEditor(empty)
            .InsertImportedPages(0, source, pageIndices);
        for (int outputIndex = 0; outputIndex < pageIndices.Count; outputIndex++)
            editor.SetRotation(outputIndex,
                rotations.TryGetValue(pageIndices[outputIndex], out int rotation) ? rotation : 0);
        ReplaceWithBuiltResult(destinationPath, editor.Build());
    }

    /// <summary>Appends complete PDF documents and normalizes their rotations for the viewer.</summary>
    internal static void AppendDocuments(
        string path, IReadOnlyList<ImportedDocument> sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0) return;

        PdfDocument target = PdfDocument.Open(File.ReadAllBytes(path));
        var editor = new PdfIncrementalPageEditor(target);
        foreach (ImportedDocument import in sources)
        {
            PdfDocument source = PdfDocument.Open(File.ReadAllBytes(import.Path));
            int offset = editor.PageCount;
            int count = new PdfIncrementalPageEditor(source).PageCount;
            if (import.PageRotations.Count != count)
                throw new ArgumentException(
                    "The imported rotation count must match the source page count.", nameof(sources));
            editor.AddImportedDocument(source);
            for (int index = 0; index < count; index++)
                editor.SetRotation(offset + index, 0);
        }
        ReplaceWithBuiltResult(path, editor.Build());
    }

    /// <summary>Appends imported page rotations to the application rotation map.</summary>
    internal static void RemapRotationsAfterDocumentAppend(
        Dictionary<int, int> rotations, IReadOnlyList<ImportedDocument> sources)
    {
        ArgumentNullException.ThrowIfNull(rotations);
        ArgumentNullException.ThrowIfNull(sources);
        int index = rotations.Count;
        foreach (ImportedDocument source in sources)
            foreach (int rotation in source.PageRotations)
                rotations[index++] = ((rotation % 360) + 360) % 360;
    }

    private static void ReplaceWithBuiltResult(string path, byte[] result)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, result);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
