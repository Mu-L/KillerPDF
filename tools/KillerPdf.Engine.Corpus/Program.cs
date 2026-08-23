using KillerPdf.Engine.Validation;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Fonts;

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

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: KillerPdf.Engine.Corpus <directory> [--max <count>]");
    Console.WriteLine("       KillerPdf.Engine.Corpus --authoring-smoke <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --font-info <font.ttf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --unicode-smoke <font.ttf> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --image-smoke <image.jpg> <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --form-smoke <output.pdf>");
    Console.WriteLine("       KillerPdf.Engine.Corpus --output-intent-smoke <profile.icc> <output.pdf>");
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
