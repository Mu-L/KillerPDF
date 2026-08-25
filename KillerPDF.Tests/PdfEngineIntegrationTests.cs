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
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace KillerPDF.Tests;

public sealed class PdfEngineIntegrationTests
{
    [Fact]
    public void CreateBlankDocument_AuthorsOneA4Page()
    {
        byte[] result = PdfEngineIntegration.CreateBlankDocument();

        IReadOnlyList<PdfPageInformation> pages =
            PdfPageInformation.Read(PdfDocument.Open(result));
        Assert.Single(pages);
        Assert.Equal(595, pages[0].Width);
        Assert.Equal(842, pages[0].Height);
    }

    [Fact]
    public void RebuildDocument_PreservesPagesAndOptionallyStripsRotations()
    {
        string input = Path.Combine(Path.GetTempPath(),
            $"killerpdf-rebuild-input-{Guid.NewGuid():N}.pdf");
        string preserved = Path.Combine(Path.GetTempPath(),
            $"killerpdf-rebuild-preserved-{Guid.NewGuid():N}.pdf");
        string stripped = Path.Combine(Path.GetTempPath(),
            $"killerpdf-rebuild-stripped-{Guid.NewGuid():N}.pdf");
        try
        {
            PdfDocument authored = PdfDocument.Open(new PdfDocumentBuilder()
                .SetMetadata(new PdfDocumentMetadata { Title = "Repair fixture" })
                .AddBlankPage(200, 300).AddBlankPage(400, 500).Build());
            File.WriteAllBytes(input, new PdfIncrementalPageEditor(authored)
                .SetRotation(0, 90).SetRotation(1, 270).Build());

            PdfEngineIntegration.RebuildDocument(input, preserved);
            PdfEngineIntegration.RebuildDocument(input, stripped, stripRotations: true);

            IReadOnlyList<PdfPageInformation> preservedPages =
                PdfPageInformation.Read(PdfDocument.Open(File.ReadAllBytes(preserved)));
            IReadOnlyList<PdfPageInformation> strippedPages =
                PdfPageInformation.Read(PdfDocument.Open(File.ReadAllBytes(stripped)));
            Assert.Equal([90, 270], preservedPages.Select(page => page.Rotation));
            Assert.Equal([0, 0], strippedPages.Select(page => page.Rotation));
            Assert.Equal([200d, 400d], preservedPages.Select(page => page.Width));
        }
        finally
        {
            foreach (string path in new[] { input, preserved, stripped })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ResaveDocument_WritesDeterministicEngineOutput()
    {
        string input = Path.Combine(Path.GetTempPath(),
            $"killerpdf-resave-input-{Guid.NewGuid():N}.pdf");
        string first = Path.Combine(Path.GetTempPath(),
            $"killerpdf-resave-first-{Guid.NewGuid():N}.pdf");
        string second = Path.Combine(Path.GetTempPath(),
            $"killerpdf-resave-second-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(input, new PdfDocumentBuilder()
                .SetMetadata(new PdfDocumentMetadata { Title = "Batch fixture" })
                .AddBlankPage(200, 300).Build());

            PdfEngineIntegration.ResaveDocument(input, first);
            PdfEngineIntegration.ResaveDocument(input, second);

            Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
            IReadOnlyList<PdfPageInformation> pages = PdfPageInformation.Read(
                PdfDocument.Open(File.ReadAllBytes(first)));
            Assert.Single(pages);
            Assert.Equal(200, pages[0].Width);
            Assert.Equal(300, pages[0].Height);
        }
        finally
        {
            foreach (string path in new[] { input, first, second })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RemoveAnnotation_RemovesOnlySelectedNativeAnnotation()
    {
        string path = Path.Combine(Path.GetTempPath(),
            $"killerpdf-remove-annotation-{Guid.NewGuid():N}.pdf");
        try
        {
            byte[] source = new PdfDocumentBuilder().AddBlankPage(200, 300)
                .AddUriLink(0, 10, 10, 50, 20, "https://example.com/first")
                .AddUriLink(0, 10, 40, 50, 20, "https://example.com/second")
                .Build();
            File.WriteAllBytes(path, source);

            PdfEngineIntegration.RemoveAnnotation(path, 0, 0);

            byte[] result = File.ReadAllBytes(path);
            PdfDocument reopened = PdfDocument.Open(result);
            PdfArray annotations = Assert.IsType<PdfArray>(
                Page(reopened, 0)[new PdfName("Annots"u8)]);
            Assert.Single(annotations);
            Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RemapRotationsAfterPageTurns_UpdatesOnlyDistinctSelectedPages()
    {
        var rotations = new Dictionary<int, int>
        {
            [0] = 0,
            [1] = 90,
            [2] = 270,
        };

        PdfEngineIntegration.RemapRotationsAfterPageTurns(
            rotations, [0, 2, 2], 90);

        Assert.Equal(90, rotations[0]);
        Assert.Equal(90, rotations[1]);
        Assert.Equal(0, rotations[2]);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PdfEngineIntegration.RemapRotationsAfterPageTurns(
                rotations, [3], 90));
    }

    [Fact]
    public void AddSearchableTextLayers_WritesExtractableMultiscriptUnicode()
    {
        string input = Path.Combine(Path.GetTempPath(),
            $"killerpdf-ocr-input-{Guid.NewGuid():N}.pdf");
        string output = Path.Combine(Path.GetTempPath(),
            $"killerpdf-ocr-output-{Guid.NewGuid():N}.pdf");
        try
        {
            byte[] source = new PdfDocumentBuilder().AddBlankPage(300, 400).Build();
            File.WriteAllBytes(input, source);
            var words = new[]
            {
                new PdfEngineIntegration.SearchableWord("Hello", 10, 10, 80, 30),
                new PdfEngineIntegration.SearchableWord("বাংলা", 10, 40, 100, 65),
                new PdfEngineIntegration.SearchableWord("日本語", 10, 75, 100, 100),
                new PdfEngineIntegration.SearchableWord("中文", 10, 110, 80, 135),
            };

            int count = PdfEngineIntegration.AddSearchableTextLayers(
                input, output,
                [new PdfEngineIntegration.SearchablePage(300, 400, words)]);

            Assert.Equal(4, count);
            Assert.True(File.ReadAllBytes(output).AsSpan(0, source.Length).SequenceEqual(source));
            using PigDocument extracted = PigDocument.Open(output);
            string text = extracted.GetPage(1).Text;
            Assert.Contains("Hello", text);
            Assert.Contains("বাংলা", text);
            Assert.Contains("日本語", text);
            Assert.Contains("中文", text);
        }
        finally
        {
            if (File.Exists(input)) File.Delete(input);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public void AddSearchableTextLayers_HandlesEveryNativePageRotation()
    {
        string input = Path.Combine(Path.GetTempPath(),
            $"killerpdf-ocr-rotated-input-{Guid.NewGuid():N}.pdf");
        string output = Path.Combine(Path.GetTempPath(),
            $"killerpdf-ocr-rotated-output-{Guid.NewGuid():N}.pdf");
        try
        {
            PdfDocument authored = PdfDocument.Open(new PdfDocumentBuilder()
                .AddBlankPage(300, 400).AddBlankPage(300, 400)
                .AddBlankPage(300, 400).AddBlankPage(300, 400).Build());
            byte[] source = new PdfIncrementalPageEditor(authored)
                .SetRotation(0, 0).SetRotation(1, 90)
                .SetRotation(2, 180).SetRotation(3, 270).Build();
            File.WriteAllBytes(input, source);
            var layers = Enumerable.Range(0, 4).Select(index =>
                new PdfEngineIntegration.SearchablePage(
                    index % 2 == 0 ? 300 : 400,
                    index % 2 == 0 ? 400 : 300,
                    [new PdfEngineIntegration.SearchableWord(
                        $"Rotation{index}", 10, 10, 120, 35)])).ToArray();

            Assert.Equal(4, PdfEngineIntegration.AddSearchableTextLayers(
                input, output, layers));

            using PigDocument extracted = PigDocument.Open(output);
            for (int index = 0; index < 4; index++)
                Assert.Contains($"Rotation{index}", extracted.GetPage(index + 1).Text);
        }
        finally
        {
            if (File.Exists(input)) File.Delete(input);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public void ApplyFormValues_WritesAllDesktopFieldTypesInOneRevision()
    {
        string path = Path.Combine(Path.GetTempPath(), $"killerpdf-forms-{Guid.NewGuid():N}.pdf");
        try
        {
            byte[] source = new PdfDocumentBuilder()
                .AddBlankPage()
                .AddBlankPage()
                .AddTextField(0, "customer.name", 20, 20, 140, 24, "Original")
                .AddComboBoxOptions(0, "customer.country", 20, 60, 140, 24, [
                    new PdfChoiceOption("US", "United States"),
                    new PdfChoiceOption("CA", "Canada")], "US")
                .AddCheckBox(0, "customer.approved", 20, 100, 20, 20)
                .AddRadioGroup("customer.plan", [
                    new PdfRadioButtonOption(0, 20, 140, 20, 20, "Free"),
                    new PdfRadioButtonOption(1, 20, 20, 20, 20, "Pro")], "Free")
                .Build();
            File.WriteAllBytes(path, source);

            PdfEngineIntegration.ApplyFormValues(path, new PdfEngineIntegration.FormEdits(
                new Dictionary<string, string> { ["customer.name"] = "Updated" },
                new Dictionary<string, string> { ["customer.country"] = "CA" },
                new Dictionary<string, bool> { ["customer.approved"] = true },
                new Dictionary<string, string> { ["customer.plan"] = "/Pro" },
                new Dictionary<string, double> { ["customer.name"] = 7.5 }));

            byte[] result = File.ReadAllBytes(path);
            Assert.True(result.AsSpan(0, source.Length).SequenceEqual(source));
            Assert.Equal(2, PdfDocumentInformation.Read(PdfDocument.Open(result)).PageCount);
            string syntax = System.Text.Encoding.Latin1.GetString(result);
            Assert.Contains("/V /Pro", syntax);
            Assert.Contains("7.5 Tf", syntax);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

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
    public void MergeFiles_ComposesPdfAndImageInputsInOriginalOrder()
    {
        string pdfPath = Path.Combine(Path.GetTempPath(), $"killerpdf-merge-{Guid.NewGuid():N}.pdf");
        string imagePath = Path.Combine(Path.GetTempPath(), $"killerpdf-merge-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(pdfPath, new PdfDocumentBuilder()
                .AddPage(200, 300, ReadOnlyMemory<byte>.Empty)
                .AddBookmark("PDF page", 0)
                .Build());
            using (var bitmap = new System.Drawing.Bitmap(40, 20))
            {
                bitmap.SetResolution(72, 72);
                using System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap);
                graphics.Clear(System.Drawing.Color.CornflowerBlue);
                bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
            }

            byte[] merged = PdfEngineIntegration.MergeFiles([pdfPath, imagePath]);

            PdfDocument document = PdfDocument.Open(merged);
            byte[] pdfSource = File.ReadAllBytes(pdfPath);
            Assert.True(merged.AsSpan(0, pdfSource.Length).SequenceEqual(pdfSource));
            Assert.Equal(2, PdfDocumentInformation.Read(document).PageCount);
            string syntax = System.Text.Encoding.Latin1.GetString(merged);
            Assert.Contains("/Subtype /Image", syntax);
            Assert.Contains("/Outlines", syntax);
        }
        finally
        {
            if (File.Exists(pdfPath)) File.Delete(pdfPath);
            if (File.Exists(imagePath)) File.Delete(imagePath);
        }
    }

    [Fact]
    public void CreateRasterDocument_AuthorsBgraPagesWithRequestedPointSizes()
    {
        byte[] result = PdfEngineIntegration.CreateRasterDocument([
            new PdfEngineIntegration.RasterPage(2, 1, 144, 72,
                new byte[] { 30, 20, 10, 255, 60, 50, 40, 128 }),
            new PdfEngineIntegration.RasterPage(1, 1, 72, 144,
                new byte[] { 90, 80, 70, 255 })]);

        Assert.Equal(2, PdfDocumentInformation.Read(PdfDocument.Open(result)).PageCount);
        string syntax = System.Text.Encoding.Latin1.GetString(result);
        Assert.Contains("/MediaBox [0 0 144 72]", syntax);
        Assert.Contains("/MediaBox [0 0 72 144]", syntax);
        Assert.Contains("/SMask", syntax);
    }

    [Fact]
    public void MergeReadableFiles_SkipsInvalidFolderImportEntries()
    {
        string validPath = Path.Combine(Path.GetTempPath(), $"killerpdf-readable-{Guid.NewGuid():N}.pdf");
        string invalidPath = Path.Combine(Path.GetTempPath(), $"killerpdf-invalid-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(validPath, new PdfDocumentBuilder().AddBlankPage().Build());
            File.WriteAllText(invalidPath, "not a PDF");

            byte[] result = PdfEngineIntegration.MergeReadableFiles([invalidPath, validPath]);

            Assert.Equal(1, PdfDocumentInformation.Read(PdfDocument.Open(result)).PageCount);
        }
        finally
        {
            if (File.Exists(validPath)) File.Delete(validPath);
            if (File.Exists(invalidPath)) File.Delete(invalidPath);
        }
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
