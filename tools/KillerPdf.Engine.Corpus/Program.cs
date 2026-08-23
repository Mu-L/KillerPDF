using System.Diagnostics;
using KillerPdf.Engine.Validation;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Fonts;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Writing;
using KillerPdf.Engine.Editing;
using KillerPdf.Engine.Signing;

if (args.Length == 2 && args[0] == "--font-info")
{
    TrueTypeFont font = TrueTypeFont.Load(File.ReadAllBytes(args[1]));
    Console.WriteLine($"{font.PostScriptName}: {font.GlyphCount:N0} glyphs, {font.UnitsPerEm} UPM");
    Console.WriteLine($"Embedding: {font.EmbeddingAllowed}; subsetting: {font.SubsettingAllowed}; U+0041 -> {font.GetGlyphId('A')}");
    return 0;
}

if (args.Length == 3 && args[0] == "--unicode-smoke")
{
    TrueTypeFont font = TrueTypeFont.Load(File.ReadAllBytes(args[1]));
    var content = new PdfContentStreamBuilder()
        .BeginText()
        .SetFont(font, 24)
        .MoveText(72, 720)
        .ShowUnicodeText("KillerPDF café Ω")
        .EndText();
    byte[] pdf = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata { Title = "KillerPDF Unicode smoke test", Language = "en-US" })
        .AddPage(612, 792, content).Build();
    string destination = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} bytes with embedded {font.PostScriptName} to {destination}");
    return 0;
}

if (args.Length == 4 && args[0] == "--text-state-smoke")
{
    TrueTypeFont font = TrueTypeFont.Load(File.ReadAllBytes(args[1]));
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[2]));
    string destination = Path.GetFullPath(args[3]);
    var content = new PdfContentStreamBuilder()
        .BeginMarkedContent(PdfStructureType.Paragraph, 0)
        .BeginText().SetFont(font, 28)
        .SetTextMatrix(0.966, 0.259, -0.259, 0.966, 90, 650)
        .SetCharacterSpacing(0.3).SetWordSpacing(1.2).SetHorizontalTextScale(94)
        .SetTextRise(2).SetTextRenderingMode(PdfTextRenderingMode.Fill)
        .ShowPositionedUnicodeText(["Killer", "PDF"], [-45])
        .SetTextLeading(40).MoveToNextTextLine().SetTextRise(0)
        .ShowUnicodeText("positioned text")
        .EndText().EndMarkedContent();
    byte[] pdf = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata
        {
            Title = "KillerPDF positioned text smoke test",
            Language = "en-US"
        })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4Conformance().EnablePdfUa2Conformance()
        .AddPage(612, 792, content)
        .AddStructureContainer(PdfStructureType.Document)
        .AddStructureElement(PdfStructureType.Paragraph, 0, 0, 1)
        .Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte positioned-text PDF to {destination}");
    return 0;
}

if (args.Length == 3 && args[0] == "--image-smoke")
{
    PdfImage image = PdfImage.FromJpeg(File.ReadAllBytes(args[1]));
    double width = 468;
    double height = width * image.Height / image.Width;
    var content = new PdfContentStreamBuilder().DrawImage(image, 72, 720 - height, width, height);
    byte[] pdf = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata { Title = "KillerPDF image smoke test", Language = "en-US" })
        .AddPage(612, 792, content).Build();
    string destination = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} bytes with {image.Width}x{image.Height} JPEG to {destination}");
    return 0;
}

if (args.Length == 3 && args[0] == "--output-intent-smoke")
{
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[1]));
    var content = new PdfContentStreamBuilder()
        .SetFillRgb(0.9, 0.2, 0.4).Rectangle(72, 600, 240, 100).Fill();
    byte[] pdf = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata { Title = "KillerPDF output intent smoke test", Language = "en-US" })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4Conformance()
        .AddPage(612, 792, content)
        .Build();
    string destination = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} bytes with {profile.ColorSpace} output intent to {destination}");
    return 0;
}

if (args.Length == 3 && args[0] == "--cmyk-smoke")
{
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[1]));
    if (profile.ColorSpace != "CMYK")
        throw new ArgumentException("The CMYK smoke test requires a CMYK ICC profile.");
    string destination = Path.GetFullPath(args[2]);
    var stencil = new PdfTilingPattern(18, 18,
        new PdfContentStreamBuilder().Rectangle(2, 2, 7, 7).Fill(),
        paintType: PdfTilingPatternPaintType.Uncolored);
    var content = new PdfContentStreamBuilder()
        .BeginMarkedContent(PdfStructureType.Figure, 0)
        .SetRenderingIntent(PdfRenderingIntent.RelativeColorimetric)
        .SetFlatnessTolerance(0.75)
        .SetFillCmyk(0.85, 0.2, 0, 0.1).Rectangle(72, 540, 210, 150).Fill()
        .SetFillPattern(stencil, new PdfCmykColor(0, 0.8, 0.9, 0.05))
        .Rectangle(330, 540, 210, 150).Fill()
        .EndMarkedContent();
    byte[] pdf = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata
        {
            Title = "KillerPDF CMYK authoring smoke test",
            Language = "en-US"
        })
        .SetOutputIntent(profile, "CMYK press profile")
        .EnablePdfA4Conformance()
        .EnablePdfUa2Conformance()
        .AddPage(612, 792, content)
        .AddStructureContainer(PdfStructureType.Document)
        .AddStructureElement(PdfStructureType.Figure, 0, 0, 1,
            alternateDescription: "CMYK solid and stencil-pattern samples")
        .Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte CMYK PDF to {destination}");
    return 0;
}

