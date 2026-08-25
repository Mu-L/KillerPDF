using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Editing;

namespace KillerPDF.Services;

/// <summary>Bridges completed application state into The KillerPDF.Engine during migration.</summary>
internal static class PdfEngineIntegration
{
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
