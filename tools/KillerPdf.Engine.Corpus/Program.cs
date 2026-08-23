using KillerPdf.Engine.Validation;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Fonts;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Writing;
using KillerPdf.Engine.Editing;

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

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: KillerPdf.Engine.Corpus <directory> [--max <count>]");
    Console.WriteLine("       KillerPdf.Engine.Corpus --authoring-smoke <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --font-info <font.ttf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --unicode-smoke <font.ttf> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --image-smoke <image.jpg> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --form-smoke <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --output-intent-smoke <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --pdfa-form-smoke <font.ttf> <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --pdfa-annotation-smoke <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --pdfa-visual-annotation-smoke <font.ttf> <profile.icc> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --incremental-smoke <input.pdf> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --incremental-annotation-smoke <input.pdf> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --incremental-visual-annotation-smoke <font.ttf> <input.pdf> <output.pdf>");
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
