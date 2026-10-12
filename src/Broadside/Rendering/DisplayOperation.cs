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

    /// <summary><see cref="IRenderingBackend.IntersectClip"/>; <see cref="DisplayOperation.First"/> indexes the paths.</summary>
    IntersectClip,
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
