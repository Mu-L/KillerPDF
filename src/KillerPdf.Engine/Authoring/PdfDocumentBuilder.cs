using System.Globalization;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Syntax;
using KillerPdf.Engine.Writing;
using KillerPdf.Engine.Fonts;
using System.Text;
using System.Security.Cryptography;
using System.Xml;

namespace KillerPdf.Engine.Authoring;

/// <summary>Creates a new PDF catalog and page tree without relying on the legacy writer.</summary>
public sealed partial class PdfDocumentBuilder
{
    private static readonly PdfName RootName = Name("Root");
    private static readonly PdfName SizeName = Name("Size");
    private readonly List<PageDefinition> _pages = [];
    private readonly List<BookmarkDefinition> _bookmarks = [];
    private readonly List<AttachmentDefinition> _attachments = [];
    private readonly List<TextFieldDefinition> _textFields = [];
    private readonly List<CheckBoxDefinition> _checkBoxes = [];
    private readonly List<RadioGroupDefinition> _radioGroups = [];
    private readonly List<ChoiceFieldDefinition> _choiceFields = [];
    private OutputIntentDefinition? _outputIntent;
    private bool _pdfA4Conformance;
    private readonly List<TextNoteDefinition> _textNotes = [];
    private readonly List<TextMarkupDefinition> _textMarkups = [];

    public PdfDocumentBuilder(PdfVersion? version = null) => Version = version ?? PdfVersion.Pdf20;

    public PdfVersion Version { get; }
    public int PageCount => _pages.Count;
    public PdfDocumentMetadata? Metadata { get; private set; }

