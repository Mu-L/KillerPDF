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
    private readonly List<NamedDestinationDefinition> _namedDestinations = [];
    private readonly List<PageLabelDefinition> _pageLabels = [];
    private readonly List<StructureElementDefinition> _structureElements = [];
    private readonly List<AttachmentDefinition> _attachments = [];
    private readonly List<TextFieldDefinition> _textFields = [];
    private readonly List<CheckBoxDefinition> _checkBoxes = [];
    private readonly List<RadioGroupDefinition> _radioGroups = [];
    private readonly List<ChoiceFieldDefinition> _choiceFields = [];
    private OutputIntentDefinition? _outputIntent;
    private PdfA4Flavor _pdfA4Flavor;
    private bool _pdfUa2Conformance;
    private PdfPageLayout? _pageLayout;
    private PdfPageMode? _pageMode;
    private PdfViewerPreferences? _viewerPreferences;
    private OpenActionDefinition? _openAction;
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
        _pdfA4Flavor = PdfA4Flavor.General;
        return this;
    }

    /// <summary>Enables PDF/A-4f authoring, including associated embedded files.</summary>
    public PdfDocumentBuilder EnablePdfA4fConformance()
    {
        _pdfA4Flavor = PdfA4Flavor.EmbeddedFiles;
        return this;
    }

    /// <summary>Enables the PDF/A-4e engineering-document conformance flavour.</summary>
    public PdfDocumentBuilder EnablePdfA4eConformance()
    {
        _pdfA4Flavor = PdfA4Flavor.Engineering;
        return this;
    }

    /// <summary>Enables PDF/UA-2 identification and accessibility conformance checks.</summary>
    public PdfDocumentBuilder EnablePdfUa2Conformance()
    {
        _pdfUa2Conformance = true;
        return this;
    }

    public PdfDocumentBuilder SetPageLayout(PdfPageLayout layout)
    {
        if (!Enum.IsDefined(layout)) throw new ArgumentOutOfRangeException(nameof(layout));
        _pageLayout = layout;
        return this;
    }

    public PdfDocumentBuilder SetPageMode(PdfPageMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        _pageMode = mode;
        return this;
    }

    public PdfDocumentBuilder SetViewerPreferences(PdfViewerPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!Enum.IsDefined(preferences.ReadingDirection))
            throw new ArgumentOutOfRangeException(nameof(preferences));
        if (!Enum.IsDefined(preferences.PrintScaling))
            throw new ArgumentOutOfRangeException(nameof(preferences));
        if (!Enum.IsDefined(preferences.Duplex))
            throw new ArgumentOutOfRangeException(nameof(preferences));
        _viewerPreferences = preferences;
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
            new Dictionary<PdfImage, PdfName>(),
            new Dictionary<PdfOptionalContentGroup, PdfName>(),
            new Dictionary<PdfGraphicsState, PdfName>(),
            new Dictionary<PdfShading, PdfName>(),
            new Dictionary<PdfFormXObject, PdfName>(),
            new Dictionary<PdfTilingPattern, PdfName>(),
            new Dictionary<PdfIccProfile, PdfName>(),
            new Dictionary<PdfSpotColor, PdfName>(),
            new Dictionary<PdfLabColorSpace, PdfName>(),
            new Dictionary<PdfIndexedColorSpace, PdfName>(), [], [], content.Length > 0,
            0, 1, new Dictionary<PdfPageBox, PageBoxDefinition>(), null, null, null));
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
            content.ImageResources.ToDictionary(entry => entry.Key, entry => entry.Value),
            content.OptionalContentResources.ToDictionary(entry => entry.Key, entry => entry.Value),
            content.GraphicsStateResources.ToDictionary(entry => entry.Key, entry => entry.Value),
            content.ShadingResources.ToDictionary(entry => entry.Key, entry => entry.Value),
            content.FormResources.ToDictionary(entry => entry.Key, entry => entry.Value),
            content.PatternResources.ToDictionary(entry => entry.Key, entry => entry.Value),
            content.IccColorSpaceResources.ToDictionary(entry => entry.Key, entry => entry.Value),
            content.SpotColorResources.ToDictionary(entry => entry.Key, entry => entry.Value),
            content.LabColorSpaceResources.ToDictionary(entry => entry.Key, entry => entry.Value),
            content.IndexedColorSpaceResources.ToDictionary(entry => entry.Key, entry => entry.Value), [],
            content.MarkedContentIds.Order().ToArray(), content.HasUntaggedContent,
            0, 1, new Dictionary<PdfPageBox, PageBoxDefinition>(), null, null, null));
        return this;
    }

    /// <summary>Sets the clockwise page rotation shown by conforming viewers.</summary>
    public PdfDocumentBuilder SetPageRotation(int pageIndex, int degrees)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        if (degrees is not (0 or 90 or 180 or 270))
            throw new ArgumentOutOfRangeException(nameof(degrees),
                "Page rotation must be 0, 90, 180, or 270 degrees.");
        _pages[pageIndex] = _pages[pageIndex] with { Rotation = degrees };
        return this;
    }

    /// <summary>Sets a visible, print-production, or artwork boundary within the media box.</summary>
    public PdfDocumentBuilder SetPageBox(
        int pageIndex, PdfPageBox box, double x, double y, double width, double height)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        if (!Enum.IsDefined(box)) throw new ArgumentOutOfRangeException(nameof(box));
        ValidateRectangle(x, y, width, height);
        PageDefinition page = _pages[pageIndex];
        if (x < 0 || y < 0 || x + width > page.Width || y + height > page.Height)
            throw new ArgumentOutOfRangeException(nameof(width),
                "A page box must remain inside the page media box.");
        var boxes = page.Boxes.ToDictionary(entry => entry.Key, entry => entry.Value);
        boxes[box] = new PageBoxDefinition(x, y, width, height);
        _pages[pageIndex] = page with { Boxes = boxes };
        return this;
    }

    /// <summary>Scales default user-space units for unusually large or small pages.</summary>
    public PdfDocumentBuilder SetPageUserUnit(int pageIndex, double userUnit)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        if (!double.IsFinite(userUnit) || userUnit is <= 0 or > 75_000)
            throw new ArgumentOutOfRangeException(nameof(userUnit));
        _pages[pageIndex] = _pages[pageIndex] with { UserUnit = userUnit };
        return this;
    }

    public PdfDocumentBuilder SetPageTransition(
        int pageIndex, PdfPageTransition transition)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ArgumentNullException.ThrowIfNull(transition);
        _pages[pageIndex] = _pages[pageIndex] with { Transition = transition };
        return this;
    }

    /// <summary>Sets how long a page remains visible during automatic presentation.</summary>
    public PdfDocumentBuilder SetPageDisplayDuration(int pageIndex, double seconds)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        if (!double.IsFinite(seconds) || seconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        _pages[pageIndex] = _pages[pageIndex] with { DisplayDuration = seconds };
        return this;
    }

    public PdfDocumentBuilder SetPageThumbnail(int pageIndex, PdfImage thumbnail)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ArgumentNullException.ThrowIfNull(thumbnail);
        _pages[pageIndex] = _pages[pageIndex] with { Thumbnail = thumbnail };
        return this;
    }

    public PdfDocumentBuilder AddUriLink(
        int pageIndex, double x, double y, double width, double height, string uri,
        PdfLinkAppearance? appearance = null)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ValidateRectangle(x, y, width, height);
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed)
            || parsed.Scheme is not ("http" or "https" or "mailto"))
            throw new ArgumentException("A link URI must use http, https, or mailto.", nameof(uri));
        PageDefinition page = _pages[pageIndex];
        _pages[pageIndex] = page with
        {
            Links = [.. page.Links, new UriLinkDefinition(
                x, y, width, height, appearance ?? new PdfLinkAppearance(), parsed.AbsoluteUri)]
        };
        return this;
    }

    public PdfDocumentBuilder AddPageLink(
        int pageIndex, double x, double y, double width, double height, int destinationPageIndex,
        PdfLinkAppearance? appearance = null)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ValidatePageIndex(destinationPageIndex, nameof(destinationPageIndex));
        ValidateRectangle(x, y, width, height);
        PageDefinition page = _pages[pageIndex];
        _pages[pageIndex] = page with
        {
            Links = [.. page.Links,
                new PageLinkDefinition(x, y, width, height,
                    appearance ?? new PdfLinkAppearance(), destinationPageIndex)]
        };
        return this;
    }

    public PdfDocumentBuilder AddNamedDestinationLink(
        int pageIndex, double x, double y, double width, double height, string destinationName,
        PdfLinkAppearance? appearance = null)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ValidateRectangle(x, y, width, height);
        if (string.IsNullOrWhiteSpace(destinationName))
            throw new ArgumentException("A named destination cannot be empty.", nameof(destinationName));
        if (!_namedDestinations.Any(destination =>
                string.Equals(destination.Name, destinationName, StringComparison.Ordinal)))
            throw new ArgumentException("The named destination has not been defined.", nameof(destinationName));
        PageDefinition page = _pages[pageIndex];
        _pages[pageIndex] = page with
        {
            Links = [.. page.Links,
                new NamedDestinationLinkDefinition(x, y, width, height,
                    appearance ?? new PdfLinkAppearance(), destinationName)]
        };
        return this;
    }

    public PdfDocumentBuilder AddNamedDestination(string name, int pageIndex) =>
        AddNamedDestination(name, pageIndex, PdfDestination.FitPage());

    public PdfDocumentBuilder AddNamedDestination(
        string name, int pageIndex, PdfDestination destination)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A named destination cannot be empty.", nameof(name));
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ArgumentNullException.ThrowIfNull(destination);
        if (_namedDestinations.Any(destination =>
                string.Equals(destination.Name, name, StringComparison.Ordinal)))
            throw new ArgumentException("Named destinations must be unique.", nameof(name));
        _namedDestinations.Add(new NamedDestinationDefinition(name, pageIndex, destination));
        return this;
    }

    public PdfDocumentBuilder SetOpenAction(int pageIndex, PdfDestination destination)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        ArgumentNullException.ThrowIfNull(destination);
        _openAction = new OpenActionDefinition(pageIndex, destination);
        return this;
    }

    public PdfDocumentBuilder AddPageLabelRange(
        int pageIndex,
        PdfPageLabelStyle style,
        string? prefix = null,
        int startNumber = 1)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        if (!Enum.IsDefined(style))
            throw new ArgumentOutOfRangeException(nameof(style));
        if (startNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(startNumber));
        if (style == PdfPageLabelStyle.None && string.IsNullOrEmpty(prefix))
            throw new ArgumentException("A page-label range without numbering requires a prefix.", nameof(prefix));
        if (_pageLabels.Any(label => label.PageIndex == pageIndex))
            throw new ArgumentException("A page-label range already begins on this page.", nameof(pageIndex));
        _pageLabels.Add(new PageLabelDefinition(pageIndex, style, prefix, startNumber));
        return this;
    }

    public PdfDocumentBuilder AddBookmark(string title, int pageIndex, int level = 0)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A bookmark title cannot be empty.", nameof(title));
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        if (level < 0)
            throw new ArgumentOutOfRangeException(nameof(level));
        if (_bookmarks.Count == 0 && level != 0)
            throw new ArgumentException("The first bookmark must be at level zero.", nameof(level));
        if (_bookmarks.Count > 0 && level > _bookmarks[^1].Level + 1)
            throw new ArgumentException("A bookmark level cannot skip its parent level.", nameof(level));
        _bookmarks.Add(new BookmarkDefinition(title, pageIndex, level));
        return this;
    }

    /// <summary>Adds a logical structure container without direct page content.</summary>
    public PdfDocumentBuilder AddStructureContainer(
        PdfStructureType type,
        int level = 0,
        string? alternateDescription = null,
        string? actualText = null) =>
        AddStructure(type, level, null, null, alternateDescription, actualText);

    /// <summary>Associates tagged page content with an element in the logical structure tree.</summary>
    public PdfDocumentBuilder AddStructureElement(
        PdfStructureType type,
        int pageIndex,
        int markedContentId,
        int level = 0,
        string? alternateDescription = null,
        string? actualText = null)
    {
        ValidatePageIndex(pageIndex, nameof(pageIndex));
        if (markedContentId < 0)
            throw new ArgumentOutOfRangeException(nameof(markedContentId));
        if (!_pages[pageIndex].MarkedContentIds.Contains(markedContentId))
            throw new ArgumentException(
                "The page content does not define this marked-content identifier.",
                nameof(markedContentId));
        if (_structureElements.Any(element =>
                element.PageIndex == pageIndex && element.MarkedContentId == markedContentId))
            throw new ArgumentException(
                "A marked-content identifier can belong to only one structure element.",
                nameof(markedContentId));
        return AddStructure(
            type, level, pageIndex, markedContentId, alternateDescription, actualText);
    }

    private PdfDocumentBuilder AddStructure(
        PdfStructureType type,
        int level,
        int? pageIndex,
        int? markedContentId,
        string? alternateDescription,
        string? actualText)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));
        if (level < 0)
            throw new ArgumentOutOfRangeException(nameof(level));
        if (_structureElements.Count == 0 && level != 0)
            throw new ArgumentException("The first structure element must be at level zero.", nameof(level));
        if (_structureElements.Count > 0 && level > _structureElements[^1].Level + 1)
            throw new ArgumentException(
                "A structure level cannot skip its parent level.", nameof(level));
        if (alternateDescription is not null && string.IsNullOrWhiteSpace(alternateDescription))
            throw new ArgumentException(
                "An alternate description cannot be empty.", nameof(alternateDescription));
        if (actualText is not null && string.IsNullOrEmpty(actualText))
            throw new ArgumentException("Actual text cannot be empty.", nameof(actualText));
        _structureElements.Add(new StructureElementDefinition(
            type, level, pageIndex, markedContentId, alternateDescription, actualText));
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
        bool pdfA4 = _pdfA4Flavor != PdfA4Flavor.None;
        var forms = new List<PdfFormXObject>();
        var patterns = new List<PdfTilingPattern>();
        var knownForms = new HashSet<PdfFormXObject>();
        var knownPatterns = new HashSet<PdfTilingPattern>();
        void AddPattern(PdfTilingPattern pattern)
        {
            if (!knownPatterns.Add(pattern)) return;
            patterns.Add(pattern);
            foreach (PdfFormXObject form in pattern.Forms.Keys) AddForm(form);
            foreach (PdfTilingPattern nested in pattern.Patterns.Keys) AddPattern(nested);
        }
        void AddForm(PdfFormXObject form)
        {
            if (!knownForms.Add(form)) return;
            forms.Add(form);
            foreach (PdfFormXObject nested in form.Forms.Keys) AddForm(nested);
            foreach (PdfTilingPattern pattern in form.Patterns.Keys) AddPattern(pattern);
        }
        foreach (PdfFormXObject form in _pages.SelectMany(page => page.Forms.Keys)) AddForm(form);
        foreach (PdfTilingPattern pattern in _pages.SelectMany(page => page.Patterns.Keys)) AddPattern(pattern);
        if (_pdfUa2Conformance && (Metadata is null
            || string.IsNullOrWhiteSpace(Metadata.Title)
            || string.IsNullOrWhiteSpace(Metadata.Language)))
            throw new InvalidOperationException(
                "PDF/UA-2 authoring requires document metadata with a title and language.");
        if (_pdfUa2Conformance && (_structureElements.Count == 0
            || _structureElements.Count(element => element.Level == 0) != 1
            || _structureElements[0].Type != PdfStructureType.Document))
            throw new InvalidOperationException(
                "PDF/UA-2 authoring requires one top-level Document structure element.");
        if (_pdfUa2Conformance && (_pages.Any(page => page.Fonts.Count > 0)
            || forms.Any(form => form.Fonts.Count > 0)))
            throw new InvalidOperationException(
                "PDF/UA-2 authoring requires embedded fonts for page text.");
        if (_pdfUa2Conformance && _pages.Any(page => page.HasUntaggedContent))
            throw new InvalidOperationException(
                "PDF/UA-2 authoring requires all painted page content to be tagged or marked as an artifact.");
        if (_pdfUa2Conformance && (_pages.Any(page => page.Links.Count > 0)
            || _bookmarks.Count > 0 || _namedDestinations.Count > 0 || _openAction is not null
            || _textFields.Count > 0 || _checkBoxes.Count > 0 || _radioGroups.Count > 0
            || _choiceFields.Count > 0 || _textNotes.Count > 0 || _textMarkups.Count > 0
            || _freeTexts.Count > 0 || _visualAnnotations.Count > 0 || _imageStamps.Count > 0))
            throw new InvalidOperationException(
                "PDF/UA-2 annotations, forms, and navigation require structure associations that are not yet authored.");
        if (_pdfUa2Conformance && _attachments.Count > 0)
            throw new InvalidOperationException(
                "PDF/UA-2 embedded files require additional accessible file-specification metadata.");
        for (int pageIndex = 0; pageIndex < _pages.Count; pageIndex++)
        {
            var registered = _structureElements.Where(element => element.PageIndex == pageIndex)
                .Select(element => element.MarkedContentId!.Value).ToHashSet();
            if (_pages[pageIndex].MarkedContentIds.Any(id => !registered.Contains(id)))
                throw new InvalidOperationException(
                    $"Page {pageIndex} contains marked content without a structure element.");
        }
        if (pdfA4 && Metadata is null)
            throw new InvalidOperationException("PDF/A-4 authoring requires XMP document metadata.");
        if (pdfA4 && _outputIntent is null)
            throw new InvalidOperationException("PDF/A-4 authoring requires an ICC output intent.");
        if (pdfA4 && _outputIntent?.Profile.ComponentCount == 4
            && _pages.SelectMany(page => page.IccColorSpaces.Keys)
                .Concat(forms.SelectMany(form => form.IccColorSpaces.Keys))
                .Concat(patterns.SelectMany(pattern => pattern.IccColorSpaces.Keys))
                .Any(profile => profile.ComponentCount == 4
                    && profile.Data.Span.SequenceEqual(_outputIntent.Profile.Data.Span)))
            throw new InvalidOperationException(
                "PDF/A-4 forbids an ICCBased CMYK content space identical to the output-intent profile; use DeviceCMYK or a distinct source profile.");
        if (pdfA4 && (_pages.Any(page => page.Fonts.Count > 0)
            || forms.Any(form => form.Fonts.Count > 0)))
            throw new InvalidOperationException("PDF/A-4 authoring requires embedded fonts; the 14 built-in PDF fonts are not embedded.");
        if (pdfA4 && (_textFields.Any(field => field.EmbeddedFont is null)
            || _choiceFields.Any(field => field.EmbeddedFont is null)))
            throw new InvalidOperationException(
                "PDF/A-4 text and choice fields require an embedded TrueType form font.");
        if (_pdfA4Flavor == PdfA4Flavor.General && _attachments.Count > 0)
            throw new InvalidOperationException("General PDF/A-4 does not permit attachments; enable PDF/A-4f conformance instead.");
        const int catalogNumber = 1;
        const int pagesNumber = 2;
        int nextObjectNumber = 3;
        int? metadataNumber = Metadata is null ? null : nextObjectNumber++;
        int? infoNumber = Metadata is null || pdfA4 ? null : nextObjectNumber++;
        int? outlinesNumber = _bookmarks.Count == 0 ? null : nextObjectNumber++;
        int[] bookmarkNumbers = _bookmarks.Select(_ => nextObjectNumber++).ToArray();
        int? structureRootNumber = _structureElements.Count == 0 ? null : nextObjectNumber++;
        int? parentTreeNumber = _structureElements.Count == 0 ? null : nextObjectNumber++;
        int? structureNamespaceNumber = _pdfUa2Conformance ? nextObjectNumber++ : null;
        int[] structureElementNumbers = _structureElements.Select(_ => nextObjectNumber++).ToArray();
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
        PdfIccProfile[] authoredIccProfiles = _pages.SelectMany(page => page.IccColorSpaces.Keys)
            .Concat(forms.SelectMany(form => form.IccColorSpaces.Keys))
            .Concat(patterns.SelectMany(pattern => pattern.IccColorSpaces.Keys))
            .Distinct().ToArray();
        PdfIccProfile[] allIccProfiles = (_outputIntent is null
                ? authoredIccProfiles
                : authoredIccProfiles.Append(_outputIntent.Profile).Distinct())
            .ToArray();
        var iccProfileNumbers = allIccProfiles
            .ToDictionary(profile => profile, _ => nextObjectNumber++);
        int? iccProfileNumber = _outputIntent is null
            ? null : iccProfileNumbers[_outputIntent.Profile];
        int? outputIntentNumber = _outputIntent is null ? null : nextObjectNumber++;
        PdfOptionalContentGroup[] optionalContentGroups = _pages
            .SelectMany(page => page.OptionalContentGroups.Keys)
            .Concat(forms.SelectMany(form => form.OptionalContentGroups.Keys))
            .Concat(patterns.SelectMany(pattern => pattern.OptionalContentGroups.Keys))
            .Distinct()
            .OrderBy(group => group.Name, StringComparer.Ordinal)
            .ToArray();
        string? duplicateLayerName = optionalContentGroups
            .GroupBy(group => group.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateLayerName is not null)
            throw new InvalidOperationException(
                $"Optional-content group name '{duplicateLayerName}' is used by more than one group.");
        var optionalContentNumbers = optionalContentGroups
            .ToDictionary(group => group, _ => nextObjectNumber++);
        PdfGraphicsState[] graphicsStates = _pages
            .SelectMany(page => page.GraphicsStates.Keys)
            .Concat(forms.SelectMany(form => form.GraphicsStates.Keys))
            .Concat(patterns.SelectMany(pattern => pattern.GraphicsStates.Keys))
            .Distinct()
            .OrderBy(state => state.FillOpacity)
            .ThenBy(state => state.StrokeOpacity)
            .ThenBy(state => state.BlendMode)
            .ToArray();
        var graphicsStateNumbers = graphicsStates
            .ToDictionary(state => state, _ => nextObjectNumber++);
        PdfShading[] shadings = _pages.SelectMany(page => page.Shadings.Keys)
            .Concat(forms.SelectMany(form => form.Shadings.Keys))
            .Concat(patterns.SelectMany(pattern => pattern.Shadings.Keys))
            .Distinct().ToArray();
        var shadingNumbers = shadings.ToDictionary(shading => shading, _ => nextObjectNumber++);
        var formNumbers = forms.ToDictionary(form => form, _ => nextObjectNumber++);
        var patternNumbers = patterns.ToDictionary(pattern => pattern, _ => nextObjectNumber++);
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
        IEnumerable<PdfStandardFont> requestedStandardFonts = _pages.SelectMany(page => page.Fonts.Keys)
            .Concat(forms.SelectMany(form => form.Fonts.Keys))
            .Concat(patterns.SelectMany(pattern => pattern.Fonts.Keys));
        if (_textFields.Any(field => field.EmbeddedFont is null)
            || _choiceFields.Any(field => field.EmbeddedFont is null))
            requestedStandardFonts = requestedStandardFonts.Append(PdfStandardFont.Helvetica);
        foreach (PdfStandardFont font in requestedStandardFonts.Distinct().Order())
            fontNumbers.Add(font, nextObjectNumber++);
        var embeddedFonts = new List<AllocatedEmbeddedFont>();
        foreach (IGrouping<TrueTypeFont, EmbeddedFontUsage> group in
            _pages.SelectMany(page => page.EmbeddedFonts)
                .Concat(forms.SelectMany(form => form.EmbeddedFonts))
                .Concat(patterns.SelectMany(pattern => pattern.EmbeddedFonts))
                .Concat(formFontUsages)
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
            .Concat(forms.SelectMany(form => form.Images.Keys))
            .Concat(patterns.SelectMany(pattern => pattern.Images.Keys))
            .Concat(_pages.Select(page => page.Thumbnail).Where(image => image is not null).Cast<PdfImage>())
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
        }
        PdfPageMode? effectivePageMode = _pageMode
            ?? (outlinesNumber.HasValue ? PdfPageMode.UseOutlines : null);
        if (effectivePageMode.HasValue)
            catalogEntries.Add(("PageMode", Name(PageModeName(effectivePageMode.Value))));
        if (_pageLayout.HasValue)
            catalogEntries.Add(("PageLayout", Name(PageLayoutName(_pageLayout.Value))));
        if (_openAction is not null)
            catalogEntries.Add(("OpenAction", DestinationArray(
                new PdfIndirectReference(allocated[_openAction.PageIndex].PageNumber, 0),
                _openAction.Destination)));
        if (structureRootNumber.HasValue)
        {
            catalogEntries.Add(("StructTreeRoot",
                new PdfIndirectReference(structureRootNumber.Value, 0)));
            catalogEntries.Add(("MarkInfo", Dictionary(("Marked", new PdfBoolean(true)))));
        }
        if (_viewerPreferences is not null || _pdfUa2Conformance)
            catalogEntries.Add(("ViewerPreferences",
                ViewerPreferencesDictionary(_viewerPreferences, _pdfUa2Conformance)));
        var catalogNameEntries = new List<(string Name, PdfObject Value)>();
        if (allocatedAttachments.Length > 0)
        {
            var names = new List<PdfObject>();
            foreach (AllocatedAttachment attachment in allocatedAttachments
                .OrderBy(value => value.Definition.FileName, StringComparer.Ordinal))
            {
                names.Add(UnicodeString(attachment.Definition.FileName));
                names.Add(new PdfIndirectReference(attachment.FileSpecificationNumber, 0));
            }
            catalogNameEntries.Add(
                ("EmbeddedFiles", Dictionary(("Names", new PdfArray(names)))));
            catalogEntries.Add(("AF", new PdfArray(allocatedAttachments.Select(attachment =>
                (PdfObject)new PdfIndirectReference(attachment.FileSpecificationNumber, 0)))));
        }
        if (_namedDestinations.Count > 0)
        {
            var names = new List<PdfObject>();
            foreach (NamedDestinationDefinition destination in _namedDestinations
                .OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                names.Add(UnicodeString(destination.Name));
                names.Add(DestinationArray(
                    new PdfIndirectReference(allocated[destination.PageIndex].PageNumber, 0),
                    destination.Destination));
            }
            catalogNameEntries.Add(("Dests", Dictionary(("Names", new PdfArray(names)))));
        }
        if (catalogNameEntries.Count > 0)
            catalogEntries.Add(("Names", Dictionary(catalogNameEntries.ToArray())));
        if (_pageLabels.Count > 0)
        {
            var numbers = new List<PdfObject>();
            foreach (PageLabelDefinition label in _pageLabels.OrderBy(value => value.PageIndex))
            {
                var entries = new List<(string Name, PdfObject Value)>();
                PdfName? style = PageLabelName(label.Style);
                if (style is not null) entries.Add(("S", style));
                if (!string.IsNullOrEmpty(label.Prefix))
                    entries.Add(("P", UnicodeString(label.Prefix)));
                if (label.Style != PdfPageLabelStyle.None && label.StartNumber != 1)
                    entries.Add(("St", new PdfInteger(label.StartNumber)));
                numbers.Add(new PdfInteger(label.PageIndex));
                numbers.Add(Dictionary(entries.ToArray()));
            }
            catalogEntries.Add(("PageLabels", Dictionary(("Nums", new PdfArray(numbers)))));
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
        if (optionalContentGroups.Length > 0)
        {
            PdfObject[] groupReferences = optionalContentGroups.Select(group =>
                (PdfObject)new PdfIndirectReference(optionalContentNumbers[group], 0)).ToArray();
            PdfObject[] initiallyHidden = optionalContentGroups
                .Where(group => !group.InitiallyVisible)
                .Select(group => (PdfObject)new PdfIndirectReference(
                    optionalContentNumbers[group], 0))
                .ToArray();
            var defaultConfiguration = new List<(string Name, PdfObject Value)>
            {
                ("Name", UnicodeString("KillerPDF Layers")),
                ("BaseState", Name("ON")),
                ("Order", new PdfArray(groupReferences))
            };
            if (initiallyHidden.Length > 0)
                defaultConfiguration.Add(("OFF", new PdfArray(initiallyHidden)));
            catalogEntries.Add(("OCProperties", Dictionary(
                ("OCGs", new PdfArray(groupReferences)),
                ("D", Dictionary(defaultConfiguration.ToArray())))));
        }
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
                    ("Subtype", Name("XML"))), BuildXmp(
                        Metadata, _pdfA4Flavor, _pdfUa2Conformance)), 0));
            if (infoNumber.HasValue)
                objects.Add(new PdfIndirectObject(infoNumber.Value, 0, BuildInfo(Metadata), 0));
        }
        if (outlinesNumber.HasValue)
        {
            var parents = new int?[_bookmarks.Count];
            var children = Enumerable.Range(0, _bookmarks.Count)
                .Select(_ => new List<int>()).ToArray();
            var levelStack = new List<int>();
            var topLevel = new List<int>();
            for (int index = 0; index < _bookmarks.Count; index++)
            {
                int level = _bookmarks[index].Level;
                while (levelStack.Count > level) levelStack.RemoveAt(levelStack.Count - 1);
                if (level == 0) topLevel.Add(index);
                else
                {
                    int parent = levelStack[level - 1];
                    parents[index] = parent;
                    children[parent].Add(index);
                }
                if (levelStack.Count == level) levelStack.Add(index);
                else levelStack[level] = index;
            }
            objects.Add(new PdfIndirectObject(outlinesNumber.Value, 0,
                Dictionary(
                    ("Type", Name("Outlines")),
                    ("First", new PdfIndirectReference(bookmarkNumbers[topLevel[0]], 0)),
                    ("Last", new PdfIndirectReference(bookmarkNumbers[topLevel[^1]], 0)),
                    ("Count", new PdfInteger(bookmarkNumbers.Length))), 0));
            for (int index = 0; index < _bookmarks.Count; index++)
            {
                BookmarkDefinition bookmark = _bookmarks[index];
                var entries = new List<(string Name, PdfObject Value)>
                {
                    ("Title", UnicodeString(bookmark.Title)),
                    ("Parent", parents[index].HasValue
                        ? new PdfIndirectReference(bookmarkNumbers[parents[index]!.Value], 0)
                        : new PdfIndirectReference(outlinesNumber.Value, 0)),
                    ("Dest", new PdfArray([
                        new PdfIndirectReference(allocated[bookmark.PageIndex].PageNumber, 0),
                        Name("Fit")]))
                };
                List<int> siblings = parents[index].HasValue
                    ? children[parents[index]!.Value] : topLevel;
                int siblingIndex = siblings.IndexOf(index);
                if (siblingIndex > 0)
                    entries.Add(("Prev", new PdfIndirectReference(
                        bookmarkNumbers[siblings[siblingIndex - 1]], 0)));
                if (siblingIndex + 1 < siblings.Count)
                    entries.Add(("Next", new PdfIndirectReference(
                        bookmarkNumbers[siblings[siblingIndex + 1]], 0)));
                if (children[index].Count > 0)
                {
                    entries.Add(("First", new PdfIndirectReference(
                        bookmarkNumbers[children[index][0]], 0)));
                    entries.Add(("Last", new PdfIndirectReference(
                        bookmarkNumbers[children[index][^1]], 0)));
                    entries.Add(("Count", new PdfInteger(DescendantCount(index))));
                }
                objects.Add(new PdfIndirectObject(
                    bookmarkNumbers[index], 0, Dictionary(entries.ToArray()), 0));
            }

            int DescendantCount(int index) => children[index]
                .Sum(child => 1 + DescendantCount(child));
        }
        var structureParentKeys = _structureElements
            .Where(element => element.PageIndex.HasValue)
            .Select(element => element.PageIndex!.Value)
            .Distinct()
            .Order()
            .Select((pageIndex, key) => (pageIndex, key))
            .ToDictionary(item => item.pageIndex, item => item.key);
        if (structureRootNumber.HasValue)
        {
            var parents = new int?[_structureElements.Count];
            var children = Enumerable.Range(0, _structureElements.Count)
                .Select(_ => new List<int>()).ToArray();
            var levelStack = new List<int>();
            var topLevel = new List<int>();
            for (int index = 0; index < _structureElements.Count; index++)
            {
                int level = _structureElements[index].Level;
                while (levelStack.Count > level) levelStack.RemoveAt(levelStack.Count - 1);
                if (level == 0) topLevel.Add(index);
                else
                {
                    int parent = levelStack[level - 1];
                    parents[index] = parent;
                    children[parent].Add(index);
                }
                if (levelStack.Count == level) levelStack.Add(index);
                else levelStack[level] = index;
            }

            var parentTreeNumbers = new List<PdfObject>();
            foreach ((int pageIndex, int key) in structureParentKeys.OrderBy(item => item.Value))
            {
                StructureElementDefinition[] pageElements = _structureElements
                    .Where(element => element.PageIndex == pageIndex).ToArray();
                int maximumMcid = pageElements.Max(element => element.MarkedContentId!.Value);
                PdfObject[] mappings = Enumerable.Repeat<PdfObject>(
                    PdfNull.Instance, maximumMcid + 1).ToArray();
                foreach (StructureElementDefinition element in pageElements)
                {
                    int elementIndex = _structureElements.IndexOf(element);
                    mappings[element.MarkedContentId!.Value] =
                        new PdfIndirectReference(structureElementNumbers[elementIndex], 0);
                }
                parentTreeNumbers.Add(new PdfInteger(key));
                parentTreeNumbers.Add(new PdfArray(mappings));
            }
            objects.Add(new PdfIndirectObject(parentTreeNumber!.Value, 0,
                Dictionary(("Nums", new PdfArray(parentTreeNumbers))), 0));
            var rootEntries = new List<(string Name, PdfObject Value)>
            {
                ("Type", Name("StructTreeRoot")),
                ("K", new PdfArray(topLevel.Select(index =>
                    (PdfObject)new PdfIndirectReference(structureElementNumbers[index], 0)))),
                ("ParentTree", new PdfIndirectReference(parentTreeNumber.Value, 0)),
                ("ParentTreeNextKey", new PdfInteger(structureParentKeys.Count))
            };
            if (structureNamespaceNumber.HasValue)
                rootEntries.Add(("Namespaces", new PdfArray([
                    new PdfIndirectReference(structureNamespaceNumber.Value, 0)])));
            objects.Add(new PdfIndirectObject(structureRootNumber.Value, 0,
                Dictionary(rootEntries.ToArray()), 0));
            if (structureNamespaceNumber.HasValue)
                objects.Add(new PdfIndirectObject(structureNamespaceNumber.Value, 0,
                    Dictionary(
                        ("Type", Name("Namespace")),
                        ("NS", Latin1String("http://iso.org/pdf2/ssn"))), 0));

            for (int index = 0; index < _structureElements.Count; index++)
            {
                StructureElementDefinition definition = _structureElements[index];
                var entries = new List<(string Name, PdfObject Value)>
                {
                    ("Type", Name("StructElem")),
                    ("S", Name(PdfStructureTypeNames.Name(definition.Type))),
                    ("P", parents[index].HasValue
                        ? new PdfIndirectReference(structureElementNumbers[parents[index]!.Value], 0)
                        : new PdfIndirectReference(structureRootNumber.Value, 0))
                };
                if (definition.PageIndex.HasValue)
                    entries.Add(("Pg", new PdfIndirectReference(
                        allocated[definition.PageIndex.Value].PageNumber, 0)));
                if (structureNamespaceNumber.HasValue)
                    entries.Add(("NS", new PdfIndirectReference(
                        structureNamespaceNumber.Value, 0)));
                var structureKids = new List<PdfObject>();
                if (definition.MarkedContentId.HasValue)
                    structureKids.Add(new PdfInteger(definition.MarkedContentId.Value));
                structureKids.AddRange(children[index].Select(child =>
                    (PdfObject)new PdfIndirectReference(structureElementNumbers[child], 0)));
                if (structureKids.Count == 1) entries.Add(("K", structureKids[0]));
                else if (structureKids.Count > 1)
                    entries.Add(("K", new PdfArray(structureKids)));
                if (definition.AlternateDescription is not null)
                    entries.Add(("Alt", UnicodeString(definition.AlternateDescription)));
                if (definition.ActualText is not null)
                    entries.Add(("ActualText", UnicodeString(definition.ActualText)));
                objects.Add(new PdfIndirectObject(
                    structureElementNumbers[index], 0, Dictionary(entries.ToArray()), 0));
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
        foreach (PdfOptionalContentGroup group in optionalContentGroups)
            objects.Add(new PdfIndirectObject(optionalContentNumbers[group], 0,
                Dictionary(
                    ("Type", Name("OCG")),
                    ("Name", UnicodeString(group.Name)),
                    ("Intent", new PdfArray([Name("View"), Name("Design")]))), 0));
        foreach (PdfGraphicsState state in graphicsStates)
            objects.Add(new PdfIndirectObject(graphicsStateNumbers[state], 0,
                Dictionary(
                    ("Type", Name("ExtGState")),
                    ("ca", Number(state.FillOpacity)),
                    ("CA", Number(state.StrokeOpacity)),
                    ("BM", Name(PdfBlendModeNames.Name(state.BlendMode)))), 0));
        foreach (PdfShading shading in shadings)
            objects.Add(new PdfIndirectObject(
                shadingNumbers[shading], 0, ShadingDictionary(shading), 0));
        foreach (PdfIccProfile profile in allIccProfiles)
            objects.Add(new PdfIndirectObject(iccProfileNumbers[profile], 0,
                new PdfStream(Dictionary(
                    ("N", new PdfInteger(profile.ComponentCount)),
                    ("Alternate", Name(profile.ComponentCount switch
                    {
                        1 => "DeviceGray",
                        3 => "DeviceRGB",
                        4 => "DeviceCMYK",
                        _ => throw new NotSupportedException()
                    }))), profile.Data.Span), 0));
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
        foreach (PdfFormXObject form in forms)
        {
            var entries = new List<(string Name, PdfObject Value)>
            {
                ("Type", Name("XObject")),
                ("Subtype", Name("Form")),
                ("FormType", new PdfInteger(1)),
                ("BBox", new PdfArray([
                    new PdfInteger(0), new PdfInteger(0),
                    Number(form.Width), Number(form.Height)])),
                ("Resources", ResourceDictionary(
                    form.Fonts, form.EmbeddedFonts, form.Images,
                    form.OptionalContentGroups, form.GraphicsStates,
                    form.Shadings, form.Forms, form.Patterns, form.IccColorSpaces,
                    form.SpotColors, form.LabColorSpaces, form.IndexedColorSpaces,
                    fontNumbers, embeddedFonts, imageNumbers,
                    optionalContentNumbers, graphicsStateNumbers,
                    shadingNumbers, formNumbers, patternNumbers, iccProfileNumbers))
            };
            if (form.IsolatedTransparencyGroup || form.KnockoutTransparencyGroup)
                entries.Add(("Group", Dictionary(
                    ("S", Name("Transparency")),
                    ("I", new PdfBoolean(form.IsolatedTransparencyGroup)),
                    ("K", new PdfBoolean(form.KnockoutTransparencyGroup)))));
            objects.Add(new PdfIndirectObject(formNumbers[form], 0,
                new PdfStream(Dictionary(entries.ToArray()), form.Content), 0));
        }
        foreach (PdfTilingPattern pattern in patterns)
        {
            objects.Add(new PdfIndirectObject(patternNumbers[pattern], 0,
                new PdfStream(Dictionary(
                    ("Type", Name("Pattern")),
                    ("PatternType", new PdfInteger(1)),
                    ("PaintType", new PdfInteger((int)pattern.PaintType)),
                    ("TilingType", new PdfInteger((int)pattern.TilingType)),
                    ("BBox", new PdfArray([
                        new PdfInteger(0), new PdfInteger(0),
                        Number(pattern.Width), Number(pattern.Height)])),
                    ("XStep", Number(pattern.HorizontalStep)),
                    ("YStep", Number(pattern.VerticalStep)),
                    ("Matrix", new PdfArray([
                        Number(pattern.Matrix.A), Number(pattern.Matrix.B),
                        Number(pattern.Matrix.C), Number(pattern.Matrix.D),
                        Number(pattern.Matrix.E), Number(pattern.Matrix.F)])),
                    ("Resources", ResourceDictionary(
                        pattern.Fonts, pattern.EmbeddedFonts, pattern.Images,
                        pattern.OptionalContentGroups, pattern.GraphicsStates,
                        pattern.Shadings, pattern.Forms, pattern.Patterns,
                        pattern.IccColorSpaces, pattern.SpotColors, pattern.LabColorSpaces,
                        pattern.IndexedColorSpaces,
                        fontNumbers, embeddedFonts, imageNumbers,
                        optionalContentNumbers, graphicsStateNumbers,
                        shadingNumbers, formNumbers, patternNumbers, iccProfileNumbers))), pattern.Content), 0));
        }
        foreach (AllocatedPage allocatedPage in allocated)
        {
            PdfDictionary resources = ResourceDictionary(
                allocatedPage.Definition.Fonts, allocatedPage.Definition.EmbeddedFonts,
                allocatedPage.Definition.Images, allocatedPage.Definition.OptionalContentGroups,
                allocatedPage.Definition.GraphicsStates, allocatedPage.Definition.Shadings,
                allocatedPage.Definition.Forms, allocatedPage.Definition.Patterns,
                allocatedPage.Definition.IccColorSpaces, allocatedPage.Definition.SpotColors,
                allocatedPage.Definition.LabColorSpaces, allocatedPage.Definition.IndexedColorSpaces,
                fontNumbers, embeddedFonts, imageNumbers, optionalContentNumbers,
                graphicsStateNumbers, shadingNumbers, formNumbers, patternNumbers,
                iccProfileNumbers);
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
            if (allocatedPage.Definition.Rotation != 0)
                entries.Add(("Rotate", new PdfInteger(allocatedPage.Definition.Rotation)));
            if (allocatedPage.Definition.UserUnit != 1)
                entries.Add(("UserUnit", Number(allocatedPage.Definition.UserUnit)));
            foreach ((PdfPageBox box, PageBoxDefinition rectangle) in allocatedPage.Definition.Boxes.OrderBy(entry => entry.Key))
                entries.Add((PageBoxName(box), new PdfArray([
                    Number(rectangle.X), Number(rectangle.Y),
                    Number(rectangle.X + rectangle.Width), Number(rectangle.Y + rectangle.Height)])));
            if (allocatedPage.Definition.DisplayDuration.HasValue)
                entries.Add(("Dur", Number(allocatedPage.Definition.DisplayDuration.Value)));
            if (allocatedPage.Definition.Transition is not null)
                entries.Add(("Trans", PageTransitionDictionary(allocatedPage.Definition.Transition)));
            if (allocatedPage.Definition.Thumbnail is not null)
                entries.Add(("Thumb", new PdfIndirectReference(
                    imageNumbers[allocatedPage.Definition.Thumbnail], 0)));
            if (allocatedPage.AnnotationNumbers.Length > 0)
                entries.Add(("Annots", new PdfArray(allocatedPage.AnnotationNumbers.Select(number =>
                    (PdfObject)new PdfIndirectReference(number, 0)))));
            int pageIndex = allocated.IndexOf(allocatedPage);
            if (structureParentKeys.TryGetValue(pageIndex, out int structureParentKey))
                entries.Add(("StructParents", new PdfInteger(structureParentKey)));
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
                    ("F", new PdfInteger(4)),
                    ("Border", new PdfArray([
                        new PdfInteger(0), new PdfInteger(0), new PdfInteger(0)]))
                };
                if (link.Appearance.BorderWidth > 0)
                {
                    var borderStyleEntries = new List<(string Name, PdfObject Value)>
                    {
                        ("W", Number(link.Appearance.BorderWidth)),
                        ("S", Name(LinkBorderStyleName(link.Appearance.BorderStyle)))
                    };
                    if (link.Appearance.BorderStyle == PdfLinkBorderStyle.Dashed)
                        borderStyleEntries.Add(("D", new PdfArray(
                            link.Appearance.DashPattern.Select(Number))));
                    annotationEntries.Add(("BS", Dictionary(borderStyleEntries.ToArray())));
                }
                if (link.Appearance.Color.HasValue)
                    annotationEntries.Add(("C", ColorArray(link.Appearance.Color.Value)));
                annotationEntries.Add(("H", Name(LinkHighlightModeName(
                    link.Appearance.HighlightMode))));
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
                else if (link is NamedDestinationLinkDefinition named)
                {
                    annotationEntries.Add(("Dest", UnicodeString(named.DestinationName)));
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

    private static byte[] BuildXmp(
        PdfDocumentMetadata metadata, PdfA4Flavor pdfA4Flavor, bool pdfUa2)
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
            if (pdfA4Flavor != PdfA4Flavor.None)
            {
                WriteSimple("pdfaid", "part", "http://www.aiim.org/pdfa/ns/id/", "4");
                WriteSimple("pdfaid", "rev", "http://www.aiim.org/pdfa/ns/id/", "2020");
                string? conformance = pdfA4Flavor switch
                {
                    PdfA4Flavor.EmbeddedFiles => "F",
                    PdfA4Flavor.Engineering => "E",
                    _ => null
                };
                WriteSimple("pdfaid", "conformance", "http://www.aiim.org/pdfa/ns/id/", conformance);
            }
            if (pdfUa2)
            {
                WriteSimple("pdfuaid", "part", "http://www.aiim.org/pdfua/ns/id/", "2");
                WriteSimple("pdfuaid", "rev", "http://www.aiim.org/pdfua/ns/id/", "2024");
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

    private enum PdfA4Flavor
    {
        None,
        General,
        EmbeddedFiles,
        Engineering
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

    private static PdfDictionary ResourceDictionary(
        IReadOnlyDictionary<PdfStandardFont, PdfName> fonts,
        IReadOnlyCollection<EmbeddedFontUsage> embeddedFontUsages,
        IReadOnlyDictionary<PdfImage, PdfName> images,
        IReadOnlyDictionary<PdfOptionalContentGroup, PdfName> optionalContentGroups,
        IReadOnlyDictionary<PdfGraphicsState, PdfName> graphicsStates,
        IReadOnlyDictionary<PdfShading, PdfName> shadings,
        IReadOnlyDictionary<PdfFormXObject, PdfName> forms,
        IReadOnlyDictionary<PdfTilingPattern, PdfName> patterns,
        IReadOnlyDictionary<PdfIccProfile, PdfName> iccColorSpaces,
        IReadOnlyDictionary<PdfSpotColor, PdfName> spotColors,
        IReadOnlyDictionary<PdfLabColorSpace, PdfName> labColorSpaces,
        IReadOnlyDictionary<PdfIndexedColorSpace, PdfName> indexedColorSpaces,
        IReadOnlyDictionary<PdfStandardFont, int> fontNumbers,
        IReadOnlyCollection<AllocatedEmbeddedFont> embeddedFonts,
        IReadOnlyDictionary<PdfImage, int> imageNumbers,
        IReadOnlyDictionary<PdfOptionalContentGroup, int> optionalContentNumbers,
        IReadOnlyDictionary<PdfGraphicsState, int> graphicsStateNumbers,
        IReadOnlyDictionary<PdfShading, int> shadingNumbers,
        IReadOnlyDictionary<PdfFormXObject, int> formNumbers,
        IReadOnlyDictionary<PdfTilingPattern, int> patternNumbers,
        IReadOnlyDictionary<PdfIccProfile, int> iccProfileNumbers)
    {
        var entries = new List<(string Name, PdfObject Value)>();
        if (fonts.Count > 0 || embeddedFontUsages.Count > 0)
        {
            var values = fonts.Select(entry => new KeyValuePair<PdfName, PdfObject>(
                entry.Value, new PdfIndirectReference(fontNumbers[entry.Key], 0))).ToList();
            values.AddRange(embeddedFontUsages.Select(usage =>
                new KeyValuePair<PdfName, PdfObject>(usage.ResourceName,
                    new PdfIndirectReference(embeddedFonts.Single(font =>
                        ReferenceEquals(font.Font, usage.Font)).Type0Number, 0))));
            entries.Add(("Font", new PdfDictionary(values)));
        }
        if (images.Count > 0 || forms.Count > 0)
        {
            var values = images.Select(entry => new KeyValuePair<PdfName, PdfObject>(
                entry.Value, new PdfIndirectReference(imageNumbers[entry.Key], 0))).ToList();
            values.AddRange(forms.Select(entry => new KeyValuePair<PdfName, PdfObject>(
                entry.Value, new PdfIndirectReference(formNumbers[entry.Key], 0))));
            entries.Add(("XObject", new PdfDictionary(values)));
        }
        if (optionalContentGroups.Count > 0)
            entries.Add(("Properties", new PdfDictionary(optionalContentGroups.Select(entry =>
                new KeyValuePair<PdfName, PdfObject>(entry.Value,
                    new PdfIndirectReference(optionalContentNumbers[entry.Key], 0))))));
        if (graphicsStates.Count > 0)
            entries.Add(("ExtGState", new PdfDictionary(graphicsStates.Select(entry =>
                new KeyValuePair<PdfName, PdfObject>(entry.Value,
                    new PdfIndirectReference(graphicsStateNumbers[entry.Key], 0))))));
        if (shadings.Count > 0)
            entries.Add(("Shading", new PdfDictionary(shadings.Select(entry =>
                new KeyValuePair<PdfName, PdfObject>(entry.Value,
                    new PdfIndirectReference(shadingNumbers[entry.Key], 0))))));
        if (patterns.Count > 0)
            entries.Add(("Pattern", new PdfDictionary(patterns.Select(entry =>
                new KeyValuePair<PdfName, PdfObject>(entry.Value,
                    new PdfIndirectReference(patternNumbers[entry.Key], 0))))));
        if (iccColorSpaces.Count > 0 || spotColors.Count > 0 || labColorSpaces.Count > 0
            || indexedColorSpaces.Count > 0)
        {
            var colorSpaces = iccColorSpaces.Select(entry =>
                new KeyValuePair<PdfName, PdfObject>(entry.Value,
                    new PdfArray([
                        Name("ICCBased"),
                        new PdfIndirectReference(iccProfileNumbers[entry.Key], 0)])))
                .ToList();
            colorSpaces.AddRange(spotColors.Select(entry =>
                new KeyValuePair<PdfName, PdfObject>(entry.Value,
                    SpotColorSpace(entry.Key))));
            colorSpaces.AddRange(labColorSpaces.Select(entry =>
                new KeyValuePair<PdfName, PdfObject>(entry.Value,
                    LabColorSpace(entry.Key))));
            colorSpaces.AddRange(indexedColorSpaces.Select(entry =>
                new KeyValuePair<PdfName, PdfObject>(entry.Value,
                    IndexedColorSpace(entry.Key))));
            entries.Add(("ColorSpace", new PdfDictionary(colorSpaces)));
        }
        return Dictionary(entries.ToArray());
    }

    private static PdfDictionary ShadingDictionary(PdfShading shading)
    {
        PdfArray coordinates = shading switch
        {
            PdfAxialGradient axial => new PdfArray([
                Number(axial.StartX), Number(axial.StartY),
                Number(axial.EndX), Number(axial.EndY)]),
            PdfRadialGradient radial => new PdfArray([
                Number(radial.StartX), Number(radial.StartY), Number(radial.StartRadius),
                Number(radial.EndX), Number(radial.EndY), Number(radial.EndRadius)]),
            _ => throw new NotSupportedException(
                $"Shading type {shading.GetType().FullName} cannot be authored.")
        };
        return Dictionary(
            ("ShadingType", new PdfInteger(shading is PdfAxialGradient ? 2 : 3)),
            ("ColorSpace", Name("DeviceRGB")),
            ("Coords", coordinates),
            ("Function", GradientFunction(shading.Stops)),
            ("Extend", new PdfArray([
                new PdfBoolean(shading.ExtendStart), new PdfBoolean(shading.ExtendEnd)])),
            ("AntiAlias", new PdfBoolean(true)));
    }

    private static PdfArray SpotColorSpace(PdfSpotColor color) => new([
        Name("Separation"),
        new PdfName(Encoding.UTF8.GetBytes(color.Name)),
        Name("DeviceCMYK"),
        Dictionary(
            ("FunctionType", new PdfInteger(2)),
            ("Domain", new PdfArray([new PdfInteger(0), new PdfInteger(1)])),
            ("C0", new PdfArray([
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(0), new PdfInteger(0)])),
            ("C1", new PdfArray([
                Number(color.AlternateColor.Cyan), Number(color.AlternateColor.Magenta),
                Number(color.AlternateColor.Yellow), Number(color.AlternateColor.Black)])),
            ("N", new PdfInteger(1))) ]);

    private static PdfArray LabColorSpace(PdfLabColorSpace colorSpace) => new([
        Name("Lab"),
        Dictionary(
            ("WhitePoint", new PdfArray([
                Number(colorSpace.WhiteX), Number(colorSpace.WhiteY), Number(colorSpace.WhiteZ)])),
            ("BlackPoint", new PdfArray([
                Number(colorSpace.BlackX), Number(colorSpace.BlackY), Number(colorSpace.BlackZ)])),
            ("Range", new PdfArray([
                Number(colorSpace.MinimumA), Number(colorSpace.MaximumA),
                Number(colorSpace.MinimumB), Number(colorSpace.MaximumB)]))) ]);

    private static PdfArray IndexedColorSpace(PdfIndexedColorSpace colorSpace) => new([
        Name("Indexed"),
        Name(colorSpace.BaseColorSpace switch
        {
            PdfIndexedBaseColorSpace.Gray => "DeviceGray",
            PdfIndexedBaseColorSpace.Rgb => "DeviceRGB",
            PdfIndexedBaseColorSpace.Cmyk => "DeviceCMYK",
            _ => throw new ArgumentOutOfRangeException(nameof(colorSpace))
        }),
        new PdfInteger(colorSpace.EntryCount - 1),
        new PdfString(colorSpace.Palette.Span, PdfStringForm.Hexadecimal) ]);

    private static PdfDictionary GradientFunction(IReadOnlyList<PdfGradientStop> stops)
    {
        PdfDictionary Segment(PdfGradientStop start, PdfGradientStop end) => Dictionary(
            ("FunctionType", new PdfInteger(2)),
            ("Domain", new PdfArray([new PdfInteger(0), new PdfInteger(1)])),
            ("C0", ColorArray(start.Color)),
            ("C1", ColorArray(end.Color)),
            ("N", new PdfInteger(1)));

        if (stops.Count == 2) return Segment(stops[0], stops[1]);
        var functions = new List<PdfObject>(stops.Count - 1);
        var bounds = new List<PdfObject>(stops.Count - 2);
        var encode = new List<PdfObject>((stops.Count - 1) * 2);
        for (int index = 0; index + 1 < stops.Count; index++)
        {
            functions.Add(Segment(stops[index], stops[index + 1]));
            if (index + 1 < stops.Count - 1) bounds.Add(Number(stops[index + 1].Offset));
            encode.Add(new PdfInteger(0));
            encode.Add(new PdfInteger(1));
        }
        return Dictionary(
            ("FunctionType", new PdfInteger(3)),
            ("Domain", new PdfArray([new PdfInteger(0), new PdfInteger(1)])),
            ("Functions", new PdfArray(functions)),
            ("Bounds", new PdfArray(bounds)),
            ("Encode", new PdfArray(encode)));
    }

    private static PdfDictionary Dictionary(params (string Name, PdfObject Value)[] entries) =>
        new(entries.Select(entry => new KeyValuePair<PdfName, PdfObject>(Name(entry.Name), entry.Value)));

    private static PdfName Name(string value) => new(System.Text.Encoding.ASCII.GetBytes(value));

    private static PdfName? PageLabelName(PdfPageLabelStyle style) => style switch
    {
        PdfPageLabelStyle.None => null,
        PdfPageLabelStyle.Decimal => Name("D"),
        PdfPageLabelStyle.UpperRoman => Name("R"),
        PdfPageLabelStyle.LowerRoman => Name("r"),
        PdfPageLabelStyle.UpperLetters => Name("A"),
        PdfPageLabelStyle.LowerLetters => Name("a"),
        _ => throw new ArgumentOutOfRangeException(nameof(style))
    };

    private static string PageBoxName(PdfPageBox box) => box switch
    {
        PdfPageBox.Crop => "CropBox",
        PdfPageBox.Bleed => "BleedBox",
        PdfPageBox.Trim => "TrimBox",
        PdfPageBox.Art => "ArtBox",
        _ => throw new ArgumentOutOfRangeException(nameof(box))
    };

    private static string LinkBorderStyleName(PdfLinkBorderStyle style) => style switch
    {
        PdfLinkBorderStyle.Solid => "S",
        PdfLinkBorderStyle.Dashed => "D",
        PdfLinkBorderStyle.Beveled => "B",
        PdfLinkBorderStyle.Inset => "I",
        PdfLinkBorderStyle.Underline => "U",
        _ => throw new ArgumentOutOfRangeException(nameof(style))
    };

    private static string LinkHighlightModeName(PdfLinkHighlightMode mode) => mode switch
    {
        PdfLinkHighlightMode.None => "N",
        PdfLinkHighlightMode.Invert => "I",
        PdfLinkHighlightMode.Outline => "O",
        PdfLinkHighlightMode.Push => "P",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static string PageLayoutName(PdfPageLayout layout) => layout switch
    {
        PdfPageLayout.SinglePage => "SinglePage",
        PdfPageLayout.OneColumn => "OneColumn",
        PdfPageLayout.TwoColumnLeft => "TwoColumnLeft",
        PdfPageLayout.TwoColumnRight => "TwoColumnRight",
        PdfPageLayout.TwoPageLeft => "TwoPageLeft",
        PdfPageLayout.TwoPageRight => "TwoPageRight",
        _ => throw new ArgumentOutOfRangeException(nameof(layout))
    };

    private static string PageModeName(PdfPageMode mode) => mode switch
    {
        PdfPageMode.UseNone => "UseNone",
        PdfPageMode.UseOutlines => "UseOutlines",
        PdfPageMode.UseThumbs => "UseThumbs",
        PdfPageMode.FullScreen => "FullScreen",
        PdfPageMode.UseOptionalContent => "UseOC",
        PdfPageMode.UseAttachments => "UseAttachments",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static PdfDictionary ViewerPreferencesDictionary(
        PdfViewerPreferences? preferences, bool requireDocumentTitle)
    {
        preferences ??= new PdfViewerPreferences();
        var entries = new List<(string Name, PdfObject Value)>();
        void AddTrue(string name, bool value)
        {
            if (value) entries.Add((name, new PdfBoolean(true)));
        }
        AddTrue("HideToolbar", preferences.HideToolbar);
        AddTrue("HideMenubar", preferences.HideMenuBar);
        AddTrue("HideWindowUI", preferences.HideWindowUi);
        AddTrue("FitWindow", preferences.FitWindow);
        AddTrue("CenterWindow", preferences.CenterWindow);
        AddTrue("DisplayDocTitle", preferences.DisplayDocumentTitle || requireDocumentTitle);
        AddTrue("PickTrayByPDFSize", preferences.PickTrayByPdfSize);
        if (preferences.ReadingDirection == PdfReadingDirection.RightToLeft)
            entries.Add(("Direction", Name("R2L")));
        if (preferences.PrintScaling == PdfPrintScaling.None)
            entries.Add(("PrintScaling", Name("None")));
        if (preferences.Duplex != PdfDuplexMode.Default)
            entries.Add(("Duplex", Name(preferences.Duplex switch
            {
                PdfDuplexMode.Simplex => "Simplex",
                PdfDuplexMode.DuplexFlipShortEdge => "DuplexFlipShortEdge",
                PdfDuplexMode.DuplexFlipLongEdge => "DuplexFlipLongEdge",
                _ => throw new ArgumentOutOfRangeException(nameof(preferences))
            })));
        return Dictionary(entries.ToArray());
    }

    private static PdfDictionary PageTransitionDictionary(PdfPageTransition transition)
    {
        var entries = new List<(string Name, PdfObject Value)>
        {
            ("S", Name(transition.Style == PdfPageTransitionStyle.Replace
                ? "R" : transition.Style.ToString())),
            ("D", Number(transition.Duration))
        };
        if (transition.Dimension.HasValue)
            entries.Add(("Dm", Name(transition.Dimension == PdfTransitionDimension.Horizontal ? "H" : "V")));
        if (transition.Motion.HasValue)
            entries.Add(("M", Name(transition.Motion == PdfTransitionMotion.Inward ? "I" : "O")));
        if (transition.Direction.HasValue)
            entries.Add(("Di", new PdfInteger(transition.Direction.Value)));
        if (transition.Scale.HasValue)
            entries.Add(("SS", Number(transition.Scale.Value)));
        if (transition.Opaque)
            entries.Add(("B", new PdfBoolean(true)));
        return Dictionary(entries.ToArray());
    }

    private static PdfArray DestinationArray(
        PdfIndirectReference page, PdfDestination destination)
    {
        var values = new List<PdfObject> { page, Name(destination.Kind switch
        {
            PdfDestinationKind.Xyz => "XYZ",
            PdfDestinationKind.Fit => "Fit",
            PdfDestinationKind.FitH => "FitH",
            PdfDestinationKind.FitV => "FitV",
            PdfDestinationKind.FitR => "FitR",
            PdfDestinationKind.FitB => "FitB",
            PdfDestinationKind.FitBH => "FitBH",
            PdfDestinationKind.FitBV => "FitBV",
            _ => throw new ArgumentOutOfRangeException(nameof(destination))
        }) };
        values.AddRange(destination.Values.Select(value =>
            value.HasValue ? Number(value.Value) : PdfNull.Instance));
        return new PdfArray(values);
    }

    private sealed record PageDefinition(
        double Width,
        double Height,
        byte[] Content,
        IReadOnlyDictionary<PdfStandardFont, PdfName> Fonts,
        IReadOnlyList<EmbeddedFontUsage> EmbeddedFonts,
        IReadOnlyDictionary<PdfImage, PdfName> Images,
        IReadOnlyDictionary<PdfOptionalContentGroup, PdfName> OptionalContentGroups,
        IReadOnlyDictionary<PdfGraphicsState, PdfName> GraphicsStates,
        IReadOnlyDictionary<PdfShading, PdfName> Shadings,
        IReadOnlyDictionary<PdfFormXObject, PdfName> Forms,
        IReadOnlyDictionary<PdfTilingPattern, PdfName> Patterns,
        IReadOnlyDictionary<PdfIccProfile, PdfName> IccColorSpaces,
        IReadOnlyDictionary<PdfSpotColor, PdfName> SpotColors,
        IReadOnlyDictionary<PdfLabColorSpace, PdfName> LabColorSpaces,
        IReadOnlyDictionary<PdfIndexedColorSpace, PdfName> IndexedColorSpaces,
        IReadOnlyList<LinkDefinition> Links,
        IReadOnlyCollection<int> MarkedContentIds,
        bool HasUntaggedContent,
        int Rotation,
        double UserUnit,
        IReadOnlyDictionary<PdfPageBox, PageBoxDefinition> Boxes,
        PdfPageTransition? Transition,
        double? DisplayDuration,
        PdfImage? Thumbnail);
    private sealed record PageBoxDefinition(
        double X, double Y, double Width, double Height);
    private sealed record AllocatedPage(
        PageDefinition Definition, int PageNumber, int? ContentNumber, int[] AnnotationNumbers);
    private abstract record LinkDefinition(
        double X, double Y, double Width, double Height, PdfLinkAppearance Appearance);
    private sealed record UriLinkDefinition(
        double X, double Y, double Width, double Height, PdfLinkAppearance Appearance, string Uri)
        : LinkDefinition(X, Y, Width, Height, Appearance);
    private sealed record PageLinkDefinition(
        double X, double Y, double Width, double Height, PdfLinkAppearance Appearance,
        int DestinationPageIndex)
        : LinkDefinition(X, Y, Width, Height, Appearance);
    private sealed record NamedDestinationLinkDefinition(
        double X, double Y, double Width, double Height, PdfLinkAppearance Appearance,
        string DestinationName)
        : LinkDefinition(X, Y, Width, Height, Appearance);
    private sealed record BookmarkDefinition(string Title, int PageIndex, int Level);
    private sealed record StructureElementDefinition(
        PdfStructureType Type,
        int Level,
        int? PageIndex,
        int? MarkedContentId,
        string? AlternateDescription,
        string? ActualText);
    private sealed record NamedDestinationDefinition(
        string Name, int PageIndex, PdfDestination Destination);
    private sealed record OpenActionDefinition(int PageIndex, PdfDestination Destination);
    private sealed record PageLabelDefinition(
        int PageIndex, PdfPageLabelStyle Style, string? Prefix, int StartNumber);
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
