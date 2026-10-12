using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>What one record of a display list does.</summary>
internal enum DisplayOperationKind : byte
{
    /// <summary><see cref="IRenderingBackend.SaveState"/>.</summary>
    SaveState,

    /// <summary><see cref="IRenderingBackend.RestoreState"/>.</summary>
    RestoreState,

    /// <summary><see cref="IRenderingBackend.SetTransform"/>; <see cref="DisplayOperation.First"/> indexes the transforms.</summary>
    SetTransform,

    /// <summary><see cref="IRenderingBackend.FillPath"/>; <see cref="DisplayOperation.First"/> indexes the paths, <see cref="DisplayOperation.Second"/> the colours.</summary>
    FillPath,

    /// <summary><see cref="IRenderingBackend.StrokePath"/>; <see cref="DisplayOperation.First"/> indexes the paths, <see cref="DisplayOperation.Second"/> the strokes.</summary>
    StrokePath,

    /// <summary><see cref="IRenderingBackend.IntersectClip"/>; <see cref="DisplayOperation.First"/> indexes the paths.</summary>
    IntersectClip,

    /// <summary><see cref="IRenderingBackend.DrawImage"/>; <see cref="DisplayOperation.First"/> indexes the images, <see cref="DisplayOperation.Second"/> the image paints.</summary>
    DrawImage,

    /// <summary><see cref="IRenderingBackend.FillImageMask"/>; <see cref="DisplayOperation.First"/> indexes the image masks, <see cref="DisplayOperation.Second"/> the colours.</summary>
    FillImageMask,
}

/// <summary>One record of a display list: a kind and up to two indexes into the list's tables, 16 bytes.</summary>
internal readonly record struct DisplayOperation(DisplayOperationKind Kind, FillRule FillRule, int First, int Second);

/// <summary>A path interned in a display list: a slice of its verbs and a slice of its points.</summary>
internal readonly record struct DisplayPath(int VerbStart, int VerbCount, int PointStart, int PointCount);

/// <summary>A colour in a display list, its components a slice of the list's component array.</summary>
internal readonly record struct DisplayColorEntry(
    PdfColorSpace ColorSpace,
    int ComponentStart,
    int ComponentCount,
    PdfDefaultColorSpaces DefaultColorSpaces,
    ColorConversion Conversion);

/// <summary>The colour and line parameters of a stroke in a display list, its dash array a slice of the list's dash array.</summary>
internal readonly record struct DisplayStroke(
    int ColorIndex,
    double LineWidth,
    LineCap LineCap,
    LineJoin LineJoin,
    double MiterLimit,
    int DashStart,
    int DashCount,
    double DashPhase,
    bool StrokeAdjustment);

/// <summary>What one paint of an interned image depends on beyond the image: the default colour spaces and the conversion at the paint.</summary>
internal readonly record struct DisplayImagePaint(PdfDefaultColorSpaces DefaultColorSpaces, ColorConversion Conversion);
