using KillerPdf.Engine.Syntax;

namespace KillerPdf.Engine.Diagnostics;

public enum PdfDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public enum PdfDiagnosticCode
{
    InvalidHeader,
    InvalidStartXref,
    InvalidCrossReference,
    InvalidIndirectObject,
    MissingCatalogRoot,
    InvalidCatalogRoot,
    InspectionLimitReached,
    AuthenticationFailed
}

/// <summary>A stable, machine-readable structural finding with a human-readable explanation.</summary>
public sealed record PdfDiagnostic(
    PdfDiagnosticCode Code,
    PdfDiagnosticSeverity Severity,
    string Message,
    int? Offset = null,
    int? ObjectNumber = null);

/// <summary>The non-throwing structural inspection result used to decide whether repair is needed.</summary>
public sealed class PdfInspectionReport
{
    private readonly PdfDiagnostic[] _diagnostics;

    internal PdfInspectionReport(
        PdfVersion? version,
        long? startXrefOffset,
        int? crossReferenceEntryCount,
        int inspectedObjectCount,
        IEnumerable<PdfDiagnostic> diagnostics)
    {
        Version = version;
        StartXrefOffset = startXrefOffset;
        CrossReferenceEntryCount = crossReferenceEntryCount;
        InspectedObjectCount = inspectedObjectCount;
        _diagnostics = diagnostics.ToArray();
    }

    public PdfVersion? Version { get; }
    public long? StartXrefOffset { get; }
    public int? CrossReferenceEntryCount { get; }
    public int InspectedObjectCount { get; }
    public IReadOnlyList<PdfDiagnostic> Diagnostics => _diagnostics;
    public bool RequiresAuthentication =>
        _diagnostics.Any(diagnostic => diagnostic.Code == PdfDiagnosticCode.AuthenticationFailed);
    public bool IsStructurallyValid =>
        !_diagnostics.Any(diagnostic => diagnostic.Severity == PdfDiagnosticSeverity.Error
            && diagnostic.Code != PdfDiagnosticCode.AuthenticationFailed);
    public bool RequiresRepair => !IsStructurallyValid;
}
