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
