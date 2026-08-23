using System.Text;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Authoring;

public sealed class PdfRadioButtonTests
{
    [Fact]
    public void AddRadioGroup_WritesIndependentDefaultValue()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddRadioGroup("plan", [
                new PdfRadioButtonOption(0, 0, 0, 20, 20, "Free"),
                new PdfRadioButtonOption(0, 30, 0, 20, 20, "Pro")],
                selectedValue: "Pro", defaultSelectedValue: "Free")
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary parent = ResolveDictionary(document, Assert.IsType<PdfArray>(
            Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")])[Name("Fields")])[0]);

        Assert.Equal("Pro", Assert.IsType<PdfName>(parent[Name("V")]).ValueAsLatin1());
        Assert.Equal("Free", Assert.IsType<PdfName>(parent[Name("DV")]).ValueAsLatin1());
    }

    [Fact]
    public void AddRadioGroup_RejectsUnknownDefaultValue()
    {
        var builder = new PdfDocumentBuilder().AddBlankPage();
        Assert.Throws<ArgumentException>(() => builder.AddRadioGroup("plan", [
            new PdfRadioButtonOption(0, 0, 0, 20, 20, "Free"),
            new PdfRadioButtonOption(0, 30, 0, 20, 20, "Pro")],
            defaultSelectedValue: "Missing"));
    }

    [Fact]
    public void AddRadioGroup_WritesParentKidsSelectionAndWidgetAppearances()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage().AddBlankPage()
            .AddRadioGroup("plan", [
                new PdfRadioButtonOption(0, 72, 650, 18, 18, "Free"),
                new PdfRadioButtonOption(1, 72, 650, 18, 18, "Pro")], "Pro")
            .Build());
        var catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        var acroForm = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        var parent = ResolveDictionary(document, Assert.IsType<PdfArray>(acroForm[Name("Fields")])[0]);
        var kids = Assert.IsType<PdfArray>(parent[Name("Kids")]);
        var freeWidget = ResolveDictionary(document, kids[0]);
        var proWidget = ResolveDictionary(document, kids[1]);

        Assert.Equal("Btn", Assert.IsType<PdfName>(parent[Name("FT")]).ValueAsLatin1());
        Assert.Equal(1 << 15, Assert.IsType<PdfInteger>(parent[Name("Ff")]).Value);
        Assert.Equal("Pro", Assert.IsType<PdfName>(parent[Name("V")]).ValueAsLatin1());
        Assert.Equal("Pro", Assert.IsType<PdfName>(parent[Name("DV")]).ValueAsLatin1());
        Assert.Equal("Off", Assert.IsType<PdfName>(freeWidget[Name("AS")]).ValueAsLatin1());
        Assert.Equal("Pro", Assert.IsType<PdfName>(proWidget[Name("AS")]).ValueAsLatin1());
        Assert.Equal(Assert.IsType<PdfIndirectReference>(acroForm[Name("Fields")] is PdfArray fields
                ? fields[0] : throw new InvalidOperationException()).ObjectNumber,
            Assert.IsType<PdfIndirectReference>(proWidget[Name("Parent")]).ObjectNumber);
        var normal = Assert.IsType<PdfDictionary>(
            Assert.IsType<PdfDictionary>(proWidget[Name("AP")])[Name("N")]);
        Assert.NotNull(normal[Name("Off")]);
        Assert.NotNull(normal[Name("Pro")]);
    }

    [Fact]
    public void AddRadioGroup_RequiresDistinctOptionsAndValidSelection()
    {
        var builder = new PdfDocumentBuilder().AddBlankPage();
        Assert.Throws<ArgumentException>(() => builder.AddRadioGroup("one", [
            new PdfRadioButtonOption(0, 0, 0, 10, 10, "A")]));
        Assert.Throws<ArgumentException>(() => builder.AddRadioGroup("duplicate", [
            new PdfRadioButtonOption(0, 0, 0, 10, 10, "A"),
            new PdfRadioButtonOption(0, 20, 0, 10, 10, "A")]));
        Assert.Throws<ArgumentException>(() => builder.AddRadioGroup("selection", [
            new PdfRadioButtonOption(0, 0, 0, 10, 10, "A"),
            new PdfRadioButtonOption(0, 20, 0, 10, 10, "B")], "C"));
    }

    [Fact]
    public void AddRadioGroup_WritesTypedBehaviorAndSupportsUnisonValues()
    {
        PdfDocument document = PdfDocument.Open(new PdfDocumentBuilder()
            .AddBlankPage()
            .AddRadioGroup("plan", [
                new PdfRadioButtonOption(0, 0, 0, 10, 10, "Pro"),
                new PdfRadioButtonOption(0, 20, 0, 10, 10, "Pro")], "Pro",
                radioOptions: new PdfRadioGroupOptions
                {
                    NoToggleToOff = true,
                    RadiosInUnison = true
                })
            .Build());
        PdfDictionary catalog = ResolveDictionary(document, document.Trailer[Name("Root")]);
        PdfDictionary acroForm = Assert.IsType<PdfDictionary>(catalog[Name("AcroForm")]);
        PdfDictionary parent = ResolveDictionary(document, Assert.IsType<PdfArray>(acroForm[Name("Fields")])[0]);
        PdfArray kids = Assert.IsType<PdfArray>(parent[Name("Kids")]);

        Assert.Equal((1 << 15) | (1 << 14) | (1 << 25),
            Assert.IsType<PdfInteger>(parent[Name("Ff")]).Value);
        Assert.All(kids, kid => Assert.Equal("Pro",
            Assert.IsType<PdfName>(ResolveDictionary(document, kid)[Name("AS")]).ValueAsLatin1()));
    }

    private static PdfDictionary ResolveDictionary(PdfDocument document, PdfObject value) =>
        Assert.IsType<PdfDictionary>(document.Resolve(Assert.IsType<PdfIndirectReference>(value)));
    private static PdfName Name(string value) => new(Encoding.ASCII.GetBytes(value));
}
