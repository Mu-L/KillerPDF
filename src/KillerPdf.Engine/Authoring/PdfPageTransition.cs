namespace KillerPdf.Engine.Authoring;

public enum PdfTransitionDimension { Horizontal, Vertical }
public enum PdfTransitionMotion { Inward, Outward }

public sealed class PdfPageTransition
{
    private PdfPageTransition(
        PdfPageTransitionStyle style, double duration,
        PdfTransitionDimension? dimension = null, PdfTransitionMotion? motion = null,
        int? direction = null, double? scale = null, bool opaque = false)
    {
        if (!double.IsFinite(duration) || duration <= 0)
            throw new ArgumentOutOfRangeException(nameof(duration));
        Style = style;
        Duration = duration;
        Dimension = dimension;
        Motion = motion;
        Direction = direction;
        Scale = scale;
        Opaque = opaque;
    }

    public static PdfPageTransition Replace(double duration = 1) =>
        new(PdfPageTransitionStyle.Replace, duration);
    public static PdfPageTransition Dissolve(double duration = 1) =>
        new(PdfPageTransitionStyle.Dissolve, duration);
    public static PdfPageTransition Fade(double duration = 1) =>
        new(PdfPageTransitionStyle.Fade, duration);
    public static PdfPageTransition Split(
        PdfTransitionDimension dimension, PdfTransitionMotion motion, double duration = 1) =>
        new(PdfPageTransitionStyle.Split, duration,
            DimensionValue(dimension), MotionValue(motion));
    public static PdfPageTransition Blinds(
        PdfTransitionDimension dimension, double duration = 1) =>
        new(PdfPageTransitionStyle.Blinds, duration, DimensionValue(dimension));
    public static PdfPageTransition Box(PdfTransitionMotion motion, double duration = 1) =>
        new(PdfPageTransitionStyle.Box, duration, motion: MotionValue(motion));
    public static PdfPageTransition Wipe(int direction, double duration = 1) =>
        new(PdfPageTransitionStyle.Wipe, duration, direction: Cardinal(direction));
    public static PdfPageTransition Glitter(int direction, double duration = 1)
    {
        if (direction is not (0 or 270 or 315))
            throw new ArgumentOutOfRangeException(nameof(direction));
        return new(PdfPageTransitionStyle.Glitter, duration, direction: direction);
    }
    public static PdfPageTransition Fly(
        int direction, PdfTransitionMotion motion, double scale = 1,
        bool opaque = false, double duration = 1)
    {
        if (!double.IsFinite(scale) || scale is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(scale));
        return new(PdfPageTransitionStyle.Fly, duration, motion: MotionValue(motion),
            direction: Cardinal(direction), scale: scale, opaque: opaque);
    }
    public static PdfPageTransition Push(int direction, double duration = 1) =>
        new(PdfPageTransitionStyle.Push, duration, direction: Cardinal(direction));
    public static PdfPageTransition Cover(int direction, double duration = 1) =>
        new(PdfPageTransitionStyle.Cover, duration, direction: Cardinal(direction));
    public static PdfPageTransition Uncover(int direction, double duration = 1) =>
        new(PdfPageTransitionStyle.Uncover, duration, direction: Cardinal(direction));

    internal PdfPageTransitionStyle Style { get; }
    public double Duration { get; }
    internal PdfTransitionDimension? Dimension { get; }
    internal PdfTransitionMotion? Motion { get; }
    internal int? Direction { get; }
    internal double? Scale { get; }
    internal bool Opaque { get; }

    private static int Cardinal(int direction)
    {
        if (direction is not (0 or 90 or 180 or 270))
            throw new ArgumentOutOfRangeException(nameof(direction));
        return direction;
    }
    private static PdfTransitionDimension DimensionValue(PdfTransitionDimension value) =>
        Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));
    private static PdfTransitionMotion MotionValue(PdfTransitionMotion value) =>
        Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));
}

internal enum PdfPageTransitionStyle
{
    Split, Blinds, Box, Wipe, Dissolve, Glitter, Replace, Fly, Push, Cover, Uncover, Fade
}