    public PdfDocumentBuilder SetMetadata(PdfDocumentMetadata metadata)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        return this;
    }

    public PdfDocumentBuilder SetOutputIntent(
        PdfIccProfile profile,
        string outputConditionIdentifier,
        string? outputCondition = null,
        string? registryName = null,
        string? information = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(outputConditionIdentifier))
            throw new ArgumentException("An output-condition identifier cannot be empty.", nameof(outputConditionIdentifier));
        _outputIntent = new OutputIntentDefinition(
            profile, outputConditionIdentifier, outputCondition, registryName, information);
        return this;
    }

    public PdfDocumentBuilder EnablePdfA4Conformance()
    {
        _pdfA4Conformance = true;
        return this;
    }

    public PdfDocumentBuilder AddBlankPage(double width = 612, double height = 792) =>
        AddPage(width, height, ReadOnlyMemory<byte>.Empty);

    public PdfDocumentBuilder AddPage(double width, double height, ReadOnlyMemory<byte> content)
    {
        ValidateDimension(width, nameof(width));
        ValidateDimension(height, nameof(height));
        _pages.Add(new PageDefinition(width, height, content.ToArray(),
            new Dictionary<PdfStandardFont, PdfName>(), [],
            new Dictionary<PdfImage, PdfName>(), []));
        return this;
    }

    public PdfDocumentBuilder AddPage(double width, double height, PdfContentStreamBuilder content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ValidateDimension(width, nameof(width));
        ValidateDimension(height, nameof(height));
        _pages.Add(new PageDefinition(
            width,
            height,
            content.Build(),
            content.FontResources.ToDictionary(entry => entry.Key, entry => entry.Value),
            content.EmbeddedFontResources.ToArray(),
            content.ImageResources.ToDictionary(entry => entry.Key, entry => entry.Value), []));
        return this;
    }

    public PdfDocumentBuilder AddUriLink(
        int pageIndex, double x, double y, double width, double height, string uri)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ValidateRectangle(x, y, width, height);
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed)
            || parsed.Scheme is not ("http" or "https" or "mailto"))
            throw new ArgumentException("A link URI must use http, https, or mailto.", nameof(uri));
        PageDefinition page = _pages[pageIndex];
        _pages[pageIndex] = page with
        {
            Links = [.. page.Links, new UriLinkDefinition(x, y, width, height, parsed.AbsoluteUri)]
        };
        return this;
    }

    public PdfDocumentBuilder AddPageLink(
        int pageIndex, double x, double y, double width, double height, int destinationPageIndex)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ValidatePageIndex(destinationPageIndex, nameof(destinationPageIndex));
        ValidateRectangle(x, y, width, height);
        PageDefinition page = _pages[pageIndex];
        _pages[pageIndex] = page with
        {
            Links = [.. page.Links,
                new PageLinkDefinition(x, y, width, height, destinationPageIndex)]
        };
        return this;
    }

    public PdfDocumentBuilder AddBookmark(string title, int pageIndex)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A bookmark title cannot be empty.", nameof(title));
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        _bookmarks.Add(new BookmarkDefinition(title, pageIndex));
        return this;
    }

    public PdfDocumentBuilder AddAttachment(
        string fileName,
        ReadOnlyMemory<byte> data,
        string mimeType = "application/octet-stream",
        string? description = null,
        PdfAssociatedFileRelationship relationship = PdfAssociatedFileRelationship.Data,
        DateTimeOffset? modificationDate = null)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("An attachment name must be a plain file name.", nameof(fileName));
        if (_attachments.Any(item => string.Equals(item.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Attachment file names must be unique.", nameof(fileName));
        if (string.IsNullOrWhiteSpace(mimeType) || mimeType.Any(character => character is < '!' or > '~'))
            throw new ArgumentException("An attachment MIME type must contain printable ASCII characters.", nameof(mimeType));
        if (!Enum.IsDefined(relationship))
            throw new ArgumentOutOfRangeException(nameof(relationship));
        _attachments.Add(new AttachmentDefinition(
            fileName, data.ToArray(), mimeType, description, relationship, modificationDate));
        return this;
    }

    public PdfDocumentBuilder AddTextField(
        int pageIndex,
        string name,
        double x,
        double y,
        double width,
        double height,
        string value = "",
        double fontSize = 12,
        PdfTextFieldOptions? options = null,
        TrueTypeFont? embeddedFont = null)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ValidateRectangle(x, y, width, height);
        ValidateUniqueFieldName(name);
        ArgumentNullException.ThrowIfNull(value);
        if (embeddedFont is null && value.Any(character => character > 0xFF))
            throw new ArgumentException(
                "The baseline text-field appearance supports Latin-1 values; Unicode form fonts are a separate milestone.",
                nameof(value));
        if (embeddedFont is not null)
            ValidateFormFontText(embeddedFont, value, nameof(value));
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        options ??= new PdfTextFieldOptions();
        if (options.MaximumLength is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumLength must be positive.");
        if (options.MaximumLength.HasValue && value.Length > options.MaximumLength.Value)
            throw new ArgumentException("The initial value exceeds the text field's maximum length.", nameof(value));
        if (options.Comb && (!options.MaximumLength.HasValue || options.Multiline || options.Password))
            throw new ArgumentException(
                "A comb field requires MaximumLength and cannot also be multiline or a password field.", nameof(options));
        _textFields.Add(new TextFieldDefinition(
            pageIndex, name, x, y, width, height, value, fontSize, options, embeddedFont));
        return this;
    }

    public PdfDocumentBuilder AddCheckBox(
        int pageIndex,
        string name,
        double x,
        double y,
        double width,
        double height,
        bool isChecked = false,
        string exportValue = "Yes")
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ValidateRectangle(x, y, width, height);
        ValidateUniqueFieldName(name);
        if (string.IsNullOrWhiteSpace(exportValue)
            || exportValue.Any(character => character is < '!' or > '~'))
            throw new ArgumentException("A checkbox export value must contain printable ASCII characters.", nameof(exportValue));
        _checkBoxes.Add(new CheckBoxDefinition(
            pageIndex, name, x, y, width, height, isChecked, exportValue));
        return this;
    }

    public PdfDocumentBuilder AddRadioGroup(
        string name,
        IEnumerable<PdfRadioButtonOption> options,
        string? selectedValue = null)
    {
        ValidateUniqueFieldName(name);
        ArgumentNullException.ThrowIfNull(options);
        PdfRadioButtonOption[] values = options.ToArray();
        if (values.Length < 2)
            throw new ArgumentException("A radio group requires at least two options.", nameof(options));
        var exportValues = new HashSet<string>(StringComparer.Ordinal);
        foreach (PdfRadioButtonOption option in values)
        {
            ArgumentNullException.ThrowIfNull(option);
            ValidatePageIndex(option.PageIndex, nameof(options));
            ValidateRectangle(option.X, option.Y, option.Width, option.Height);
            if (string.IsNullOrWhiteSpace(option.ExportValue)
                || option.ExportValue.Any(character => character is < '!' or > '~'))
                throw new ArgumentException("Radio export values must contain printable ASCII characters.", nameof(options));
            if (!exportValues.Add(option.ExportValue))
                throw new ArgumentException("Radio export values must be unique within a group.", nameof(options));
        }
        if (selectedValue is not null && !exportValues.Contains(selectedValue))
            throw new ArgumentException("The selected radio value must name one of the options.", nameof(selectedValue));
        _radioGroups.Add(new RadioGroupDefinition(name, values, selectedValue));
        return this;
    }

    public PdfDocumentBuilder AddComboBox(
        int pageIndex,
        string name,
        double x,
        double y,
        double width,
        double height,
        IEnumerable<string> options,
        string? selectedValue = null,
        bool editable = false,
        double fontSize = 12,
        TrueTypeFont? embeddedFont = null)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ValidateRectangle(x, y, width, height);
        ValidateUniqueFieldName(name);
        ArgumentNullException.ThrowIfNull(options);
        string[] values = options.ToArray();
        if (values.Length == 0 || values.Any(string.IsNullOrEmpty))
            throw new ArgumentException("Combo-box options cannot be empty.", nameof(options));
        if (embeddedFont is null && values.Any(value => value.Any(character => character > 0xFF)))
            throw new ArgumentException("Combo-box options require an embedded font for Unicode text.", nameof(options));
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("Combo-box options must be unique.", nameof(options));
        if (selectedValue is not null && !editable && !values.Contains(selectedValue, StringComparer.Ordinal))
            throw new ArgumentException("A non-editable combo-box value must be one of its options.", nameof(selectedValue));
        if (embeddedFont is null && selectedValue?.Any(character => character > 0xFF) == true)
            throw new ArgumentException("The baseline combo-box appearance supports Latin-1 values.", nameof(selectedValue));
        if (embeddedFont is not null)
        {
            foreach (string value in values)
                ValidateFormFontText(embeddedFont, value, nameof(options));
            if (selectedValue is not null)
                ValidateFormFontText(embeddedFont, selectedValue, nameof(selectedValue));
        }
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        _choiceFields.Add(new ChoiceFieldDefinition(
            pageIndex, name, x, y, width, height, values,
            selectedValue ?? values[0], editable, fontSize, embeddedFont));
        return this;
    }

    public PdfDocumentBuilder AddTextNote(
        int pageIndex,
        double x,
        double y,
        string contents,
        PdfRgbColor? color = null,
        bool open = false,
        double size = 24)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ArgumentNullException.ThrowIfNull(contents);
        if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        if (!double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(y));
        if (!double.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        _textNotes.Add(new TextNoteDefinition(
            pageIndex, x, y, size, contents, color ?? PdfRgbColor.NoteYellow, open));
        return this;
    }

    public PdfDocumentBuilder AddHighlight(
        int pageIndex,
        double x,
        double y,
        double width,
        double height,
        string? contents = null,
        PdfRgbColor? color = null,
        double opacity = 0.35)
        => AddTextMarkup(PdfTextMarkupType.Highlight, pageIndex, x, y, width, height,
            contents, color ?? PdfRgbColor.Yellow, opacity);

    public PdfDocumentBuilder AddUnderline(
        int pageIndex, double x, double y, double width, double height,
        string? contents = null, PdfRgbColor? color = null, double opacity = 1)
        => AddTextMarkup(PdfTextMarkupType.Underline, pageIndex, x, y, width, height,
            contents, color ?? new PdfRgbColor(0, 0.35, 0.9), opacity);

    public PdfDocumentBuilder AddStrikeOut(
        int pageIndex, double x, double y, double width, double height,
        string? contents = null, PdfRgbColor? color = null, double opacity = 1)
        => AddTextMarkup(PdfTextMarkupType.StrikeOut, pageIndex, x, y, width, height,
            contents, color ?? new PdfRgbColor(0.9, 0.1, 0.1), opacity);

    public PdfDocumentBuilder AddSquiggly(
        int pageIndex, double x, double y, double width, double height,
        string? contents = null, PdfRgbColor? color = null, double opacity = 1)
        => AddTextMarkup(PdfTextMarkupType.Squiggly, pageIndex, x, y, width, height,
            contents, color ?? new PdfRgbColor(0.9, 0.1, 0.1), opacity);

    private PdfDocumentBuilder AddTextMarkup(
        PdfTextMarkupType type, int pageIndex, double x, double y, double width, double height,
        string? contents, PdfRgbColor color, double opacity)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ValidateRectangle(x, y, width, height);
        if (!double.IsFinite(opacity) || opacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(opacity));
        _textMarkups.Add(new TextMarkupDefinition(
            type, pageIndex, x, y, width, height, contents, color, opacity));
        return this;
    }

    public byte[] Build()
    {
        if (_pdfA4Conformance && Metadata is null)
            throw new InvalidOperationException("PDF/A-4 authoring requires XMP document metadata.");
        if (_pdfA4Conformance && _outputIntent is null)
            throw new InvalidOperationException("PDF/A-4 authoring requires an ICC output intent.");
        if (_pdfA4Conformance && _pages.Any(page => page.Fonts.Count > 0))
            throw new InvalidOperationException("PDF/A-4 authoring requires embedded fonts; the 14 built-in PDF fonts are not embedded.");
        if (_pdfA4Conformance && (_textFields.Any(field => field.EmbeddedFont is null)
            || _choiceFields.Any(field => field.EmbeddedFont is null)))
            throw new InvalidOperationException(
                "PDF/A-4 text and choice fields require an embedded TrueType form font.");
        if (_pdfA4Conformance && _attachments.Count > 0)
            throw new InvalidOperationException("General PDF/A-4 does not permit attachments; use the PDF/A-4f milestone.");
        const int catalogNumber = 1;
        const int pagesNumber = 2;
        int nextObjectNumber = 3;
        int? metadataNumber = Metadata is null ? null : nextObjectNumber++;
        int? infoNumber = Metadata is null || _pdfA4Conformance ? null : nextObjectNumber++;
        int? outlinesNumber = _bookmarks.Count == 0 ? null : nextObjectNumber++;
        int[] bookmarkNumbers = _bookmarks.Select(_ => nextObjectNumber++).ToArray();
        var allocatedAttachments = _attachments.Select(attachment =>
            new AllocatedAttachment(attachment, nextObjectNumber++, nextObjectNumber++)).ToArray();
        var allocatedTextFields = _textFields.Select(field =>
            new AllocatedTextField(field, nextObjectNumber++, nextObjectNumber++)).ToArray();
        var allocatedCheckBoxes = _checkBoxes.Select(field =>
            new AllocatedCheckBox(field,
                nextObjectNumber++, nextObjectNumber++, nextObjectNumber++)).ToArray();
        var allocatedRadioGroups = new List<AllocatedRadioGroup>();
        foreach (RadioGroupDefinition group in _radioGroups)
        {
            int parentNumber = nextObjectNumber++;
            var widgets = group.Options.Select(option => new AllocatedRadioWidget(
                option, nextObjectNumber++, nextObjectNumber++, nextObjectNumber++)).ToArray();
            allocatedRadioGroups.Add(new AllocatedRadioGroup(group, parentNumber, widgets));
        }
        var allocatedChoiceFields = _choiceFields.Select(field =>
            new AllocatedChoiceField(field, nextObjectNumber++, nextObjectNumber++)).ToArray();
        var allocatedTextNotes = _textNotes.Select(note =>
            new AllocatedTextNote(note, nextObjectNumber++, nextObjectNumber++)).ToArray();
        var allocatedTextMarkups = _textMarkups.Select(markup =>
            new AllocatedTextMarkup(markup, nextObjectNumber++, nextObjectNumber++)).ToArray();
        var allocatedFreeTexts = _freeTexts.Select(freeText =>
            new AllocatedFreeText(freeText, nextObjectNumber++, nextObjectNumber++)).ToArray();
        var allocatedVisualAnnotations = _visualAnnotations.Select(annotation =>
            new AllocatedVisualAnnotation(annotation, nextObjectNumber++, nextObjectNumber++)).ToArray();
        var allocatedImageStamps = _imageStamps.Select(stamp =>
            new AllocatedImageStamp(stamp, nextObjectNumber++, nextObjectNumber++)).ToArray();
        int? iccProfileNumber = _outputIntent is null ? null : nextObjectNumber++;
        int? outputIntentNumber = _outputIntent is null ? null : nextObjectNumber++;
        TrueTypeFont[] formEmbeddedFonts = _textFields.Select(field => field.EmbeddedFont)
            .Concat(_choiceFields.Select(field => field.EmbeddedFont))
            .Concat(_freeTexts.Select(freeText => (TrueTypeFont?)freeText.Font))
            .Where(font => font is not null).Cast<TrueTypeFont>().Distinct().ToArray();
        var formFontResources = formEmbeddedFonts.Select((font, index) => (font, index))
            .ToDictionary(item => item.font,
                item => new PdfName(Encoding.ASCII.GetBytes($"FormF{item.index + 1}")));
        var formFontUsages = new List<EmbeddedFontUsage>();
        foreach (TrueTypeFont font in formEmbeddedFonts)
        {
            var usage = new EmbeddedFontUsage(font, formFontResources[font]);
            foreach (string value in _textFields.Where(field => ReferenceEquals(field.EmbeddedFont, font))
                .Select(field => field.Value)
                .Concat(_choiceFields.Where(field => ReferenceEquals(field.EmbeddedFont, font))
                    .SelectMany(field => field.Options.Append(field.SelectedValue)))
                .Concat(_freeTexts.Where(freeText => ReferenceEquals(freeText.Font, font))
                    .Select(freeText => freeText.Contents)))
                AddDrawableTextMappings(usage, value);
            formFontUsages.Add(usage);
        }
        var fontNumbers = new Dictionary<PdfStandardFont, int>();
        IEnumerable<PdfStandardFont> requestedStandardFonts = _pages.SelectMany(page => page.Fonts.Keys);
        if (_textFields.Any(field => field.EmbeddedFont is null)
            || _choiceFields.Any(field => field.EmbeddedFont is null))
            requestedStandardFonts = requestedStandardFonts.Append(PdfStandardFont.Helvetica);
        foreach (PdfStandardFont font in requestedStandardFonts.Distinct().Order())
            fontNumbers.Add(font, nextObjectNumber++);
        var embeddedFonts = new List<AllocatedEmbeddedFont>();
        foreach (IGrouping<TrueTypeFont, EmbeddedFontUsage> group in
            _pages.SelectMany(page => page.EmbeddedFonts).Concat(formFontUsages)
                .GroupBy(usage => usage.Font))
        {
            var mappings = new SortedDictionary<ushort, int>();
            foreach ((ushort glyph, int scalar) in group.SelectMany(usage => usage.UnicodeByGlyph))
            {
                if (mappings.TryGetValue(glyph, out int existing) && existing != scalar)
                    throw new InvalidOperationException($"Glyph {glyph} has conflicting Unicode mappings.");
                mappings[glyph] = scalar;
            }
            embeddedFonts.Add(new AllocatedEmbeddedFont(
                group.Key, mappings,
                nextObjectNumber++, nextObjectNumber++, nextObjectNumber++,
                nextObjectNumber++, nextObjectNumber++));
        }
        var imageNumbers = new Dictionary<PdfImage, int>();
        foreach (PdfImage image in _pages.SelectMany(page => page.Images.Keys)
            .Concat(_imageStamps.Select(stamp => stamp.Image)).Distinct())
            AddImageAndMask(image);
        void AddImageAndMask(PdfImage image)
        {
            if (imageNumbers.ContainsKey(image))
                return;
            imageNumbers.Add(image, nextObjectNumber++);
            if (image.SoftMask is not null)
                AddImageAndMask(image.SoftMask);
        }
        var allocated = new List<AllocatedPage>(_pages.Count);
        for (int pageIndex = 0; pageIndex < _pages.Count; pageIndex++)
        {
            PageDefinition page = _pages[pageIndex];
            int pageNumber = nextObjectNumber++;
            int? contentNumber = page.Content.Length == 0 ? null : nextObjectNumber++;
            int[] annotationNumbers = [
                .. page.Links.Select(_ => nextObjectNumber++),
                .. allocatedTextFields.Where(field => field.Definition.PageIndex == pageIndex)
                    .Select(field => field.FieldNumber),
                .. allocatedCheckBoxes.Where(field => field.Definition.PageIndex == pageIndex)
                    .Select(field => field.FieldNumber),
                .. allocatedRadioGroups.SelectMany(group => group.Widgets)
                    .Where(widget => widget.Option.PageIndex == pageIndex)
                    .Select(widget => widget.WidgetNumber),
                .. allocatedChoiceFields.Where(field => field.Definition.PageIndex == pageIndex)
                    .Select(field => field.FieldNumber),
                .. allocatedTextNotes.Where(note => note.Definition.PageIndex == pageIndex)
                    .Select(note => note.AnnotationNumber),
                .. allocatedTextMarkups.Where(markup => markup.Definition.PageIndex == pageIndex)
                    .Select(markup => markup.AnnotationNumber),
                .. allocatedFreeTexts.Where(freeText => freeText.Definition.PageIndex == pageIndex)
                    .Select(freeText => freeText.AnnotationNumber),
                .. allocatedVisualAnnotations.Where(annotation => annotation.Definition.PageIndex == pageIndex)
                    .Select(annotation => annotation.AnnotationNumber),
                .. allocatedImageStamps.Where(stamp => stamp.Definition.PageIndex == pageIndex)
                    .Select(stamp => stamp.AnnotationNumber)];
            allocated.Add(new AllocatedPage(page, pageNumber, contentNumber, annotationNumbers));
        }

        var kids = new PdfArray(allocated.Select(page =>
            (PdfObject)new PdfIndirectReference(page.PageNumber, 0)));
        var catalogEntries = new List<(string Name, PdfObject Value)>
        {
            ("Type", Name("Catalog")),
            ("Pages", new PdfIndirectReference(pagesNumber, 0))
        };
        if (metadataNumber.HasValue)
            catalogEntries.Add(("Metadata", new PdfIndirectReference(metadataNumber.Value, 0)));
        if (!string.IsNullOrWhiteSpace(Metadata?.Language))
            catalogEntries.Add(("Lang", UnicodeString(Metadata.Language)));
        if (outlinesNumber.HasValue)
        {
            catalogEntries.Add(("Outlines", new PdfIndirectReference(outlinesNumber.Value, 0)));
            catalogEntries.Add(("PageMode", Name("UseOutlines")));
        }
        if (allocatedAttachments.Length > 0)
        {
            var names = new List<PdfObject>();
            foreach (AllocatedAttachment attachment in allocatedAttachments
                .OrderBy(value => value.Definition.FileName, StringComparer.Ordinal))
            {
                names.Add(UnicodeString(attachment.Definition.FileName));
                names.Add(new PdfIndirectReference(attachment.FileSpecificationNumber, 0));
            }
            catalogEntries.Add(("Names", Dictionary(
                ("EmbeddedFiles", Dictionary(("Names", new PdfArray(names)))))));
            catalogEntries.Add(("AF", new PdfArray(allocatedAttachments.Select(attachment =>
                (PdfObject)new PdfIndirectReference(attachment.FileSpecificationNumber, 0)))));
        }
        if (allocatedTextFields.Length > 0 || allocatedCheckBoxes.Length > 0
            || allocatedRadioGroups.Count > 0 || allocatedChoiceFields.Length > 0)
        {
            var fieldReferences = allocatedTextFields.Select(field => field.FieldNumber)
                .Concat(allocatedCheckBoxes.Select(field => field.FieldNumber))
                .Concat(allocatedRadioGroups.Select(field => field.ParentNumber))
                .Concat(allocatedChoiceFields.Select(field => field.FieldNumber))
                .Select(number => (PdfObject)new PdfIndirectReference(number, 0));
            var formEntries = new List<(string Name, PdfObject Value)>
            {
                ("Fields", new PdfArray(fieldReferences)),
                ("NeedAppearances", new PdfBoolean(false))
            };
            if (allocatedTextFields.Length > 0 || allocatedChoiceFields.Length > 0)
            {
                var formFontEntries = new List<KeyValuePair<PdfName, PdfObject>>();
                if (fontNumbers.TryGetValue(PdfStandardFont.Helvetica, out int helveticaNumber))
                    formFontEntries.Add(new(Name("Helv"), new PdfIndirectReference(helveticaNumber, 0)));
                foreach ((TrueTypeFont font, PdfName resource) in formFontResources)
                    formFontEntries.Add(new(resource, new PdfIndirectReference(
                        embeddedFonts.Single(value => ReferenceEquals(value.Font, font)).Type0Number, 0)));
                PdfName defaultFormFont = fontNumbers.ContainsKey(PdfStandardFont.Helvetica)
                    ? Name("Helv") : formFontResources.Values.First();
                var formFonts = new PdfDictionary(formFontEntries);
                formEntries.Add(("DA", Latin1String($"{NameToken(defaultFormFont)} 12 Tf 0 g")));
                formEntries.Add(("DR", Dictionary(("Font", formFonts))));
            }
            catalogEntries.Add(("AcroForm", Dictionary(formEntries.ToArray())));
        }
        if (outputIntentNumber.HasValue)
            catalogEntries.Add(("OutputIntents", new PdfArray([
                new PdfIndirectReference(outputIntentNumber.Value, 0)])));
        var catalog = Dictionary(catalogEntries.ToArray());
        var pages = Dictionary(
            ("Type", Name("Pages")),
            ("Count", new PdfInteger(allocated.Count)),
            ("Kids", kids));

        var objects = new List<PdfIndirectObject>
        {
            new(catalogNumber, 0, catalog, 0),
            new(pagesNumber, 0, pages, 0)
        };
        if (Metadata is not null)
        {
            objects.Add(new PdfIndirectObject(metadataNumber!.Value, 0,
                new PdfStream(Dictionary(
                    ("Type", Name("Metadata")),
                    ("Subtype", Name("XML"))), BuildXmp(Metadata, _pdfA4Conformance)), 0));
            if (infoNumber.HasValue)
                objects.Add(new PdfIndirectObject(infoNumber.Value, 0, BuildInfo(Metadata), 0));
        }
        if (outlinesNumber.HasValue)
        {
            objects.Add(new PdfIndirectObject(outlinesNumber.Value, 0,
                Dictionary(
                    ("Type", Name("Outlines")),
                    ("First", new PdfIndirectReference(bookmarkNumbers[0], 0)),
                    ("Last", new PdfIndirectReference(bookmarkNumbers[^1], 0)),
                    ("Count", new PdfInteger(bookmarkNumbers.Length))), 0));
            for (int index = 0; index < _bookmarks.Count; index++)
            {
                BookmarkDefinition bookmark = _bookmarks[index];
                var entries = new List<(string Name, PdfObject Value)>
                {
                    ("Title", UnicodeString(bookmark.Title)),
                    ("Parent", new PdfIndirectReference(outlinesNumber.Value, 0)),
                    ("Dest", new PdfArray([
                        new PdfIndirectReference(allocated[bookmark.PageIndex].PageNumber, 0),
                        Name("Fit")]))
                };
                if (index > 0)
                    entries.Add(("Prev", new PdfIndirectReference(bookmarkNumbers[index - 1], 0)));
                if (index + 1 < bookmarkNumbers.Length)
                    entries.Add(("Next", new PdfIndirectReference(bookmarkNumbers[index + 1], 0)));
                objects.Add(new PdfIndirectObject(
                    bookmarkNumbers[index], 0, Dictionary(entries.ToArray()), 0));
            }
        }
        foreach (AllocatedAttachment allocatedAttachment in allocatedAttachments)
            AddAttachmentObjects(objects, allocatedAttachment);
        foreach (AllocatedTextField allocatedTextField in allocatedTextFields)
        {
            (PdfName resource, int number) = FormFontBinding(
                allocatedTextField.Definition.EmbeddedFont,
                fontNumbers, formFontResources, embeddedFonts);
            AddTextFieldObjects(
                objects, allocatedTextField, allocated, resource, number);
        }
        foreach (AllocatedCheckBox allocatedCheckBox in allocatedCheckBoxes)
            AddCheckBoxObjects(objects, allocatedCheckBox, allocated);
        foreach (AllocatedRadioGroup allocatedRadioGroup in allocatedRadioGroups)
            AddRadioGroupObjects(objects, allocatedRadioGroup, allocated);
        foreach (AllocatedChoiceField allocatedChoiceField in allocatedChoiceFields)
        {
            (PdfName resource, int number) = FormFontBinding(
                allocatedChoiceField.Definition.EmbeddedFont,
                fontNumbers, formFontResources, embeddedFonts);
            AddChoiceFieldObjects(
                objects, allocatedChoiceField, allocated, resource, number);
        }
        if (_outputIntent is not null)
            AddOutputIntentObjects(
                objects, _outputIntent, iccProfileNumber!.Value, outputIntentNumber!.Value);
        for (int index = 0; index < allocatedTextNotes.Length; index++)
            AddTextNoteObjects(objects, allocatedTextNotes[index], allocated, index + 1);
        for (int index = 0; index < allocatedTextMarkups.Length; index++)
            AddTextMarkupObjects(objects, allocatedTextMarkups[index], allocated, index + 1);
        for (int index = 0; index < allocatedFreeTexts.Length; index++)
        {
            FreeTextDefinition freeText = allocatedFreeTexts[index].Definition;
            (PdfName resource, int number) = FormFontBinding(
                freeText.Font, fontNumbers, formFontResources, embeddedFonts);
            AddFreeTextObjects(objects, allocatedFreeTexts[index], allocated, index + 1, resource, number);
        }
        for (int index = 0; index < allocatedVisualAnnotations.Length; index++)
            AddVisualAnnotationObjects(objects, allocatedVisualAnnotations[index], allocated, index + 1);
        for (int index = 0; index < allocatedImageStamps.Length; index++)
            AddImageStampObjects(objects, allocatedImageStamps[index], allocated, index + 1,
                imageNumbers[allocatedImageStamps[index].Definition.Image]);
        foreach ((PdfStandardFont font, int number) in fontNumbers.OrderBy(entry => entry.Value))
            objects.Add(new PdfIndirectObject(number, 0, StandardFontDictionary(font), 0));
        foreach (AllocatedEmbeddedFont font in embeddedFonts)
            AddEmbeddedFontObjects(objects, font);
        foreach ((PdfImage image, int number) in imageNumbers.OrderBy(entry => entry.Value))
            objects.Add(new PdfIndirectObject(number, 0,
                PdfImageXObjectFactory.Create(image, image.SoftMask is null ? null
                    : new PdfIndirectReference(imageNumbers[image.SoftMask], 0)), 0));
        foreach (AllocatedPage allocatedPage in allocated)
        {
            PdfDictionary resources = Dictionary();
            var resourceEntries = new List<(string Name, PdfObject Value)>();
            if (allocatedPage.Definition.Fonts.Count > 0 || allocatedPage.Definition.EmbeddedFonts.Count > 0)
            {
                var fontEntries = allocatedPage.Definition.Fonts.Select(entry =>
                    new KeyValuePair<PdfName, PdfObject>(
                        entry.Value,
                        new PdfIndirectReference(fontNumbers[entry.Key], 0))).ToList();
                fontEntries.AddRange(allocatedPage.Definition.EmbeddedFonts.Select(usage =>
                    new KeyValuePair<PdfName, PdfObject>(
                        usage.ResourceName,
                        new PdfIndirectReference(
                            embeddedFonts.Single(font => ReferenceEquals(font.Font, usage.Font)).Type0Number, 0))));
                resourceEntries.Add(("Font", new PdfDictionary(fontEntries)));
            }
            if (allocatedPage.Definition.Images.Count > 0)
            {
                var imageEntries = allocatedPage.Definition.Images.Select(entry =>
                    new KeyValuePair<PdfName, PdfObject>(entry.Value,
                        new PdfIndirectReference(imageNumbers[entry.Key], 0)));
                resourceEntries.Add(("XObject", new PdfDictionary(imageEntries)));
            }
            resources = Dictionary(resourceEntries.ToArray());
            var entries = new List<(string Name, PdfObject Value)>
            {
                ("Type", Name("Page")),
                ("Parent", new PdfIndirectReference(pagesNumber, 0)),
                ("MediaBox", new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    Number(allocatedPage.Definition.Width), Number(allocatedPage.Definition.Height)])),
                ("Resources", resources)
            };
            if (allocatedPage.ContentNumber.HasValue)
                entries.Add(("Contents", new PdfIndirectReference(allocatedPage.ContentNumber.Value, 0)));
            if (allocatedPage.AnnotationNumbers.Length > 0)
                entries.Add(("Annots", new PdfArray(allocatedPage.AnnotationNumbers.Select(number =>
                    (PdfObject)new PdfIndirectReference(number, 0)))));
            objects.Add(new PdfIndirectObject(
                allocatedPage.PageNumber, 0, Dictionary(entries.ToArray()), 0));

            if (allocatedPage.ContentNumber.HasValue)
            {
                objects.Add(new PdfIndirectObject(
                    allocatedPage.ContentNumber.Value,
                    0,
                    new PdfStream(Dictionary(), allocatedPage.Definition.Content),
                    0));
            }
            for (int index = 0; index < allocatedPage.Definition.Links.Count; index++)
            {
                LinkDefinition link = allocatedPage.Definition.Links[index];
                var annotationEntries = new List<(string Name, PdfObject Value)>
                {
                    ("Type", Name("Annot")),
                    ("Subtype", Name("Link")),
                    ("Rect", new PdfArray([
                        Number(link.X), Number(link.Y),
                        Number(link.X + link.Width), Number(link.Y + link.Height)])),
                    ("Border", new PdfArray([
                        new PdfInteger(0), new PdfInteger(0), new PdfInteger(0)]))
                };
                if (link is UriLinkDefinition uri)
                {
                    annotationEntries.Add(("A", Dictionary(
                        ("S", Name("URI")),
                        ("URI", new PdfString(Encoding.UTF8.GetBytes(uri.Uri), PdfStringForm.Literal)))));
                }
                else if (link is PageLinkDefinition pageLink)
                {
                    annotationEntries.Add(("Dest", new PdfArray([
                        new PdfIndirectReference(allocated[pageLink.DestinationPageIndex].PageNumber, 0),
                        Name("Fit")])));
                }
                objects.Add(new PdfIndirectObject(
                    allocatedPage.AnnotationNumbers[index], 0,
                    Dictionary(annotationEntries.ToArray()), 0));
            }
        }

        return Assemble(objects, catalogNumber, infoNumber);
    }

    private byte[] Assemble(List<PdfIndirectObject> objects, int rootNumber, int? infoNumber)
    {
        using var output = new MemoryStream();
        WriteAscii(output, $"%PDF-{Version}\n");
        output.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);
        var offsets = new List<(int Number, int Offset)>(objects.Count);
        foreach (PdfIndirectObject value in objects.OrderBy(value => value.ObjectNumber))
        {
            int offset = checked((int)output.Position);
            offsets.Add((value.ObjectNumber, offset));
            PdfObjectWriter.Write(output, value);
        }

        int xrefOffset = checked((int)output.Position);
        output.Write("xref\n0 1\n0000000000 65535 f \n"u8);
        foreach ((int number, int offset) in offsets)
            WriteAscii(output, $"{number} 1\n{offset:0000000000} 00000 n \n");
        output.Write("trailer\n"u8);
        byte[] identifier = DocumentIdentifier(objects);
        var trailerEntries = new List<KeyValuePair<PdfName, PdfObject>>
        {
            new(SizeName, new PdfInteger(objects.Max(item => item.ObjectNumber) + 1)),
            new(RootName, new PdfIndirectReference(rootNumber, 0)),
            new(Name("ID"), new PdfArray([
                new PdfString(identifier, PdfStringForm.Hexadecimal),
                new PdfString(identifier, PdfStringForm.Hexadecimal)]))
        };
        if (infoNumber.HasValue)
            trailerEntries.Add(new(Name("Info"), new PdfIndirectReference(infoNumber.Value, 0)));
        PdfObjectWriter.Write(output, new PdfDictionary(trailerEntries));
        output.Write("\nstartxref\n"u8);
        WriteAscii(output, xrefOffset.ToString(CultureInfo.InvariantCulture));
        output.Write("\n%%EOF\n"u8);
        return output.ToArray();
    }

    private static PdfObject Number(double value) =>
        value == Math.Truncate(value) && value is >= long.MinValue and <= long.MaxValue
            ? new PdfInteger((long)value)
            : new PdfReal(value);

    private static PdfDictionary StandardFontDictionary(PdfStandardFont font)
    {
        string baseName = font switch
        {
            PdfStandardFont.Helvetica => "Helvetica",
            PdfStandardFont.HelveticaBold => "Helvetica-Bold",
            PdfStandardFont.HelveticaOblique => "Helvetica-Oblique",
            PdfStandardFont.HelveticaBoldOblique => "Helvetica-BoldOblique",
            PdfStandardFont.TimesRoman => "Times-Roman",
            PdfStandardFont.TimesBold => "Times-Bold",
            PdfStandardFont.TimesItalic => "Times-Italic",
            PdfStandardFont.TimesBoldItalic => "Times-BoldItalic",
            PdfStandardFont.Courier => "Courier",
            PdfStandardFont.CourierBold => "Courier-Bold",
            PdfStandardFont.CourierOblique => "Courier-Oblique",
            PdfStandardFont.CourierBoldOblique => "Courier-BoldOblique",
            PdfStandardFont.Symbol => "Symbol",
            PdfStandardFont.ZapfDingbats => "ZapfDingbats",
            _ => throw new ArgumentOutOfRangeException(nameof(font))
        };
        var entries = new List<(string Name, PdfObject Value)>
        {
            ("Type", Name("Font")),
            ("Subtype", Name("Type1")),
            ("BaseFont", Name(baseName))
        };
        if (font is not PdfStandardFont.Symbol and not PdfStandardFont.ZapfDingbats)
            entries.Add(("Encoding", Name("WinAnsiEncoding")));
        return Dictionary(entries.ToArray());
    }

    private static void AddAttachmentObjects(
        ICollection<PdfIndirectObject> objects, AllocatedAttachment allocated)
    {
        AttachmentDefinition attachment = allocated.Definition;
        var parameterEntries = new List<(string Name, PdfObject Value)>
        {
            ("Size", new PdfInteger(attachment.Data.Length))
        };
        if (attachment.ModificationDate.HasValue)
            parameterEntries.Add(("ModDate", Latin1String(PdfDate(attachment.ModificationDate.Value))));
        objects.Add(new PdfIndirectObject(allocated.EmbeddedFileNumber, 0,
            new PdfStream(Dictionary(
                ("Type", Name("EmbeddedFile")),
                ("Subtype", Name(attachment.MimeType)),
                ("Params", Dictionary(parameterEntries.ToArray()))), attachment.Data), 0));

        var fileEntries = new List<(string Name, PdfObject Value)>
        {
            ("Type", Name("Filespec")),
            ("F", UnicodeString(attachment.FileName)),
            ("UF", UnicodeString(attachment.FileName)),
            ("EF", Dictionary(
                ("F", new PdfIndirectReference(allocated.EmbeddedFileNumber, 0)),
                ("UF", new PdfIndirectReference(allocated.EmbeddedFileNumber, 0)))),
            ("AFRelationship", Name(attachment.Relationship.ToString()))
        };
        if (!string.IsNullOrEmpty(attachment.Description))
            fileEntries.Add(("Desc", UnicodeString(attachment.Description)));
        objects.Add(new PdfIndirectObject(allocated.FileSpecificationNumber, 0,
            Dictionary(fileEntries.ToArray()), 0));
    }

    private static void AddTextFieldObjects(
        ICollection<PdfIndirectObject> objects,
        AllocatedTextField allocatedField,
        IReadOnlyList<AllocatedPage> pages,
        PdfName fontResource,
        int fontNumber)
    {
        TextFieldDefinition field = allocatedField.Definition;
        int pageNumber = pages[field.PageIndex].PageNumber;
        PdfString defaultAppearance = Latin1String(
            $"{NameToken(fontResource)} {FormatNumber(field.FontSize)} Tf 0 g");
        var fieldEntries = new List<(string Name, PdfObject Value)>
        {
                ("Type", Name("Annot")),
                ("Subtype", Name("Widget")),
                ("FT", Name("Tx")),
                ("T", UnicodeString(field.Name)),
                ("V", UnicodeString(field.Value)),
                ("Rect", new PdfArray([
                    Number(field.X), Number(field.Y),
                    Number(field.X + field.Width), Number(field.Y + field.Height)])),
                ("P", new PdfIndirectReference(pageNumber, 0)),
                ("F", new PdfInteger(4)),
                ("DA", defaultAppearance),
                ("MK", Dictionary(
                    ("BG", new PdfArray([new PdfInteger(1), new PdfInteger(1), new PdfInteger(1)])),
                    ("BC", new PdfArray([new PdfInteger(0), new PdfInteger(0), new PdfInteger(0)])))),
                ("BS", Dictionary(("W", new PdfInteger(1)), ("S", Name("S")))),
                ("AP", Dictionary(("N", new PdfIndirectReference(allocatedField.AppearanceNumber, 0))))
        };
        int flags = TextFieldFlags(field.Options);
        if (flags != 0)
            fieldEntries.Add(("Ff", new PdfInteger(flags)));
        if (field.Options.MaximumLength.HasValue)
            fieldEntries.Add(("MaxLen", new PdfInteger(field.Options.MaximumLength.Value)));
        objects.Add(new PdfIndirectObject(allocatedField.FieldNumber, 0,
            Dictionary(fieldEntries.ToArray()), 0));

        byte[] appearance = BuildSimpleTextAppearance(
            field.Width, field.Height, field.FontSize, field.Value,
            fontResource, field.EmbeddedFont);
        objects.Add(new PdfIndirectObject(allocatedField.AppearanceNumber, 0,
            new PdfStream(Dictionary(
                ("Type", Name("XObject")),
                ("Subtype", Name("Form")),
                ("FormType", new PdfInteger(1)),
                ("BBox", new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    Number(field.Width), Number(field.Height)])),
                ("Resources", Dictionary(("Font", new PdfDictionary([
                    new KeyValuePair<PdfName, PdfObject>(
                        fontResource, new PdfIndirectReference(fontNumber, 0))]))))),
                appearance), 0));
    }

    private static int TextFieldFlags(PdfTextFieldOptions options)
    {
        int flags = 0;
        if (options.ReadOnly) flags |= 1;
        if (options.Required) flags |= 1 << 1;
        if (options.Multiline) flags |= 1 << 12;
        if (options.Password) flags |= 1 << 13;
        if (options.Comb) flags |= 1 << 24;
        return flags;
    }

    private static void AddCheckBoxObjects(
        ICollection<PdfIndirectObject> objects,
        AllocatedCheckBox allocatedField,
        IReadOnlyList<AllocatedPage> pages)
    {
        CheckBoxDefinition field = allocatedField.Definition;
        PdfName onState = Name(field.ExportValue);
        PdfName currentState = field.IsChecked ? onState : Name("Off");
        objects.Add(new PdfIndirectObject(allocatedField.FieldNumber, 0,
            Dictionary(
                ("Type", Name("Annot")),
                ("Subtype", Name("Widget")),
                ("FT", Name("Btn")),
                ("T", UnicodeString(field.Name)),
                ("V", currentState),
                ("AS", currentState),
                ("Rect", new PdfArray([
                    Number(field.X), Number(field.Y),
                    Number(field.X + field.Width), Number(field.Y + field.Height)])),
                ("P", new PdfIndirectReference(pages[field.PageIndex].PageNumber, 0)),
                ("F", new PdfInteger(4)),
                ("MK", Dictionary(
                    ("BG", new PdfArray([new PdfInteger(1), new PdfInteger(1), new PdfInteger(1)])),
                    ("BC", new PdfArray([new PdfInteger(0), new PdfInteger(0), new PdfInteger(0)])))),
                ("BS", Dictionary(("W", new PdfInteger(1)), ("S", Name("S")))),
                ("AP", Dictionary(("N", new PdfDictionary([
                    new KeyValuePair<PdfName, PdfObject>(
                        Name("Off"), new PdfIndirectReference(allocatedField.OffAppearanceNumber, 0)),
                    new KeyValuePair<PdfName, PdfObject>(
                        onState, new PdfIndirectReference(allocatedField.OnAppearanceNumber, 0))]))))), 0));

        objects.Add(new PdfIndirectObject(allocatedField.OffAppearanceNumber, 0,
            CheckBoxAppearance(field, isChecked: false), 0));
        objects.Add(new PdfIndirectObject(allocatedField.OnAppearanceNumber, 0,
            CheckBoxAppearance(field, isChecked: true), 0));
    }

    private static PdfStream CheckBoxAppearance(CheckBoxDefinition field, bool isChecked)
    {
        using var output = new MemoryStream();
        WriteAscii(output, $"q\n1 g\n0 0 {FormatNumber(field.Width)} {FormatNumber(field.Height)} re\nf\n");
        WriteAscii(output, $"0 G\n1 w\n0.5 0.5 {FormatNumber(Math.Max(0, field.Width - 1))} {FormatNumber(Math.Max(0, field.Height - 1))} re\nS\n");
        if (isChecked)
        {
            double inset = Math.Min(3, Math.Min(field.Width, field.Height) / 4);
            WriteAscii(output,
                $"2 w\n{FormatNumber(inset)} {FormatNumber(inset)} m\n" +
                $"{FormatNumber(field.Width - inset)} {FormatNumber(field.Height - inset)} l\n" +
                $"{FormatNumber(field.Width - inset)} {FormatNumber(inset)} m\n" +
                $"{FormatNumber(inset)} {FormatNumber(field.Height - inset)} l\nS\n");
        }
        output.Write("Q\n"u8);
        return new PdfStream(Dictionary(
            ("Type", Name("XObject")),
            ("Subtype", Name("Form")),
            ("FormType", new PdfInteger(1)),
            ("BBox", new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                Number(field.Width), Number(field.Height)])),
            ("Resources", Dictionary())), output.ToArray());
    }

    private static void AddRadioGroupObjects(
        ICollection<PdfIndirectObject> objects,
        AllocatedRadioGroup allocatedGroup,
        IReadOnlyList<AllocatedPage> pages)
    {
        RadioGroupDefinition group = allocatedGroup.Definition;
        PdfName selected = Name(group.SelectedValue ?? "Off");
        objects.Add(new PdfIndirectObject(allocatedGroup.ParentNumber, 0,
            Dictionary(
                ("FT", Name("Btn")),
                ("Ff", new PdfInteger(1 << 15)),
                ("T", UnicodeString(group.Name)),
                ("V", selected),
                ("Kids", new PdfArray(allocatedGroup.Widgets.Select(widget =>
                    (PdfObject)new PdfIndirectReference(widget.WidgetNumber, 0))))), 0));

        foreach (AllocatedRadioWidget allocatedWidget in allocatedGroup.Widgets)
        {
            PdfRadioButtonOption option = allocatedWidget.Option;
            PdfName onState = Name(option.ExportValue);
            PdfName appearanceState = group.SelectedValue == option.ExportValue ? onState : Name("Off");
            objects.Add(new PdfIndirectObject(allocatedWidget.WidgetNumber, 0,
                Dictionary(
                    ("Type", Name("Annot")),
                    ("Subtype", Name("Widget")),
                    ("Parent", new PdfIndirectReference(allocatedGroup.ParentNumber, 0)),
                    ("Rect", new PdfArray([
                        Number(option.X), Number(option.Y),
                        Number(option.X + option.Width), Number(option.Y + option.Height)])),
                    ("P", new PdfIndirectReference(pages[option.PageIndex].PageNumber, 0)),
                    ("F", new PdfInteger(4)),
                    ("AS", appearanceState),
                    ("AP", Dictionary(("N", new PdfDictionary([
                        new KeyValuePair<PdfName, PdfObject>(
                            Name("Off"), new PdfIndirectReference(allocatedWidget.OffAppearanceNumber, 0)),
                        new KeyValuePair<PdfName, PdfObject>(
                            onState, new PdfIndirectReference(allocatedWidget.OnAppearanceNumber, 0))]))))), 0));
            objects.Add(new PdfIndirectObject(allocatedWidget.OffAppearanceNumber, 0,
                RadioAppearance(option, selected: false), 0));
            objects.Add(new PdfIndirectObject(allocatedWidget.OnAppearanceNumber, 0,
                RadioAppearance(option, selected: true), 0));
        }
    }

    private static PdfStream RadioAppearance(PdfRadioButtonOption option, bool selected)
    {
        using var output = new MemoryStream();
        double centerX = option.Width / 2;
        double centerY = option.Height / 2;
        double radius = Math.Max(0, Math.Min(option.Width, option.Height) / 2 - 0.75);
        output.Write("q\n1 g\n"u8);
        WriteCircle(output, centerX, centerY, radius);
        output.Write("B\n"u8);
        if (selected)
        {
            output.Write("0 g\n"u8);
            WriteCircle(output, centerX, centerY, radius * 0.48);
            output.Write("f\n"u8);
        }
        output.Write("Q\n"u8);
        return new PdfStream(Dictionary(
            ("Type", Name("XObject")),
            ("Subtype", Name("Form")),
            ("FormType", new PdfInteger(1)),
            ("BBox", new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                Number(option.Width), Number(option.Height)])),
            ("Resources", Dictionary())), output.ToArray());
    }

    private static void WriteCircle(Stream output, double x, double y, double radius)
    {
        const double kappa = 0.5522847498307936;
        double control = radius * kappa;
        WriteAscii(output, $"{FormatNumber(x + radius)} {FormatNumber(y)} m\n");
        WriteAscii(output, $"{FormatNumber(x + radius)} {FormatNumber(y + control)} {FormatNumber(x + control)} {FormatNumber(y + radius)} {FormatNumber(x)} {FormatNumber(y + radius)} c\n");
        WriteAscii(output, $"{FormatNumber(x - control)} {FormatNumber(y + radius)} {FormatNumber(x - radius)} {FormatNumber(y + control)} {FormatNumber(x - radius)} {FormatNumber(y)} c\n");
        WriteAscii(output, $"{FormatNumber(x - radius)} {FormatNumber(y - control)} {FormatNumber(x - control)} {FormatNumber(y - radius)} {FormatNumber(x)} {FormatNumber(y - radius)} c\n");
        WriteAscii(output, $"{FormatNumber(x + control)} {FormatNumber(y - radius)} {FormatNumber(x + radius)} {FormatNumber(y - control)} {FormatNumber(x + radius)} {FormatNumber(y)} c\nh\n");
    }

    private static void AddChoiceFieldObjects(
        ICollection<PdfIndirectObject> objects,
        AllocatedChoiceField allocatedField,
        IReadOnlyList<AllocatedPage> pages,
        PdfName fontResource,
        int fontNumber)
    {
        ChoiceFieldDefinition field = allocatedField.Definition;
        int flags = (1 << 17) | (field.Editable ? 1 << 18 : 0);
        objects.Add(new PdfIndirectObject(allocatedField.FieldNumber, 0,
            Dictionary(
                ("Type", Name("Annot")),
                ("Subtype", Name("Widget")),
                ("FT", Name("Ch")),
                ("Ff", new PdfInteger(flags)),
                ("T", UnicodeString(field.Name)),
                ("V", UnicodeString(field.SelectedValue)),
                ("Opt", new PdfArray(field.Options.Select(value => (PdfObject)UnicodeString(value)))),
                ("Rect", new PdfArray([
                    Number(field.X), Number(field.Y),
                    Number(field.X + field.Width), Number(field.Y + field.Height)])),
                ("P", new PdfIndirectReference(pages[field.PageIndex].PageNumber, 0)),
                ("F", new PdfInteger(4)),
                ("DA", Latin1String($"{NameToken(fontResource)} {FormatNumber(field.FontSize)} Tf 0 g")),
                ("AP", Dictionary(("N", new PdfIndirectReference(allocatedField.AppearanceNumber, 0))))), 0));

        byte[] appearance = BuildSimpleTextAppearance(
            field.Width, field.Height, field.FontSize, field.SelectedValue,
            fontResource, field.EmbeddedFont);
        objects.Add(new PdfIndirectObject(allocatedField.AppearanceNumber, 0,
            new PdfStream(Dictionary(
                ("Type", Name("XObject")),
                ("Subtype", Name("Form")),
                ("FormType", new PdfInteger(1)),
                ("BBox", new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    Number(field.Width), Number(field.Height)])),
                ("Resources", Dictionary(("Font", new PdfDictionary([
                    new KeyValuePair<PdfName, PdfObject>(
                        fontResource, new PdfIndirectReference(fontNumber, 0))]))))),
                appearance), 0));
    }

    private static byte[] BuildSimpleTextAppearance(
        double width,
        double height,
        double fontSize,
        string value,
        PdfName fontResource,
        TrueTypeFont? embeddedFont)
    {
        using var output = new MemoryStream();
        WriteAscii(output, $"q\n1 1 1 rg\n0 0 {FormatNumber(width)} {FormatNumber(height)} re\nf\n");
        WriteAscii(output, $"0 G\n1 w\n0.5 0.5 {FormatNumber(Math.Max(0, width - 1))} {FormatNumber(Math.Max(0, height - 1))} re\nS\n");
        WriteAscii(output, $"BT\n{NameToken(fontResource)} {FormatNumber(fontSize)} Tf\n0 g\n3 {FormatNumber(Math.Max(1, (height - fontSize) / 2))} Td\n");
        WriteShownText(output, value, embeddedFont);
        output.Write("ET\nQ\n"u8);
        return output.ToArray();
    }

    private static void AddOutputIntentObjects(
        ICollection<PdfIndirectObject> objects,
        OutputIntentDefinition definition,
        int profileNumber,
        int outputIntentNumber)
    {
        objects.Add(new PdfIndirectObject(profileNumber, 0,
            new PdfStream(Dictionary(
                ("N", new PdfInteger(definition.Profile.ComponentCount))),
                definition.Profile.Data.Span), 0));
        var entries = new List<(string Name, PdfObject Value)>
        {
            ("Type", Name("OutputIntent")),
            ("S", Name("GTS_PDFA1")),
            ("OutputConditionIdentifier", UnicodeString(definition.Identifier)),
            ("DestOutputProfile", new PdfIndirectReference(profileNumber, 0))
        };
        Add("OutputCondition", definition.Condition);
        Add("RegistryName", definition.RegistryName);
        Add("Info", definition.Information);
        objects.Add(new PdfIndirectObject(
            outputIntentNumber, 0, Dictionary(entries.ToArray()), 0));

        void Add(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value)) entries.Add((name, UnicodeString(value)));
        }
    }

    private static void AddTextNoteObjects(
        ICollection<PdfIndirectObject> objects,
        AllocatedTextNote allocated,
        IReadOnlyList<AllocatedPage> pages,
        int sequence)
    {
        TextNoteDefinition note = allocated.Definition;
        objects.Add(new PdfIndirectObject(allocated.AnnotationNumber, 0,
            Dictionary(
                ("Type", Name("Annot")),
                ("Subtype", Name("Text")),
                ("Rect", new PdfArray([
                    Number(note.X), Number(note.Y),
                    Number(note.X + note.Size), Number(note.Y + note.Size)])),
                ("P", new PdfIndirectReference(pages[note.PageIndex].PageNumber, 0)),
                ("F", new PdfInteger(4)),
                ("Contents", UnicodeString(note.Contents)),
                ("NM", Latin1String($"KillerPDF-Note-{sequence}")),
                ("Name", Name("Note")),
                ("Open", new PdfBoolean(note.Open)),
                ("C", ColorArray(note.Color)),
                ("AP", Dictionary(("N", new PdfIndirectReference(allocated.AppearanceNumber, 0))))), 0));

        using var appearance = new MemoryStream();
        WriteAscii(appearance,
            $"q\n{ColorOperands(note.Color)} rg\n0 0 {FormatNumber(note.Size)} {FormatNumber(note.Size)} re\nf\n" +
            $"0 G\n1 w\n0.5 0.5 {FormatNumber(Math.Max(0, note.Size - 1))} {FormatNumber(Math.Max(0, note.Size - 1))} re\nS\n");
        double fold = note.Size * 0.3;
        WriteAscii(appearance,
            $"{FormatNumber(note.Size - fold)} {FormatNumber(note.Size)} m\n" +
            $"{FormatNumber(note.Size - fold)} {FormatNumber(note.Size - fold)} l\n" +
            $"{FormatNumber(note.Size)} {FormatNumber(note.Size - fold)} l\nS\n" +
            $"{FormatNumber(note.Size * 0.22)} {FormatNumber(note.Size * 0.58)} m\n" +
            $"{FormatNumber(note.Size * 0.7)} {FormatNumber(note.Size * 0.58)} l\n" +
            $"{FormatNumber(note.Size * 0.22)} {FormatNumber(note.Size * 0.38)} m\n" +
            $"{FormatNumber(note.Size * 0.62)} {FormatNumber(note.Size * 0.38)} l\nS\nQ\n");
        objects.Add(new PdfIndirectObject(allocated.AppearanceNumber, 0,
            AnnotationAppearance(note.Size, note.Size, Dictionary(), appearance.ToArray()), 0));
    }

    private static void AddTextMarkupObjects(
        ICollection<PdfIndirectObject> objects,
        AllocatedTextMarkup allocated,
        IReadOnlyList<AllocatedPage> pages,
        int sequence)
    {
        TextMarkupDefinition highlight = allocated.Definition;
        var entries = new List<(string Name, PdfObject Value)>
        {
            ("Type", Name("Annot")),
            ("Subtype", Name(highlight.Type.ToString())),
            ("Rect", new PdfArray([
                Number(highlight.X), Number(highlight.Y),
                Number(highlight.X + highlight.Width), Number(highlight.Y + highlight.Height)])),
            ("QuadPoints", new PdfArray([
                Number(highlight.X), Number(highlight.Y + highlight.Height),
                Number(highlight.X + highlight.Width), Number(highlight.Y + highlight.Height),
                Number(highlight.X), Number(highlight.Y),
                Number(highlight.X + highlight.Width), Number(highlight.Y)])),
            ("P", new PdfIndirectReference(pages[highlight.PageIndex].PageNumber, 0)),
            ("F", new PdfInteger(4)),
            ("NM", Latin1String($"KillerPDF-{highlight.Type}-{sequence}")),
            ("C", ColorArray(highlight.Color)),
            ("CA", new PdfReal(highlight.Opacity)),
            ("AP", Dictionary(("N", new PdfIndirectReference(allocated.AppearanceNumber, 0))))
        };
        if (!string.IsNullOrEmpty(highlight.Contents))
            entries.Add(("Contents", UnicodeString(highlight.Contents)));
        objects.Add(new PdfIndirectObject(
            allocated.AnnotationNumber, 0, Dictionary(entries.ToArray()), 0));

        var graphicsState = Dictionary(
            ("Type", Name("ExtGState")),
            ("ca", new PdfReal(highlight.Opacity)),
            ("CA", new PdfReal(highlight.Opacity)),
            ("BM", Name("Multiply")));
        PdfDictionary resources = Dictionary(("ExtGState", new PdfDictionary([
            new KeyValuePair<PdfName, PdfObject>(Name("GS1"), graphicsState)])));
        byte[] appearance = TextMarkupAppearance(highlight);
        objects.Add(new PdfIndirectObject(allocated.AppearanceNumber, 0,
            AnnotationAppearance(highlight.Width, highlight.Height, resources, appearance), 0));
    }

    private static byte[] TextMarkupAppearance(TextMarkupDefinition markup)
    {
        string color = ColorOperands(markup.Color);
        string width = FormatNumber(markup.Width);
        string height = FormatNumber(markup.Height);
        string drawing = markup.Type switch
        {
            PdfTextMarkupType.Highlight => $"{color} rg\n0 0 {width} {height} re\nf\n",
            PdfTextMarkupType.Underline => MarkupLine(markup, markup.Height * 0.08),
            PdfTextMarkupType.StrikeOut => MarkupLine(markup, markup.Height * 0.48),
            PdfTextMarkupType.Squiggly => SquigglyLine(markup),
            _ => throw new ArgumentOutOfRangeException(nameof(markup.Type))
        };
        return Encoding.ASCII.GetBytes($"q\n/GS1 gs\n{drawing}Q\n");
    }

    private static string MarkupLine(TextMarkupDefinition markup, double y)
    {
        double lineWidth = Math.Max(0.75, markup.Height * 0.07);
        return $"{ColorOperands(markup.Color)} RG\n{FormatNumber(lineWidth)} w\n" +
            $"0 {FormatNumber(y)} m\n{FormatNumber(markup.Width)} {FormatNumber(y)} l\nS\n";
    }

    private static string SquigglyLine(TextMarkupDefinition markup)
    {
        double amplitude = Math.Max(0.75, markup.Height * 0.1);
        double step = Math.Max(1.5, amplitude * 2);
        var result = new StringBuilder(
            $"{ColorOperands(markup.Color)} RG\n{FormatNumber(Math.Max(0.75, amplitude * 0.55))} w\n0 {FormatNumber(amplitude)} m\n");
        bool high = false;
        for (double x = step; x < markup.Width; x += step)
        {
            result.Append(FormatNumber(x)).Append(' ')
                .Append(FormatNumber(high ? amplitude * 2 : 0)).Append(" l\n");
            high = !high;
        }
        result.Append(FormatNumber(markup.Width)).Append(' ')
            .Append(FormatNumber(high ? amplitude * 2 : 0)).Append(" l\nS\n");
        return result.ToString();
    }

    private static PdfStream AnnotationAppearance(
        double width, double height, PdfDictionary resources, byte[] content) =>
        new(Dictionary(
            ("Type", Name("XObject")),
            ("Subtype", Name("Form")),
            ("FormType", new PdfInteger(1)),
            ("BBox", new PdfArray([
                new PdfInteger(0), new PdfInteger(0), Number(width), Number(height)])),
            ("Resources", resources)), content);

    private static PdfArray ColorArray(PdfRgbColor color) =>
        new([Number(color.Red), Number(color.Green), Number(color.Blue)]);

    private static string ColorOperands(PdfRgbColor color) =>
        $"{FormatNumber(color.Red)} {FormatNumber(color.Green)} {FormatNumber(color.Blue)}";

    private static string FormatNumber(double value) =>
        Encoding.ASCII.GetString(PdfObjectWriter.Write(Number(value)));

    private static string NameToken(PdfName value) =>
        Encoding.ASCII.GetString(PdfObjectWriter.Write(value));

    private static void AddTextMappings(EmbeddedFontUsage usage, string value)
    {
        foreach (Rune rune in value.EnumerateRunes())
            usage.AddMapping(usage.Font.GetGlyphId(rune.Value), rune.Value);
    }

    private static void AddDrawableTextMappings(EmbeddedFontUsage usage, string value)
    {
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (rune.Value is '\r' or '\n')
                continue;
            usage.AddMapping(usage.Font.GetGlyphId(rune.Value), rune.Value);
        }
    }

    private static void WriteShownText(Stream output, string value, TrueTypeFont? embeddedFont)
    {
        if (embeddedFont is null)
        {
            PdfObjectWriter.Write(output,
                new PdfString(Encoding.Latin1.GetBytes(value), PdfStringForm.Literal));
        }
        else
        {
            output.WriteByte((byte)'<');
            foreach (Rune rune in value.EnumerateRunes())
            {
                ushort glyph = embeddedFont.GetGlyphId(rune.Value);
                WriteAscii(output, glyph.ToString("X4", CultureInfo.InvariantCulture));
            }
            output.WriteByte((byte)'>');
        }
        output.Write(" Tj\n"u8);
    }

    private static (PdfName Resource, int ObjectNumber) FormFontBinding(
        TrueTypeFont? embeddedFont,
        IReadOnlyDictionary<PdfStandardFont, int> standardFonts,
        IReadOnlyDictionary<TrueTypeFont, PdfName> formFontResources,
        IReadOnlyList<AllocatedEmbeddedFont> embeddedFonts)
    {
        if (embeddedFont is null)
            return (Name("Helv"), standardFonts[PdfStandardFont.Helvetica]);
        return (formFontResources[embeddedFont],
            embeddedFonts.Single(value => ReferenceEquals(value.Font, embeddedFont)).Type0Number);
    }

    private void ValidateUniqueFieldName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A form field name cannot be empty.", nameof(name));
        if (_textFields.Any(field => string.Equals(field.Name, name, StringComparison.Ordinal))
            || _checkBoxes.Any(field => string.Equals(field.Name, name, StringComparison.Ordinal))
            || _radioGroups.Any(field => string.Equals(field.Name, name, StringComparison.Ordinal))
            || _choiceFields.Any(field => string.Equals(field.Name, name, StringComparison.Ordinal)))
            throw new ArgumentException("Form field names must be unique.", nameof(name));
    }

    private static void ValidateFormFontText(TrueTypeFont font, string value, string parameterName)
    {
        if (!font.EmbeddingAllowed)
            throw new ArgumentException($"Font {font.PostScriptName} prohibits PDF embedding.", parameterName);
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (font.GetGlyphId(rune.Value) == 0 && rune.Value != 0)
                throw new ArgumentException(
                    $"Font {font.PostScriptName} has no glyph for U+{rune.Value:X4}.", parameterName);
        }
    }

    private static void AddEmbeddedFontObjects(
        ICollection<PdfIndirectObject> objects, AllocatedEmbeddedFont allocated)
    {
        var type0 = new PdfIndirectReference(allocated.Type0Number, 0);
        var cidFont = new PdfIndirectReference(allocated.CidFontNumber, 0);
        var descriptor = new PdfIndirectReference(allocated.DescriptorNumber, 0);
        var fontFile = new PdfIndirectReference(allocated.FontFileNumber, 0);
        var toUnicode = new PdfIndirectReference(allocated.ToUnicodeNumber, 0);
        EmbeddedTrueTypeFontObjects values = PdfEmbeddedTrueTypeFontFactory.Create(
            allocated.Font, allocated.Mappings, type0, cidFont, descriptor, fontFile, toUnicode);
        objects.Add(new PdfIndirectObject(allocated.FontFileNumber, 0, values.FontFile, 0));
        objects.Add(new PdfIndirectObject(allocated.DescriptorNumber, 0, values.Descriptor, 0));
        objects.Add(new PdfIndirectObject(allocated.CidFontNumber, 0, values.CidFont, 0));
        objects.Add(new PdfIndirectObject(allocated.ToUnicodeNumber, 0, values.ToUnicode, 0));
        objects.Add(new PdfIndirectObject(allocated.Type0Number, 0, values.Type0, 0));
    }

    private static PdfString Latin1String(string value) =>
        new(Encoding.Latin1.GetBytes(value), PdfStringForm.Literal);

    private static PdfString UnicodeString(string value)
    {
        byte[] text = Encoding.BigEndianUnicode.GetBytes(value);
        byte[] bytes = new byte[text.Length + 2];
        bytes[0] = 0xFE;
        bytes[1] = 0xFF;
        text.CopyTo(bytes, 2);
        return new PdfString(bytes, PdfStringForm.Hexadecimal);
    }

    private static PdfDictionary BuildInfo(PdfDocumentMetadata metadata)
    {
        var entries = new List<(string Name, PdfObject Value)>();
        Add("Title", metadata.Title);
        Add("Author", metadata.Author);
        Add("Subject", metadata.Subject);
        Add("Keywords", metadata.Keywords);
        Add("Creator", metadata.Creator);
        Add("Producer", metadata.Producer);
        AddDate("CreationDate", metadata.CreationDate);
        AddDate("ModDate", metadata.ModificationDate);
        return Dictionary(entries.ToArray());

        void Add(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value)) entries.Add((name, UnicodeString(value)));
        }
        void AddDate(string name, DateTimeOffset? value)
        {
            if (value.HasValue) entries.Add((name, Latin1String(PdfDate(value.Value))));
        }
    }

    private static byte[] BuildXmp(PdfDocumentMetadata metadata, bool pdfA4)
    {
        using var output = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.None
        };
        using (XmlWriter xml = XmlWriter.Create(output, settings))
        {
            xml.WriteProcessingInstruction("xpacket", "begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"");
            xml.WriteStartElement("x", "xmpmeta", "adobe:ns:meta/");
            xml.WriteStartElement("rdf", "RDF", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
            xml.WriteStartElement("rdf", "Description", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
            xml.WriteAttributeString("rdf", "about", null, string.Empty);
            WriteSimple("dc", "format", "http://purl.org/dc/elements/1.1/", "application/pdf");
            WriteAlternative("title", metadata.Title);
            WriteSequence("creator", metadata.Author);
            WriteAlternative("description", metadata.Subject);
            WriteSimple("pdf", "Keywords", "http://ns.adobe.com/pdf/1.3/", metadata.Keywords);
            WriteSimple("pdf", "Producer", "http://ns.adobe.com/pdf/1.3/", metadata.Producer);
            WriteSimple("xmp", "CreatorTool", "http://ns.adobe.com/xap/1.0/", metadata.Creator);
            WriteSimple("xmp", "CreateDate", "http://ns.adobe.com/xap/1.0/", XmpDate(metadata.CreationDate));
            WriteSimple("xmp", "ModifyDate", "http://ns.adobe.com/xap/1.0/", XmpDate(metadata.ModificationDate));
            if (pdfA4)
            {
                WriteSimple("pdfaid", "part", "http://www.aiim.org/pdfa/ns/id/", "4");
                WriteSimple("pdfaid", "rev", "http://www.aiim.org/pdfa/ns/id/", "2020");
            }
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteProcessingInstruction("xpacket", "end=\"w\"");

            void WriteSimple(string prefix, string name, string ns, string? value)
            {
                if (!string.IsNullOrEmpty(value)) xml.WriteElementString(prefix, name, ns, value);
            }
            void WriteAlternative(string name, string? value)
            {
                if (string.IsNullOrEmpty(value)) return;
                xml.WriteStartElement("dc", name, "http://purl.org/dc/elements/1.1/");
                xml.WriteStartElement("rdf", "Alt", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
                xml.WriteStartElement("rdf", "li", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
                xml.WriteAttributeString("xml", "lang", null, "x-default");
                xml.WriteString(value);
                xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndElement();
            }
            void WriteSequence(string name, string? value)
            {
                if (string.IsNullOrEmpty(value)) return;
                xml.WriteStartElement("dc", name, "http://purl.org/dc/elements/1.1/");
                xml.WriteStartElement("rdf", "Seq", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
                xml.WriteElementString("rdf", "li", "http://www.w3.org/1999/02/22-rdf-syntax-ns#", value);
                xml.WriteEndElement(); xml.WriteEndElement();
            }
        }
        return output.ToArray();
    }

    private static byte[] DocumentIdentifier(IEnumerable<PdfIndirectObject> objects)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (PdfIndirectObject value in objects.OrderBy(value => value.ObjectNumber))
            hash.AppendData(PdfObjectWriter.Write(value));
        return hash.GetHashAndReset()[..16];
    }

    private static string PdfDate(DateTimeOffset value)
    {
        TimeSpan offset = value.Offset;
        char sign = offset < TimeSpan.Zero ? '-' : '+';
        offset = offset.Duration();
        return $"D:{value:yyyyMMddHHmmss}{sign}{offset.Hours:00}'{offset.Minutes:00}'";
    }

    private static string? XmpDate(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static void ValidateDimension(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name, "Page dimensions must be positive finite numbers.");
    }

    private void ValidatePageIndex(int pageIndex, string name)
    {
        if ((uint)pageIndex >= (uint)_pages.Count)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateRectangle(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        if (!double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(y));
        if (!double.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(height) || height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
    }

    private static void WriteAscii(Stream output, string value)
    {
        foreach (char character in value)
            output.WriteByte(checked((byte)character));
    }

    private static PdfDictionary Dictionary(params (string Name, PdfObject Value)[] entries) =>
        new(entries.Select(entry => new KeyValuePair<PdfName, PdfObject>(Name(entry.Name), entry.Value)));

    private static PdfName Name(string value) => new(System.Text.Encoding.ASCII.GetBytes(value));

    private sealed record PageDefinition(
        double Width,
        double Height,
        byte[] Content,
        IReadOnlyDictionary<PdfStandardFont, PdfName> Fonts,
        IReadOnlyList<EmbeddedFontUsage> EmbeddedFonts,
        IReadOnlyDictionary<PdfImage, PdfName> Images,
        IReadOnlyList<LinkDefinition> Links);
    private sealed record AllocatedPage(
        PageDefinition Definition, int PageNumber, int? ContentNumber, int[] AnnotationNumbers);
    private abstract record LinkDefinition(double X, double Y, double Width, double Height);
    private sealed record UriLinkDefinition(
        double X, double Y, double Width, double Height, string Uri)
        : LinkDefinition(X, Y, Width, Height);
    private sealed record PageLinkDefinition(
        double X, double Y, double Width, double Height, int DestinationPageIndex)
        : LinkDefinition(X, Y, Width, Height);
    private sealed record BookmarkDefinition(string Title, int PageIndex);
    private sealed record AttachmentDefinition(
        string FileName,
        byte[] Data,
        string MimeType,
        string? Description,
        PdfAssociatedFileRelationship Relationship,
        DateTimeOffset? ModificationDate);
    private sealed record AllocatedAttachment(
        AttachmentDefinition Definition, int EmbeddedFileNumber, int FileSpecificationNumber);
    private sealed record TextFieldDefinition(
        int PageIndex, string Name, double X, double Y, double Width, double Height,
        string Value, double FontSize, PdfTextFieldOptions Options, TrueTypeFont? EmbeddedFont);
    private sealed record AllocatedTextField(
        TextFieldDefinition Definition, int FieldNumber, int AppearanceNumber);
    private sealed record CheckBoxDefinition(
        int PageIndex, string Name, double X, double Y, double Width, double Height,
        bool IsChecked, string ExportValue);
    private sealed record AllocatedCheckBox(
        CheckBoxDefinition Definition,
        int FieldNumber,
        int OffAppearanceNumber,
        int OnAppearanceNumber);
    private sealed record RadioGroupDefinition(
        string Name, IReadOnlyList<PdfRadioButtonOption> Options, string? SelectedValue);
    private sealed record AllocatedRadioGroup(
        RadioGroupDefinition Definition, int ParentNumber, IReadOnlyList<AllocatedRadioWidget> Widgets);
    private sealed record AllocatedRadioWidget(
        PdfRadioButtonOption Option,
        int WidgetNumber,
        int OffAppearanceNumber,
        int OnAppearanceNumber);
    private sealed record ChoiceFieldDefinition(
        int PageIndex, string Name, double X, double Y, double Width, double Height,
        IReadOnlyList<string> Options, string SelectedValue, bool Editable, double FontSize,
        TrueTypeFont? EmbeddedFont);
    private sealed record AllocatedChoiceField(
        ChoiceFieldDefinition Definition, int FieldNumber, int AppearanceNumber);
    private sealed record OutputIntentDefinition(
        PdfIccProfile Profile,
        string Identifier,
        string? Condition,
        string? RegistryName,
        string? Information);
    private sealed record TextNoteDefinition(
        int PageIndex, double X, double Y, double Size, string Contents,
        PdfRgbColor Color, bool Open);
    private sealed record AllocatedTextNote(
        TextNoteDefinition Definition, int AnnotationNumber, int AppearanceNumber);
    private sealed record TextMarkupDefinition(
        PdfTextMarkupType Type, int PageIndex, double X, double Y, double Width, double Height,
        string? Contents, PdfRgbColor Color, double Opacity);
    private sealed record AllocatedTextMarkup(
        TextMarkupDefinition Definition, int AnnotationNumber, int AppearanceNumber);
    private sealed record AllocatedEmbeddedFont(
        TrueTypeFont Font,
        IReadOnlyDictionary<ushort, int> Mappings,
        int Type0Number,
        int CidFontNumber,
        int DescriptorNumber,
        int FontFileNumber,
        int ToUnicodeNumber);
}