if (args.Length == 3 && args[0] == "--pdfa4f-attachment-smoke")
{
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[1]));
    string destination = Path.GetFullPath(args[2]);
    byte[] pdf = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata
        {
            Title = "KillerPDF PDF/A-4f attachment smoke test",
            Language = "en-US"
        })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4fConformance()
        .AddBlankPage()
        .AddAttachment("evidence.txt", "KillerPDF PDF/A-4f attachment"u8.ToArray(),
            "text/plain", "PDF/A-4f validation payload", PdfAssociatedFileRelationship.Data,
            DateTimeOffset.UtcNow)
        .Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte PDF/A-4f attachment PDF to {destination}");
    return 0;
}

if (args.Length == 3 && args[0] == "--pdfa4e-smoke")
{
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[1]));
    string destination = Path.GetFullPath(args[2]);
    byte[] pdf = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata
        {
            Title = "KillerPDF PDF/A-4e engineering smoke test",
            Language = "en-US"
        })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4eConformance()
        .AddPage(612, 792, new PdfContentStreamBuilder()
            .SetStrokeRgb(0.2, 0.4, 0.8).SetLineWidth(2)
            .Rectangle(72, 500, 468, 200).Stroke())
        .AddAttachment("engineering-data.txt", "KillerPDF engineering data"u8.ToArray(),
            "text/plain", "Engineering validation payload",
            PdfAssociatedFileRelationship.Data, DateTimeOffset.UtcNow)
        .Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte PDF/A-4e engineering PDF to {destination}");
    return 0;
}

if (args.Length == 2 && args[0] == "--authoring-smoke")
{
    string destination = Path.GetFullPath(args[1]);
    var content = new PdfContentStreamBuilder()
        .SaveState()
        .SetFillRgb(0.9, 0.2, 0.4)
        .Rectangle(72, 72, 200, 100)
        .Fill()
        .RestoreState();
    byte[] pdf = new PdfDocumentBuilder().AddPage(612, 792, content).Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} bytes to {destination}");
    return 0;
}

if (args.Length == 2 && args[0] == "--tagged-smoke")
{
    string destination = Path.GetFullPath(args[1]);
    var content = new PdfContentStreamBuilder()
        .BeginArtifact().SetStrokeGray(0.5).Rectangle(36, 36, 540, 720).Stroke()
        .EndMarkedContent()
        .BeginMarkedContent(PdfStructureType.Figure, 0)
        .SetFillRgb(0.9, 0.2, 0.4).Rectangle(72, 600, 240, 100).Fill()
        .EndMarkedContent();
    byte[] pdf = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata
        {
            Title = "KillerPDF tagged document smoke test",
            Language = "en-US"
        })
        .EnablePdfUa2Conformance()
        .AddPage(612, 792, content)
        .AddStructureContainer(PdfStructureType.Document)
        .AddStructureElement(PdfStructureType.Figure, 0, 0, 1,
            alternateDescription: "A pink rectangle")
        .Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte tagged PDF to {destination}");
    return 0;
}

if (args.Length == 2 && args[0] == "--tagged-import-smoke")
{
    string destination = Path.GetFullPath(args[1]);
    var content = new PdfContentStreamBuilder()
        .BeginArtifact().SetStrokeGray(0.5).Rectangle(36, 36, 540, 720).Stroke()
        .EndMarkedContent()
        .BeginMarkedContent(PdfStructureType.Figure, 0)
        .SetFillRgb(0.9, 0.2, 0.4).Rectangle(72, 600, 240, 100).Fill()
        .EndMarkedContent();
    byte[] source = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata
        {
            Title = "KillerPDF imported tagged document smoke test",
            Language = "en-US"
        })
        .EnablePdfUa2Conformance()
        .AddPage(612, 792, content)
        .AddStructureContainer(PdfStructureType.Document)
        .AddStructureElement(PdfStructureType.Figure, 0, 0, 1,
            alternateDescription: "A pink rectangle")
        .Build();
    byte[] target = new PdfDocumentBuilder().Build();
    byte[] pdf = new PdfIncrementalPageEditor(PdfDocument.Open(target))
        .AddImportedDocument(PdfDocument.Open(source))
        .Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte imported tagged PDF to {destination}");
    return 0;
}

if (args.Length == 3 && args[0] == "--layers-smoke")
{
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[1]));
    string destination = Path.GetFullPath(args[2]);
    var artwork = new PdfOptionalContentGroup("Artwork");
    var review = new PdfOptionalContentGroup("Review notes", initiallyVisible: false);
    var content = new PdfContentStreamBuilder()
        .BeginOptionalContent(artwork)
        .BeginMarkedContent(PdfStructureType.Figure, 0)
        .SetFillRgb(0.15, 0.45, 0.85).Rectangle(72, 560, 240, 140).Fill()
        .EndMarkedContent()
        .EndMarkedContent()
        .BeginOptionalContent(review)
        .BeginMarkedContent(PdfStructureType.Figure, 1)
        .SetStrokeRgb(0.9, 0.15, 0.3).SetLineWidth(4)
        .Rectangle(96, 584, 192, 92).Stroke()
        .EndMarkedContent()
        .EndMarkedContent();
    byte[] source = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata
        {
            Title = "KillerPDF optional-content layer smoke test",
            Language = "en-US"
        })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4Conformance()
        .EnablePdfUa2Conformance()
        .AddPage(612, 792, content)
        .AddStructureContainer(PdfStructureType.Document)
        .AddStructureElement(PdfStructureType.Figure, 0, 0, 1,
            alternateDescription: "A blue rectangle")
        .AddStructureElement(PdfStructureType.Figure, 0, 1, 1,
            alternateDescription: "A red review outline")
        .Build();
    byte[] target = new PdfDocumentBuilder().Build();
    byte[] pdf = new PdfIncrementalPageEditor(PdfDocument.Open(target))
        .AddImportedDocument(PdfDocument.Open(source))
        .Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte layered PDF to {destination}");
    return 0;
}

