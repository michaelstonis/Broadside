namespace Broadside.Graphics;

/// <summary>Whether glyphs are filled, stroked, used as a clip, or invisible.</summary>
/// <remarks>ISO 32000-2 §9.3.6, Table 104.</remarks>
public enum TextRenderingMode
{
    /// <summary>0: fill.</summary>
    Fill = 0,

    /// <summary>1: stroke.</summary>
    Stroke = 1,

    /// <summary>2: fill, then stroke.</summary>
    FillStroke = 2,

    /// <summary>3: neither fill nor stroke (invisible).</summary>
    Invisible = 3,

    /// <summary>4: fill and add to the clipping path.</summary>
    FillClip = 4,

    /// <summary>5: stroke and add to the clipping path.</summary>
    StrokeClip = 5,

    /// <summary>6: fill, stroke and add to the clipping path.</summary>
    FillStrokeClip = 6,

    /// <summary>7: add to the clipping path.</summary>
    Clip = 7,
}
