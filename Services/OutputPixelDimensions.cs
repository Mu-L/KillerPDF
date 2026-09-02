namespace KillerPDF.Services;

internal static class OutputPixelDimensions
{
    internal static (int Width, int Height) FromPoints(
        double widthPoints, double heightPoints, double dpi)
    {
        if (!double.IsFinite(widthPoints) || !double.IsFinite(heightPoints)
            || !double.IsFinite(dpi) || widthPoints <= 0 || heightPoints <= 0 || dpi <= 0)
            return (0, 0);

        return (
            Math.Max(1, (int)Math.Round(widthPoints / 72.0 * dpi)),
            Math.Max(1, (int)Math.Round(heightPoints / 72.0 * dpi)));
    }
}