if (args.Length == 4 && args[0] == "--signature-smoke")
{
    string openSsl = Path.GetFullPath(args[1]);
    if (!File.Exists(openSsl))
        throw new FileNotFoundException("The OpenSSL executable was not found.", openSsl);
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[2]));
    string destination = Path.GetFullPath(args[3]);
    string scratch = Path.Combine(Path.GetTempPath(),
        $"killerpdf-signature-{Guid.NewGuid():N}");
    Directory.CreateDirectory(scratch);
    try
    {
        string keyPath = Path.Combine(scratch, "key.pem");
        string certificatePath = Path.Combine(scratch, "certificate.pem");
        string contentPath = Path.Combine(scratch, "content.bin");
        string signaturePath = Path.Combine(scratch, "signature.der");
        string verifiedPath = Path.Combine(scratch, "verified.bin");
        RunOpenSsl("req", "-x509", "-newkey", "rsa:2048",
            "-keyout", keyPath, "-out", certificatePath,
            "-days", "1", "-nodes", "-subj", "/CN=KillerPDF Signature Smoke");

        byte[] source = new PdfDocumentBuilder()
            .SetMetadata(new PdfDocumentMetadata
            {
                Title = "KillerPDF detached CMS signature smoke test",
                Language = "en-US"
            })
            .SetOutputIntent(profile, "sRGB IEC61966-2.1")
            .EnablePdfA4Conformance()
            .AddBlankPage()
            .Build();
        byte[] pdf = PdfDetachedSignatureWriter.Sign(
            PdfDocument.Open(source), content =>
            {
                File.WriteAllBytes(contentPath, content.ToArray());
                RunOpenSsl("cms", "-sign", "-binary", "-in", contentPath,
                    "-signer", certificatePath, "-inkey", keyPath,
                    "-outform", "DER", "-out", signaturePath,
                    "-md", "sha256", "-nosmimecap");
                return File.ReadAllBytes(signaturePath);
            }, new PdfSignatureOptions
            {
                FieldName = "ReleaseApproval",
                SignerName = "KillerPDF Signature Smoke",
                Reason = "Engine validation",
                SigningTime = DateTimeOffset.UtcNow
            });
        RunOpenSsl("cms", "-verify", "-binary", "-inform", "DER",
            "-in", signaturePath, "-content", contentPath,
            "-certfile", certificatePath, "-noverify", "-out", verifiedPath);
        if (!File.ReadAllBytes(contentPath).AsSpan()
            .SequenceEqual(File.ReadAllBytes(verifiedPath)))
            throw new InvalidOperationException("OpenSSL returned different verified signature content.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, pdf);
        Console.WriteLine($"Wrote {pdf.Length:N0} byte CMS-signed PDF to {destination}");
    }
    finally
    {
        Directory.Delete(scratch, recursive: true);
    }
    return 0;

    void RunOpenSsl(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(openSsl)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("OpenSSL could not be started.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"OpenSSL exited with code {process.ExitCode}: {standardError}{standardOutput}");
    }
}

if (args.Length == 3 && args[0] == "--transparency-smoke")
{
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[1]));
    string destination = Path.GetFullPath(args[2]);
    var content = new PdfContentStreamBuilder()
        .SetFillRgb(0.15, 0.45, 0.85)
        .SetGraphicsState(new PdfGraphicsState(0.7, 1, PdfBlendMode.Multiply))
        .BeginMarkedContent(PdfStructureType.Figure, 0)
        .Rectangle(72, 520, 260, 180).Fill()
        .EndMarkedContent()
        .SetFillRgb(0.9, 0.2, 0.4)
        .SetGraphicsState(new PdfGraphicsState(0.55, 1, PdfBlendMode.Screen))
        .BeginMarkedContent(PdfStructureType.Figure, 1)
        .Rectangle(220, 580, 260, 140).Fill()
        .EndMarkedContent();
    byte[] source = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata
        {
            Title = "KillerPDF transparency and blend-mode smoke test",
            Language = "en-US"
        })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4Conformance()
        .EnablePdfUa2Conformance()
        .AddPage(612, 792, content)
        .AddStructureContainer(PdfStructureType.Document)
        .AddStructureElement(PdfStructureType.Figure, 0, 0, 1,
            alternateDescription: "A translucent blue rectangle")
        .AddStructureElement(PdfStructureType.Figure, 0, 1, 1,
            alternateDescription: "A translucent pink rectangle")
        .Build();
    byte[] target = new PdfDocumentBuilder().Build();
    byte[] pdf = new PdfIncrementalPageEditor(PdfDocument.Open(target))
        .AddImportedDocument(PdfDocument.Open(source))
        .Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte transparency PDF to {destination}");
    return 0;
}

