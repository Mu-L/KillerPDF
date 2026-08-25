using System;
using System.Collections.Generic;
using System.IO;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Editing;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Security;
using KillerPDF.Services;
using Xunit;

namespace KillerPDF.Tests;

public sealed class PdfEngineIntegrationTests
{
    [Fact]
    public void RemoveEncryption_WritesPasswordFreeDocumentWithPreservedMetadata()
    {
        string sourcePath = Path.Combine(Path.GetTempPath(), $"killerpdf-encrypted-{Guid.NewGuid():N}.pdf");
        string destinationPath = Path.Combine(Path.GetTempPath(), $"killerpdf-decrypted-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(sourcePath, new PdfDocumentBuilder()
                .SetMetadata(new PdfDocumentMetadata { Title = "Preserved" })
                .SetPasswordEncryption(new PdfPasswordEncryptionOptions
                {
                    UserPassword = "user",
                    OwnerPassword = "owner"
                })
                .AddBlankPage()
                .Build());

            PdfEngineIntegration.RemoveEncryption(sourcePath, destinationPath, "owner");

            PdfDocument document = PdfDocument.Open(File.ReadAllBytes(destinationPath));
            Assert.False(document.IsEncrypted);
            Assert.Equal("Preserved", PdfDocumentInformation.Read(document).Title);
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
            if (File.Exists(destinationPath)) File.Delete(destinationPath);
        }
    }

    [Fact]
    public void CreateZeroRotationCopy_PreservesSourcePrefixAndClearsEveryRotation()
    {
        string sourcePath = Path.Combine(Path.GetTempPath(), $"killerpdf-rotated-{Guid.NewGuid():N}.pdf");
        string destinationPath = Path.Combine(Path.GetTempPath(), $"killerpdf-render-{Guid.NewGuid():N}.pdf");
        try
        {
            byte[] unrotated = new PdfDocumentBuilder()
                .AddBlankPage()
                .AddBlankPage()
                .Build();
            byte[] source = new PdfIncrementalPageEditor(PdfDocument.Open(unrotated))
                .SetRotation(0, 90)
                .SetRotation(1, 270)
                .Build();
            File.WriteAllBytes(sourcePath, source);

            PdfEngineIntegration.CreateZeroRotationCopy(sourcePath, destinationPath);

            byte[] result = File.ReadAllBytes(destinationPath);
            Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
            Assert.Contains("/Rotate 0", System.Text.Encoding.ASCII.GetString(result));
            Assert.Equal(2, PdfDocumentInformation.Read(PdfDocument.Open(result)).PageCount);
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
            if (File.Exists(destinationPath)) File.Delete(destinationPath);
        }
    }

    [Fact]
    public void MergeDocuments_PreservesFirstPrefixAndImportsCompleteDocuments()
    {
        byte[] first = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBookmark("First", 0)
            .Build();
        byte[] second = new PdfDocumentBuilder()
            .AddBlankPage()
            .AddBlankPage()
            .AddBookmark("Second", 1)
            .Build();

        byte[] merged = PdfEngineIntegration.MergeDocuments([first, second]);

        Assert.True(merged.AsSpan(0, first.Length).SequenceEqual(first));
        Assert.Equal(3, PdfDocumentInformation.Read(PdfDocument.Open(merged)).PageCount);
    }

    [Fact]
    public void ExtractPages_UsesRequestedPageOrderAndReturnsIndependentDocument()
    {
        byte[] source = new PdfDocumentBuilder()
            .AddPage(100, 200, ReadOnlyMemory<byte>.Empty)
            .AddPage(100, 300, ReadOnlyMemory<byte>.Empty)
            .AddPage(100, 400, ReadOnlyMemory<byte>.Empty)
            .Build();

        byte[] extracted = PdfEngineIntegration.ExtractPages(source, [2, 0]);
        Assert.Equal(2, PdfDocumentInformation.Read(PdfDocument.Open(extracted)).PageCount);
    }

    [Fact]
    public void SplitPages_ReturnsOneValidDocumentPerPage()
    {
        byte[] source = new PdfDocumentBuilder()
            .AddPage(100, 200, ReadOnlyMemory<byte>.Empty)
            .AddPage(100, 300, ReadOnlyMemory<byte>.Empty)
            .AddPage(100, 400, ReadOnlyMemory<byte>.Empty)
            .Build();

        IReadOnlyList<byte[]> pages = PdfEngineIntegration.SplitPages(source);

        Assert.Equal(3, pages.Count);
        Assert.All(pages, page => Assert.Equal(
            1, PdfDocumentInformation.Read(PdfDocument.Open(page)).PageCount));
    }

    [Fact]
    public void ValidateDocument_RejectsSourceWithoutTrailer()
    {
        string path = Path.Combine(Path.GetTempPath(), $"killerpdf-invalid-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllText(path, "%PDF-1.7\n1 0 obj\n<<>>\nendobj\n");

            Assert.ThrowsAny<Exception>(() => PdfEngineIntegration.ValidateDocument(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DuplicatePage_DeepCopiesPageAtFollowingPosition()
    {
        string path = Path.Combine(Path.GetTempPath(), $"killerpdf-duplicate-{Guid.NewGuid():N}.pdf");
        try
        {
            byte[] source = new PdfDocumentBuilder()
                .AddBlankPage(100, 200)
                .AddBlankPage(300, 400)
                .Build();
            File.WriteAllBytes(path, source);

            PdfEngineIntegration.DuplicatePage(path, 0);

            byte[] result = File.ReadAllBytes(path);
            Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
            PdfDocument reopened = PdfDocument.Open(result);
            Assert.Equal(3, PageCount(reopened));
            Assert.Equal([0d, 0d, 100d, 200d], PageMediaBox(reopened, 0));
            Assert.Equal([0d, 0d, 100d, 200d], PageMediaBox(reopened, 1));
            Assert.Equal([0d, 0d, 300d, 400d], PageMediaBox(reopened, 2));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RemapRotationsAfterPageDuplication_CopiesSourceRotation()
    {
        var rotations = new Dictionary<int, int> { [0] = 90, [1] = 270 };

        PdfEngineIntegration.RemapRotationsAfterPageDuplication(rotations, 0);

        Assert.Equal(new Dictionary<int, int> { [0] = 90, [1] = 90, [2] = 270 }, rotations);
    }

    [Fact]
    public void ReplacePage_ImportsReplacementAtSamePositionAndKeepsPageCount()
    {
        string path = Path.Combine(Path.GetTempPath(), $"killerpdf-replace-{Guid.NewGuid():N}.pdf");
        string replacementPath = Path.Combine(Path.GetTempPath(), $"killerpdf-replacement-{Guid.NewGuid():N}.pdf");
        try
        {
            byte[] source = new PdfDocumentBuilder()
                .AddBlankPage(100, 200).SetPageRotation(0, 90)
                .AddBlankPage(200, 300).SetPageRotation(1, 180)
                .AddBlankPage(300, 400).SetPageRotation(2, 270)
                .Build();
            File.WriteAllBytes(path, source);
            File.WriteAllBytes(replacementPath, new PdfDocumentBuilder()
                .AddBlankPage(612, 792).SetPageRotation(0, 90)
                .Build());

            PdfEngineIntegration.ReplacePage(path, 1, replacementPath);

            byte[] result = File.ReadAllBytes(path);
            Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
            PdfDocument reopened = PdfDocument.Open(result);
            Assert.Equal(3, PageCount(reopened));
            Assert.Equal([0d, 0d, 100d, 200d], PageMediaBox(reopened, 0));
            Assert.Equal([0d, 0d, 612d, 792d], PageMediaBox(reopened, 1));
            Assert.Equal(0, PageRotation(reopened, 1));
            Assert.Equal([0d, 0d, 300d, 400d], PageMediaBox(reopened, 2));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(replacementPath)) File.Delete(replacementPath);
        }
    }

    [Fact]
    public void RemapRotationsAfterPageReplacement_ResetsOnlyReplacementPage()
    {
        var rotations = new Dictionary<int, int> { [0] = 90, [1] = 180, [2] = 270 };

        PdfEngineIntegration.RemapRotationsAfterPageReplacement(rotations, 1);

        Assert.Equal(new Dictionary<int, int> { [0] = 90, [1] = 0, [2] = 270 }, rotations);
    }

    [Fact]
    public void ExtractPages_WritesSelectedOrderWithEffectiveRotations()
    {
        string sourcePath = Path.Combine(Path.GetTempPath(), $"killerpdf-extract-source-{Guid.NewGuid():N}.pdf");
        string destinationPath = Path.Combine(Path.GetTempPath(), $"killerpdf-extract-output-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(sourcePath, new PdfDocumentBuilder()
                .AddBlankPage(100, 200)
                .AddBlankPage(200, 300)
                .AddBlankPage(300, 400)
                .Build());

            PdfEngineIntegration.ExtractPages(sourcePath, destinationPath, [2, 0],
                new Dictionary<int, int> { [0] = 90, [1] = 180, [2] = 270 });

            PdfDocument reopened = PdfDocument.Open(File.ReadAllBytes(destinationPath));
            Assert.Equal(2, PageCount(reopened));
            Assert.Equal([0d, 0d, 300d, 400d], PageMediaBox(reopened, 0));
            Assert.Equal(270, PageRotation(reopened, 0));
            Assert.Equal([0d, 0d, 100d, 200d], PageMediaBox(reopened, 1));
            Assert.Equal(90, PageRotation(reopened, 1));
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
            if (File.Exists(destinationPath)) File.Delete(destinationPath);
        }
    }

    [Fact]
    public void AppendDocuments_MergesCompleteSourcesAndNormalizesStoredRotations()
    {
        string targetPath = Path.Combine(Path.GetTempPath(), $"killerpdf-merge-target-{Guid.NewGuid():N}.pdf");
        string sourcePath = Path.Combine(Path.GetTempPath(), $"killerpdf-merge-source-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(targetPath, new PdfDocumentBuilder().AddBlankPage(100, 200).Build());
            File.WriteAllBytes(sourcePath, new PdfDocumentBuilder()
                .AddBlankPage(200, 300).SetPageRotation(0, 90)
                .AddBlankPage(300, 400).SetPageRotation(1, 270)
                .Build());
            var imports = new[]
            {
                new PdfEngineIntegration.ImportedDocument(sourcePath, [90, 270])
            };

            PdfEngineIntegration.AppendDocuments(targetPath, imports);

            PdfDocument reopened = PdfDocument.Open(File.ReadAllBytes(targetPath));
            Assert.Equal(3, PageCount(reopened));
            Assert.Equal([0d, 0d, 200d, 300d], PageMediaBox(reopened, 1));
            Assert.Equal(0, PageRotation(reopened, 1));
            Assert.Equal([0d, 0d, 300d, 400d], PageMediaBox(reopened, 2));
            Assert.Equal(0, PageRotation(reopened, 2));

            var rotations = new Dictionary<int, int> { [0] = 180 };
            PdfEngineIntegration.RemapRotationsAfterDocumentAppend(rotations, imports);
            Assert.Equal(new Dictionary<int, int>
            {
                [0] = 180, [1] = 90, [2] = 270
            }, rotations);
        }
        finally
        {
            if (File.Exists(targetPath)) File.Delete(targetPath);
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
        }
    }

    [Fact]
    public void InsertBlankPage_AddsA4PageAtRequestedPosition()
    {
        string path = Path.Combine(Path.GetTempPath(), $"killerpdf-insert-{Guid.NewGuid():N}.pdf");
        try
        {
            byte[] source = new PdfDocumentBuilder()
                .AddBlankPage(100, 200).SetPageRotation(0, 90)
                .AddBlankPage(300, 400).SetPageRotation(1, 270)
                .Build();
            File.WriteAllBytes(path, source);

            PdfEngineIntegration.InsertBlankPage(path, 1, 595, 842);

            byte[] result = File.ReadAllBytes(path);
            Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
            PdfDocument reopened = PdfDocument.Open(result);
            Assert.Equal(3, PageCount(reopened));
            Assert.Equal([0d, 0d, 100d, 200d], PageMediaBox(reopened, 0));
            Assert.Equal([0d, 0d, 595d, 842d], PageMediaBox(reopened, 1));
            Assert.Equal([0d, 0d, 300d, 400d], PageMediaBox(reopened, 2));
            Assert.Equal(270, PageRotation(reopened, 2));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RemapRotationsAfterPageInsertion_ShiftsPagesAndAddsZeroRotation()
    {
        var rotations = new Dictionary<int, int>
        {
            [0] = 90,
            [1] = 180,
            [2] = 270
        };

        PdfEngineIntegration.RemapRotationsAfterPageInsertion(rotations, 1);

        Assert.Equal(new Dictionary<int, int>
        {
            [0] = 90,
            [1] = 0,
            [2] = 180,
            [3] = 270
        }, rotations);
    }

    [Fact]
    public void MovePage_ReordersPagesAndPreservesTheirRotation()
    {
        string path = Path.Combine(Path.GetTempPath(), $"killerpdf-move-{Guid.NewGuid():N}.pdf");
        try
        {
            byte[] source = new PdfDocumentBuilder()
                .AddBlankPage(100, 200).SetPageRotation(0, 90)
                .AddBlankPage(200, 300).SetPageRotation(1, 180)
                .AddBlankPage(300, 400).SetPageRotation(2, 270)
                .Build();
            File.WriteAllBytes(path, source);

            PdfEngineIntegration.MovePage(path, 0, 2);

            byte[] result = File.ReadAllBytes(path);
            Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
            PdfDocument reopened = PdfDocument.Open(result);
            Assert.Equal([0d, 0d, 200d, 300d], PageMediaBox(reopened, 0));
            Assert.Equal(180, PageRotation(reopened, 0));
            Assert.Equal([0d, 0d, 300d, 400d], PageMediaBox(reopened, 1));
            Assert.Equal(270, PageRotation(reopened, 1));
            Assert.Equal([0d, 0d, 100d, 200d], PageMediaBox(reopened, 2));
            Assert.Equal(90, PageRotation(reopened, 2));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RemapRotationsAfterPageMove_MovesRotationWithPage()
    {
        var rotations = new Dictionary<int, int>
        {
            [0] = 90,
            [1] = 180,
            [2] = 270,
            [3] = 0
        };

        PdfEngineIntegration.RemapRotationsAfterPageMove(rotations, 0, 2);

        Assert.Equal(new Dictionary<int, int>
        {
            [0] = 180,
            [1] = 270,
            [2] = 90,
            [3] = 0
        }, rotations);
    }

    [Fact]
    public void RemovePages_DeletesSelectedPagesAndPreservesRetainedRotations()
    {
        string path = Path.Combine(Path.GetTempPath(), $"killerpdf-delete-{Guid.NewGuid():N}.pdf");
        try
        {
            byte[] source = new PdfDocumentBuilder()
                .AddBlankPage(100, 200).SetPageRotation(0, 90)
                .AddBlankPage(200, 300).SetPageRotation(1, 180)
                .AddBlankPage(300, 400).SetPageRotation(2, 270)
                .AddBlankPage(400, 500)
                .Build();
            File.WriteAllBytes(path, source);

            PdfEngineIntegration.RemovePages(path, [2, 0]);

            byte[] result = File.ReadAllBytes(path);
            Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
            PdfDocument reopened = PdfDocument.Open(result);
            Assert.Equal(2, PageCount(reopened));
            Assert.Equal([0d, 0d, 200d, 300d], PageMediaBox(reopened, 0));
            Assert.Equal(180, PageRotation(reopened, 0));
            Assert.Equal([0d, 0d, 400d, 500d], PageMediaBox(reopened, 1));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RemapRotationsAfterPageRemoval_DropsDeletedEntriesAndRenumbersSurvivors()
    {
        var rotations = new Dictionary<int, int>
        {
            [0] = 90,
            [1] = 180,
            [2] = 270,
            [3] = 0,
            [4] = 90
        };

        PdfEngineIntegration.RemapRotationsAfterPageRemoval(rotations, [3, 1]);

        Assert.Equal(new Dictionary<int, int>
        {
            [0] = 90,
            [1] = 270,
            [2] = 90
        }, rotations);
    }

    [Fact]
    public void ApplyCropBoxes_WritesMatchingCropAndTrimBoxesIncrementally()
    {
        string path = Path.Combine(Path.GetTempPath(), $"killerpdf-crop-{Guid.NewGuid():N}.pdf");
        try
        {
            byte[] source = new PdfDocumentBuilder()
                .AddBlankPage(200, 300)
                .SetPageRotation(0, 90)
                .AddBlankPage(400, 500)
                .Build();
            File.WriteAllBytes(path, source);

            PdfEngineIntegration.ApplyCropBoxes(path,
                new Dictionary<int, PdfEngineIntegration.PageRectangle?>
                {
                    [0] = new(10, 20, 150, 240),
                    [1] = new(25, 30, 300, 400)
                });

            byte[] result = File.ReadAllBytes(path);
            Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
            PdfDocument reopened = PdfDocument.Open(result);
            Assert.Equal([10d, 20d, 160d, 260d], PageBox(reopened, 0, "CropBox"));
            Assert.Equal([10d, 20d, 160d, 260d], PageBox(reopened, 0, "TrimBox"));
            Assert.Equal(90, PageRotation(reopened, 0));
            Assert.Equal([25d, 30d, 325d, 430d], PageBox(reopened, 1, "CropBox"));
            Assert.Equal([25d, 30d, 325d, 430d], PageBox(reopened, 1, "TrimBox"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ApplyCropBoxes_WithNullRectangleRemovesCropAndTrimBoxes()
    {
        string path = Path.Combine(Path.GetTempPath(), $"killerpdf-crop-{Guid.NewGuid():N}.pdf");
        try
        {
            byte[] source = new PdfDocumentBuilder()
                .AddBlankPage(200, 300)
                .SetPageBox(0, PdfPageBox.Crop, 10, 10, 180, 280)
                .SetPageBox(0, PdfPageBox.Trim, 20, 20, 160, 260)
                .Build();
            File.WriteAllBytes(path, source);

            PdfEngineIntegration.ApplyCropBoxes(path,
                new Dictionary<int, PdfEngineIntegration.PageRectangle?> { [0] = null });

            PdfDocument reopened = PdfDocument.Open(File.ReadAllBytes(path));
            PdfDictionary page = Page(reopened, 0);
            Assert.False(page.ContainsKey(new PdfName("CropBox"u8)));
            Assert.False(page.ContainsKey(new PdfName("TrimBox"u8)));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ApplyPageRotations_WritesFinalIncrementalRotationRevision()
    {
        string path = Path.Combine(Path.GetTempPath(), $"killerpdf-rotation-{Guid.NewGuid():N}.pdf");
        try
        {
            byte[] source = new PdfDocumentBuilder()
                .AddBlankPage()
                .AddBlankPage()
                .Build();
            File.WriteAllBytes(path, source);

            PdfEngineIntegration.ApplyPageRotations(path, new Dictionary<int, int>
            {
                [0] = 90,
                [1] = 270
            });

            byte[] result = File.ReadAllBytes(path);
            Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
            PdfDocument reopened = PdfDocument.Open(result);
            Assert.Equal(90, PageRotation(reopened, 0));
            Assert.Equal(270, PageRotation(reopened, 1));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ApplyDocumentMetadata_WritesCompleteMetadataAndPreservesPrefix()
    {
        string path = Path.Combine(Path.GetTempPath(), $"killerpdf-metadata-{Guid.NewGuid():N}.pdf");
        try
        {
            byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
            File.WriteAllBytes(path, source);
            var metadata = new PdfDocumentMetadata
            {
                Title = "Updated title",
                Author = "Steve",
                Subject = "The KillerPDF.Engine",
                Keywords = "PDF 2.0, PDF/A",
                Creator = "KillerPDF",
                Producer = "Original producer",
                Language = "en-US",
                CreationDate = new DateTimeOffset(2026, 8, 24, 10, 11, 12, TimeSpan.FromHours(-7)),
                ModificationDate = new DateTimeOffset(2026, 8, 24, 11, 12, 13, TimeSpan.Zero),
                Trapped = PdfTrappedStatus.False
            };

            PdfEngineIntegration.ApplyDocumentMetadata(path, metadata);

            byte[] result = File.ReadAllBytes(path);
            Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
            var info = PdfDocumentInformation.Read(PdfDocument.Open(result));
            Assert.Equal(metadata.Title, info.Title);
            Assert.Equal(metadata.Author, info.Author);
            Assert.Equal(metadata.Subject, info.Subject);
            Assert.Equal(metadata.Keywords, info.Keywords);
            Assert.Equal(metadata.Creator, info.Creator);
            Assert.Equal(metadata.Producer, info.Producer);
            Assert.Equal(metadata.Language, info.Language);
            Assert.Equal(metadata.CreationDate, info.CreationDate);
            Assert.Equal(metadata.ModificationDate, info.ModificationDate);
            Assert.Equal(metadata.Trapped, info.Trapped);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ApplyPageRotations_WithNoApplicationRotations_LeavesFileUntouched()
    {
        string path = Path.Combine(Path.GetTempPath(), $"killerpdf-rotation-{Guid.NewGuid():N}.pdf");
        try
        {
            byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
            File.WriteAllBytes(path, source);

            PdfEngineIntegration.ApplyPageRotations(path, new Dictionary<int, int>());

            Assert.Equal(source, File.ReadAllBytes(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ApplyPageRotations_WithInvalidPageIndex_PreservesOriginalFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"killerpdf-rotation-{Guid.NewGuid():N}.pdf");
        try
        {
            byte[] source = new PdfDocumentBuilder().AddBlankPage().Build();
            File.WriteAllBytes(path, source);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PdfEngineIntegration.ApplyPageRotations(path, new Dictionary<int, int> { [1] = 90 }));

            Assert.Equal(source, File.ReadAllBytes(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static long PageRotation(PdfDocument document, int pageIndex)
    {
        return Assert.IsType<PdfInteger>(Page(document, pageIndex)[new PdfName("Rotate"u8)]).Value;
    }

    private static double[] PageBox(PdfDocument document, int pageIndex, string name)
    {
        PdfName key = name switch
        {
            "CropBox" => new PdfName("CropBox"u8),
            "TrimBox" => new PdfName("TrimBox"u8),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        PdfArray box = Assert.IsType<PdfArray>(Page(document, pageIndex)[key]);
        return box.Select(item => item switch
        {
            PdfInteger integer => (double)integer.Value,
            PdfReal real => real.Value,
            _ => throw new Xunit.Sdk.XunitException("Page box contains a nonnumeric value.")
        }).ToArray();
    }

    private static double[] PageMediaBox(PdfDocument document, int pageIndex)
    {
        PdfArray box = Assert.IsType<PdfArray>(
            Page(document, pageIndex)[new PdfName("MediaBox"u8)]);
        return box.Select(item => item switch
        {
            PdfInteger integer => (double)integer.Value,
            PdfReal real => real.Value,
            _ => throw new Xunit.Sdk.XunitException("Media box contains a nonnumeric value.")
        }).ToArray();
    }

    private static int PageCount(PdfDocument document)
    {
        PdfDictionary catalog = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(document.Trailer[new PdfName("Root"u8)])));
        PdfDictionary pages = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(catalog[new PdfName("Pages"u8)])));
        return checked((int)Assert.IsType<PdfInteger>(pages[new PdfName("Count"u8)]).Value);
    }

    private static PdfDictionary Page(PdfDocument document, int pageIndex)
    {
        PdfDictionary catalog = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(document.Trailer[new PdfName("Root"u8)])));
        PdfDictionary pages = Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(catalog[new PdfName("Pages"u8)])));
        PdfArray kids = Assert.IsType<PdfArray>(pages[new PdfName("Kids"u8)]);
        return Assert.IsType<PdfDictionary>(document.Resolve(
            Assert.IsType<PdfIndirectReference>(kids[pageIndex])));
    }
}