if (args.Length == 3 && args[0] == "--gradient-smoke")
{
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[1]));
    string destination = Path.GetFullPath(args[2]);
    var axial = new PdfAxialGradient(72, 560, 300, 700, [
        new PdfGradientStop(0, new PdfRgbColor(0.1, 0.3, 0.9)),
        new PdfGradientStop(0.45, new PdfRgbColor(0.2, 0.9, 0.7)),
        new PdfGradientStop(1, new PdfRgbColor(1, 0.8, 0.1))]);
    var radial = new PdfRadialGradient(420, 630, 0, 420, 630, 90, [
        new PdfGradientStop(0, new PdfRgbColor(1, 1, 1)),
        new PdfGradientStop(0.55, new PdfRgbColor(0.95, 0.25, 0.5)),
        new PdfGradientStop(1, new PdfRgbColor(0.2, 0.05, 0.3))]);
    var content = new PdfContentStreamBuilder()
        .BeginArtifact()
        .SetStrokeRgb(0.2, 0.75, 0.9).SetLineWidth(5)
        .SetLineCap(PdfLineCap.Round).SetLineJoin(PdfLineJoin.Bevel)
        .SetMiterLimit(6).SetDashPattern([18, 7, 3, 7], 2)
        .Rectangle(60, 488, 492, 244).Stroke()
        .EndMarkedContent()
        .BeginMarkedContent(PdfStructureType.Figure, 0)
        .SaveState().Rectangle(72, 560, 228, 140).Clip().PaintShading(axial).RestoreState()
        .EndMarkedContent()
        .BeginMarkedContent(PdfStructureType.Figure, 1)
        .SaveState().Rectangle(330, 540, 180, 180).Clip().PaintShading(radial).RestoreState()
        .EndMarkedContent();
    byte[] source = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata
        {
            Title = "KillerPDF gradient shading smoke test",
            Language = "en-US"
        })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4Conformance()
        .EnablePdfUa2Conformance()
        .AddPage(612, 792, content)
        .AddStructureContainer(PdfStructureType.Document)
        .AddStructureElement(PdfStructureType.Figure, 0, 0, 1,
            alternateDescription: "A blue, green, and yellow axial gradient")
        .AddStructureElement(PdfStructureType.Figure, 0, 1, 1,
            alternateDescription: "A white, pink, and purple radial gradient")
        .Build();
    byte[] target = new PdfDocumentBuilder().Build();
    byte[] pdf = new PdfIncrementalPageEditor(PdfDocument.Open(target))
        .AddImportedDocument(PdfDocument.Open(source))
        .Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte gradient PDF to {destination}");
    return 0;
}

if (args.Length == 3 && args[0] == "--form-xobject-smoke")
{
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[1]));
    string destination = Path.GetFullPath(args[2]);
    var gradient = new PdfAxialGradient(0, 0, 180, 0, [
        new PdfGradientStop(0, new PdfRgbColor(0.08, 0.2, 0.65)),
        new PdfGradientStop(0.5, new PdfRgbColor(0.1, 0.75, 0.8)),
        new PdfGradientStop(1, new PdfRgbColor(0.95, 0.75, 0.15))]);
    var emblem = new PdfFormXObject(180, 80, new PdfContentStreamBuilder()
        .SetOpacity(0.92)
        .Rectangle(0, 0, 180, 80).Clip()
        .PaintShading(gradient), isolatedTransparencyGroup: true);
    var card = new PdfFormXObject(220, 120, new PdfContentStreamBuilder()
        .SetFillRgb(0.12, 0.12, 0.16).Rectangle(0, 0, 220, 120).Fill()
        .DrawForm(emblem, 20, 20));
    var content = new PdfContentStreamBuilder()
        .BeginMarkedContent(PdfStructureType.Figure, 0)
        .DrawForm(card, 72, 560)
        .DrawForm(card, 330, 560, 176, 96)
        .EndMarkedContent();
    byte[] source = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata
        {
            Title = "KillerPDF reusable form smoke test",
            Language = "en-US"
        })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4Conformance()
        .EnablePdfUa2Conformance()
        .AddPage(612, 792, content)
        .AddStructureContainer(PdfStructureType.Document)
        .AddStructureElement(PdfStructureType.Figure, 0, 0, 1,
            alternateDescription: "Two reusable gradient cards")
        .Build();
    byte[] target = new PdfDocumentBuilder().Build();
    byte[] pdf = new PdfIncrementalPageEditor(PdfDocument.Open(target))
        .AddImportedDocument(PdfDocument.Open(source))
        .Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte Form XObject PDF to {destination}");
    return 0;
}

if (args.Length == 3 && args[0] == "--tiling-pattern-smoke")
{
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[1]));
    string destination = Path.GetFullPath(args[2]);
    var pattern = new PdfTilingPattern(24, 24, new PdfContentStreamBuilder()
        .Rectangle(3, 3, 6, 6).Fill().Rectangle(15, 15, 6, 6).Fill(),
        paintType: PdfTilingPatternPaintType.Uncolored,
        matrix: new PdfPatternMatrix(0.966, 0.259, -0.259, 0.966, 0, 0));
    var content = new PdfContentStreamBuilder()
        .BeginMarkedContent(PdfStructureType.Figure, 0)
        .SetFillRgb(0.08, 0.12, 0.22).Rectangle(72, 500, 468, 220).Fill()
        .SetFillPattern(pattern, new PdfRgbColor(0.95, 0.3, 0.2))
        .Rectangle(72, 500, 468, 220).Fill()
        .EndMarkedContent();
    byte[] pdf = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata
        {
            Title = "KillerPDF tiling pattern smoke test",
            Language = "en-US"
        })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4Conformance()
        .EnablePdfUa2Conformance()
        .AddPage(612, 792, content)
        .AddStructureContainer(PdfStructureType.Document)
        .AddStructureElement(PdfStructureType.Figure, 0, 0, 1,
            alternateDescription: "A repeating red dot pattern on a dark blue field")
        .Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte tiling-pattern PDF to {destination}");
    return 0;
}

if (args.Length == 2 && args[0] == "--form-smoke")
{
    string destination = Path.GetFullPath(args[1]);
    byte[] pdf = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata { Title = "KillerPDF form smoke test", Language = "en-US" })
        .AddBlankPage()
        .AddTextField(0, "customer.name", 72, 680, 240, 28, "Steve the Killer", 12)
        .AddCheckBox(0, "customer.approved", 72, 640, 18, 18, isChecked: true)
        .AddRadioGroup("customer.plan", [
            new PdfRadioButtonOption(0, 72, 600, 18, 18, "Free"),
            new PdfRadioButtonOption(0, 120, 600, 18, 18, "Pro")], "Pro")
        .AddComboBox(0, "customer.theme", 72, 550, 180, 24,
            ["Dark", "Mourning", "98SE"], "Mourning")
        .Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte AcroForm PDF to {destination}");
    return 0;
}

if (args.Length == 4 && args[0] == "--pdfa-form-smoke")
{
    TrueTypeFont font = TrueTypeFont.Load(File.ReadAllBytes(args[1]));
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[2]));
    string destination = Path.GetFullPath(args[3]);
    byte[] pdf = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata { Title = "KillerPDF PDF/A form smoke test", Language = "en-US" })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4Conformance()
        .AddBlankPage()
        .AddTextField(0, "customer.name", 72, 680, 240, 28, "KillerPDF café Ω", 12,
            embeddedFont: font)
        .AddComboBox(0, "customer.theme", 72, 630, 180, 24,
            ["Dark", "Mourning", "98SE"], "Mourning", embeddedFont: font)
        .AddCheckBox(0, "customer.approved", 72, 590, 18, 18, isChecked: true)
        .AddRadioGroup("customer.plan", [
            new PdfRadioButtonOption(0, 72, 550, 18, 18, "Free"),
            new PdfRadioButtonOption(0, 120, 550, 18, 18, "Pro")], "Pro")
        .Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte PDF/A-4 AcroForm PDF to {destination}");
    return 0;
}

if (args.Length == 4 && args[0] == "--pdfa-cff-smoke")
{
    TrueTypeFont font = TrueTypeFont.Load(File.ReadAllBytes(args[1]));
    if (!font.HasCffOutlines)
        throw new ArgumentException("The CFF smoke test requires an OTTO font with CFF outlines.");
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[2]));
    string destination = Path.GetFullPath(args[3]);
    var content = new PdfContentStreamBuilder()
        .BeginText().SetFont(font, 24).MoveText(72, 700)
        .ShowUnicodeText("KillerPDF").EndText();
    byte[] pdf = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata
        {
            Title = "KillerPDF CFF OpenType smoke test",
            Language = "en-US"
        })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4Conformance()
        .AddPage(612, 792, content)
        .Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte PDF/A-4 with embedded {font.PostScriptName} to {destination}");
    return 0;
}

if (args.Length == 3 && args[0] == "--pdfa-annotation-smoke")
{
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[1]));
    string destination = Path.GetFullPath(args[2]);
    var content = new PdfContentStreamBuilder()
        .SetFillRgb(0.12, 0.12, 0.12)
        .Rectangle(72, 620, 360, 72)
        .Fill();
    byte[] pdf = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata { Title = "KillerPDF PDF/A annotation smoke test", Language = "en-US" })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4Conformance()
        .AddPage(612, 792, content)
        .AddTextNote(0, 448, 650, "Review this section", PdfRgbColor.NoteYellow)
        .AddHighlight(0, 90, 640, 300, 28, "Highlighted passage", PdfRgbColor.Yellow, 0.35)
        .AddUnderline(0, 90, 590, 300, 28, "Underlined passage")
        .AddStrikeOut(0, 90, 540, 300, 28, "Struck passage")
        .AddSquiggly(0, 90, 490, 300, 28, "Spelling review")
        .Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte PDF/A-4 annotation PDF to {destination}");
    return 0;
}

if (args.Length == 4 && args[0] == "--pdfa-visual-annotation-smoke")
{
    TrueTypeFont font = TrueTypeFont.Load(File.ReadAllBytes(args[1]));
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[2]));
    PdfImage stampImage = PdfImage.FromRgba(2, 2, new byte[]
    {
        255, 40, 80, 230, 40, 120, 255, 150,
        40, 220, 120, 150, 255, 180, 40, 230
    });
    string destination = Path.GetFullPath(args[3]);
    byte[] pdf = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata { Title = "KillerPDF PDF/A visual annotation smoke test", Language = "en-US" })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4Conformance()
        .AddBlankPage()
        .AddFreeText(0, 72, 660, 250, 70, "KillerPDF café Ω\nMultiline free text", font, 14,
            textColor: new PdfRgbColor(0.1, 0.1, 0.1), fillColor: new PdfRgbColor(1, 1, 0.8), opacity: 0.9)
        .AddLineAnnotation(0, new PdfPoint(72, 620), new PdfPoint(320, 580),
            new PdfRgbColor(0.1, 0.35, 0.9), 3, 0.8, "Line annotation")
        .AddRectangleAnnotation(0, 72, 490, 110, 65,
            new PdfRgbColor(0.9, 0.1, 0.2), new PdfRgbColor(1, 0.8, 0.85), 3, 0.75, "Rectangle")
        .AddEllipseAnnotation(0, 210, 490, 110, 65,
            new PdfRgbColor(0.1, 0.55, 0.25), new PdfRgbColor(0.8, 1, 0.85), 3, 0.75, "Ellipse")
        .AddInkAnnotation(0,
        [
            [new PdfPoint(72, 430), new PdfPoint(110, 455), new PdfPoint(150, 425)],
            [new PdfPoint(170, 430), new PdfPoint(210, 455), new PdfPoint(250, 425)]
        ], new PdfRgbColor(0.45, 0.1, 0.7), 4, 0.85, "Two ink strokes")
        .AddImageStamp(0, 350, 390, 100, 60, stampImage, "RGBA image stamp")
        .Build();
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte PDF/A-4 visual annotation PDF to {destination}");
    return 0;
}

if (args.Length == 3 && args[0] == "--incremental-smoke")
{
    byte[] source = File.ReadAllBytes(args[1]);
    PdfDocument document = PdfDocument.Open(source);
    var rootName = new PdfName("Root"u8);
    var pageModeName = new PdfName("PageMode"u8);
    var rootReference = document.Trailer[rootName] as PdfIndirectReference
        ?? throw new InvalidDataException("The source trailer does not contain an indirect /Root.");
    var rootDictionary = document.Resolve(rootReference) as PdfDictionary
        ?? throw new InvalidDataException("The source catalog is not a dictionary.");
    var entries = rootDictionary.Where(entry => !entry.Key.Equals(pageModeName)).ToList();
    entries.Add(new KeyValuePair<PdfName, PdfObject>(pageModeName, new PdfName("UseThumbs"u8)));
    byte[] pdf = new PdfIncrementalUpdateBuilder(document)
        .ReplaceObject(rootReference.ObjectNumber, new PdfDictionary(entries))
        .Build();
    if (!pdf.AsSpan(0, source.Length).SequenceEqual(source))
        throw new InvalidDataException("The incremental update changed source bytes.");
    string destination = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length - source.Length:N0} appended bytes to {destination}");
    return 0;
}

if (args.Length == 3 && args[0] == "--incremental-annotation-smoke")
{
    byte[] source = File.ReadAllBytes(args[1]);
    byte[] pdf = new PdfIncrementalAnnotationEditor(PdfDocument.Open(source))
        .AddTextNote(0, 500, 700, "Incrementally appended note", PdfRgbColor.NoteYellow, open: true)
        .AddHighlight(0, 360, 650, 160, 24, "Incrementally appended highlight")
        .AddUnderline(0, 360, 620, 160, 20, "Incrementally appended underline")
        .AddStrikeOut(0, 360, 590, 160, 20, "Incrementally appended strikeout")
        .AddSquiggly(0, 360, 560, 160, 20, "Incrementally appended squiggly")
        .Build();
    if (!pdf.AsSpan(0, source.Length).SequenceEqual(source))
        throw new InvalidDataException("The incremental annotation update changed source bytes.");
    string destination = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote five annotations in {pdf.Length - source.Length:N0} appended bytes to {destination}");
    return 0;
}

if (args.Length == 4 && args[0] == "--incremental-visual-annotation-smoke")
{
    TrueTypeFont font = TrueTypeFont.Load(File.ReadAllBytes(args[1]));
    PdfImage stampImage = PdfImage.FromRgba(2, 2, new byte[]
    {
        255, 40, 80, 230, 40, 120, 255, 150,
        40, 220, 120, 150, 255, 180, 40, 230
    });
    byte[] source = File.ReadAllBytes(args[2]);
    byte[] pdf = new PdfIncrementalAnnotationEditor(PdfDocument.Open(source))
        .AddFreeText(0, 350, 680, 170, 60, "Incremental café Ω\nEmbedded free text", font, 12,
            fillColor: new PdfRgbColor(1, 1, 0.8), opacity: 0.9)
        .AddLine(0, new PdfPoint(350, 640), new PdfPoint(520, 610),
            new PdfRgbColor(0.1, 0.35, 0.9), 3, 0.8, "Incremental line")
        .AddRectangle(0, 350, 530, 75, 50,
            new PdfRgbColor(0.9, 0.1, 0.2), new PdfRgbColor(1, 0.8, 0.85), 3, 0.75)
        .AddEllipse(0, 445, 530, 75, 50,
            new PdfRgbColor(0.1, 0.55, 0.25), new PdfRgbColor(0.8, 1, 0.85), 3, 0.75)
        .AddInk(0,
        [
            [new PdfPoint(350, 480), new PdfPoint(385, 500), new PdfPoint(420, 475)],
            [new PdfPoint(445, 480), new PdfPoint(480, 500), new PdfPoint(515, 475)]
        ], new PdfRgbColor(0.45, 0.1, 0.7), 4, 0.85)
        .AddImageStamp(0, 460, 410, 100, 60, stampImage, "Incremental RGBA image stamp")
        .Build();
    if (!pdf.AsSpan(0, source.Length).SequenceEqual(source))
        throw new InvalidDataException("The incremental visual annotation update changed source bytes.");
    string destination = Path.GetFullPath(args[3]);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote six visual annotations in {pdf.Length - source.Length:N0} appended bytes to {destination}");
    return 0;
}

if (args.Length == 4 && args[0] == "--pdfa-page-smoke")
{
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[1]));
    string sourcePath = Path.GetFullPath(args[2]);
    string destination = Path.GetFullPath(args[3]);
    byte[] source = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata { Title = "KillerPDF PDF/A page operations smoke test", Language = "en-US" })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4Conformance()
        .AddPage(600, 400, new PdfContentStreamBuilder()
            .SetFillRgb(0.9, 0.15, 0.25).Rectangle(50, 50, 500, 300).Fill())
        .AddPage(500, 500, new PdfContentStreamBuilder()
            .SetFillRgb(0.15, 0.7, 0.3).Rectangle(50, 50, 400, 400).Fill())
        .AddPage(400, 600, new PdfContentStreamBuilder()
            .SetFillRgb(0.1, 0.4, 0.9).Rectangle(50, 50, 300, 500).Fill())
        .AddPageLabelRange(0, PdfPageLabelStyle.LowerRoman)
        .AddPageLabelRange(2, PdfPageLabelStyle.Decimal, "Appendix ")
        .Build();
    byte[] pdf = new PdfIncrementalPageEditor(PdfDocument.Open(source))
        .RemovePage(1)
        .RotateClockwise(0)
        .SetCropBox(1, 25, 25, 350, 550)
        .MovePage(0, 1)
        .InsertBlankPage(1, 300, 300)
        .Build();
    if (!pdf.AsSpan(0, source.Length).SequenceEqual(source))
        throw new InvalidDataException("The incremental page update changed source bytes.");
    Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(sourcePath, source);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Inserted and removed pages, reordered two, rotated one, and cropped one in {pdf.Length - source.Length:N0} appended bytes to {destination}");
    return 0;
}

if (args.Length == 4 && args[0] == "--pdfa-import-smoke")
{
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[1]));
    byte[] importSource = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata { Title = "KillerPDF page import source", Language = "en-US" })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4Conformance()
        .AddPage(400, 600, new PdfContentStreamBuilder()
            .SetFillRgb(0.1, 0.4, 0.9).Rectangle(50, 50, 300, 500).Fill())
        .AddPage(600, 400, new PdfContentStreamBuilder()
            .SetFillRgb(0.9, 0.15, 0.25).Rectangle(50, 50, 500, 300).Fill())
        .AddTextNote(0, 340, 540, "Imported archival annotation")
        .AddPageLink(0, 20, 20, 40, 20, 1)
        .AddNamedDestination("imported-appendix", 1)
        .AddNamedDestinationLink(0, 72, 40, 120, 20, "imported-appendix")
        .AddPageLabelRange(0, PdfPageLabelStyle.Decimal, "Imported ")
        .AddBookmark("Imported appendix", 1)
        .AddCheckBox(1, "import.approved", 520, 330, 18, 18, isChecked: true)
        .Build();
    byte[] target = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata { Title = "KillerPDF PDF/A page import smoke test", Language = "en-US" })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4Conformance()
        .AddPage(300, 300, new PdfContentStreamBuilder()
            .SetFillRgb(0.55, 0.2, 0.75).Rectangle(40, 40, 220, 220).Fill())
        .AddNamedDestination("imported-appendix", 0)
        .AddPageLabelRange(0, PdfPageLabelStyle.None, "Cover")
        .AddBookmark("Target cover", 0)
        .AddCheckBox(0, "target.approved", 250, 250, 18, 18, isChecked: false)
        .Build();
    PdfDocument sourceDocument = PdfDocument.Open(importSource);
    byte[] pdf = new PdfIncrementalPageEditor(PdfDocument.Open(target))
        .AddImportedDocument(sourceDocument)
        .Build();
    if (!pdf.AsSpan(0, target.Length).SequenceEqual(target))
        throw new InvalidDataException("The incremental page import changed target source bytes.");
    string sourcePath = Path.GetFullPath(args[2]);
    string destination = Path.GetFullPath(args[3]);
    Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(sourcePath, target);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Imported two linked PDF/A pages in {pdf.Length - target.Length:N0} appended bytes to {destination}");
    return 0;
}

if (args.Length == 2 && args[0] == "--document-import-smoke")
{
    byte[] source = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata { Title = "KillerPDF complete import source", Language = "en-US" })
        .AddBlankPage(400, 600)
        .AddBlankPage(600, 400)
        .AddBookmark("Imported appendix", 1)
        .AddBookmark("Imported detail", 0, 1)
        .AddNamedDestination("imported-appendix", 1)
        .AddNamedDestinationLink(0, 40, 40, 120, 24, "imported-appendix")
        .AddPageLabelRange(0, PdfPageLabelStyle.Decimal, "Imported ")
        .AddAttachment("source.txt", "source attachment"u8.ToArray(), "text/plain")
        .Build();
    byte[] target = new PdfDocumentBuilder()
        .AddBlankPage(300, 300)
        .AddNamedDestination("imported-appendix", 0)
        .AddPageLabelRange(0, PdfPageLabelStyle.None, "Cover")
        .AddBookmark("Target cover", 0)
        .AddAttachment("target.txt", "target attachment"u8.ToArray(), "text/plain")
        .Build();
    byte[] pdf = new PdfIncrementalPageEditor(PdfDocument.Open(target))
        .AddImportedDocument(PdfDocument.Open(source))
        .Build();
    if (!pdf.AsSpan(0, target.Length).SequenceEqual(target))
        throw new InvalidDataException("The complete document import changed target bytes.");
    string destination = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Imported bookmarks, navigation metadata, and attachments in {pdf.Length - target.Length:N0} appended bytes to {destination}");
    return 0;
}

if (args.Length == 4 && args[0] == "--import-document")
{
    PdfDocument importSource = PdfDocument.Open(File.ReadAllBytes(args[1]));
    byte[] target = File.ReadAllBytes(args[2]);
    byte[] pdf = new PdfIncrementalPageEditor(PdfDocument.Open(target))
        .AddImportedDocument(importSource)
        .Build();
    if (!pdf.AsSpan(0, target.Length).SequenceEqual(target))
        throw new InvalidDataException("The incremental document import changed target source bytes.");
    string destination = Path.GetFullPath(args[3]);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Imported {new PdfIncrementalPageEditor(importSource).PageCount:N0} pages in " +
        $"{pdf.Length - target.Length:N0} appended bytes to {destination}");
    return 0;
}

if (args.Length == 3 && args[0] == "--pdfa-navigation-smoke")
{
    PdfIccProfile profile = PdfIccProfile.Load(File.ReadAllBytes(args[1]));
    byte[] pdf = new PdfDocumentBuilder()
        .SetMetadata(new PdfDocumentMetadata { Title = "KillerPDF PDF/A navigation smoke test", Language = "en-US" })
        .SetOutputIntent(profile, "sRGB IEC61966-2.1")
        .EnablePdfA4Conformance()
        .AddBlankPage().AddBlankPage().AddBlankPage()
        .AddNamedDestination("appendix", 2)
        .AddNamedDestination("résumé", 1)
        .AddNamedDestinationLink(0, 72, 680, 180, 24, "appendix")
        .AddPageLabelRange(0, PdfPageLabelStyle.LowerRoman)
        .AddPageLabelRange(2, PdfPageLabelStyle.Decimal, "Appendix ", 1)
        .Build();
    string destination = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllBytes(destination, pdf);
    Console.WriteLine($"Wrote {pdf.Length:N0} byte PDF/A-4 navigation PDF to {destination}");
    return 0;
}

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: KillerPdf.Engine.Corpus <directory> [--max <count>]");
    Console.WriteLine("       KillerPdf.Engine.Corpus --authoring-smoke <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --tagged-smoke <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --tagged-import-smoke <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --layers-smoke <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --signature-smoke <openssl.exe> <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --transparency-smoke <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --gradient-smoke <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --form-xobject-smoke <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --tiling-pattern-smoke <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --font-info <font.ttf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --unicode-smoke <font.ttf> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --text-state-smoke <font.ttf> <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --image-smoke <image.jpg> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --form-smoke <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --output-intent-smoke <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --cmyk-smoke <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --pdfa4f-attachment-smoke <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --pdfa4e-smoke <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --pdfa-form-smoke <font.ttf> <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --pdfa-cff-smoke <font.otf> <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --pdfa-annotation-smoke <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --pdfa-visual-annotation-smoke <font.ttf> <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --incremental-smoke <input.pdf> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --incremental-annotation-smoke <input.pdf> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --incremental-visual-annotation-smoke <font.ttf> <input.pdf> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --pdfa-page-smoke <profile.icc> <source.pdf> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --pdfa-import-smoke <profile.icc> <target.pdf> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --document-import-smoke <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --import-document <source.pdf> <target.pdf> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --pdfa-navigation-smoke <profile.icc> <output.pdf>");
    return args.Length == 0 ? 2 : 0;
}

string root = Path.GetFullPath(args[0]);
if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"Directory not found: {root}");
    return 2;
}

int maximum = int.MaxValue;
for (int index = 1; index < args.Length; index++)
{
    if (args[index] != "--max" || index + 1 >= args.Length
        || !int.TryParse(args[++index], out maximum) || maximum < 1)
    {
        Console.Error.WriteLine("Expected --max followed by a positive integer.");
        return 2;
    }
}

string[] files = Directory.EnumerateFiles(root, "*.pdf", SearchOption.AllDirectories)
    .Order(StringComparer.OrdinalIgnoreCase)
    .Take(maximum)
    .ToArray();
int passed = 0;
int failed = 0;
var started = DateTimeOffset.UtcNow;

foreach (string file in files)
{
    try
    {
        PdfRoundTripResult result = PdfRoundTripValidator.Validate(File.ReadAllBytes(file));
        if (result.Succeeded)
        {
            passed++;
            Console.WriteLine($"PASS  {Path.GetRelativePath(root, file)}  {result.RewrittenSha256}");
        }
        else
        {
            failed++;
            string findings = string.Join(
                "; ",
                result.SourceInspection.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
            string details = findings.Length > 0
                ? findings
                : result.FailureMessage ?? "Unknown validation failure.";
            Console.WriteLine($"FAIL  {Path.GetRelativePath(root, file)}  {details}");
        }
    }
    catch (Exception error)
    {
        failed++;
        Console.WriteLine($"FAIL  {Path.GetRelativePath(root, file)}  {error.Message}");
    }
}

TimeSpan elapsed = DateTimeOffset.UtcNow - started;
Console.WriteLine($"Checked {files.Length:N0}: {passed:N0} passed, {failed:N0} failed in {elapsed.TotalSeconds:N1}s.");
return failed == 0 ? 0 : 1;
