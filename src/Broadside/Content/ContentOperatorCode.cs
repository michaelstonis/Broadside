namespace Broadside.Content;

/// <summary>The content stream operators of ISO 32000-2, one value per operator keyword, and <see cref="Unknown"/> for any other keyword.</summary>
/// <remarks>
/// ISO 32000-2 Annex A, Table A.1 (73 operators), described in the clause tables cited on each value. Table A.1's own cross
/// references contain errata (<c>BMC</c>, <c>BT</c> and <c>c</c>); the clause tables are authoritative.
/// </remarks>
public enum ContentOperatorCode
{
    /// <summary>A keyword that is not a PDF operator (§7.8.2): ignored, silently inside a compatibility section.</summary>
    Unknown = 0,

    /// <summary><c>q</c>: save the graphics state (§8.4.4, Table 56).</summary>
    SaveState,

    /// <summary><c>Q</c>: restore the graphics state (§8.4.4, Table 56).</summary>
    RestoreState,

    /// <summary><c>cm</c>: concatenate a matrix to the CTM (§8.4.4, Table 56).</summary>
    ConcatenateMatrix,

    /// <summary><c>w</c>: set the line width (§8.4.4, Table 56).</summary>
    SetLineWidth,

    /// <summary><c>J</c>: set the line cap style (§8.4.4, Table 56).</summary>
    SetLineCap,

    /// <summary><c>j</c>: set the line join style (§8.4.4, Table 56).</summary>
    SetLineJoin,

    /// <summary><c>M</c>: set the miter limit (§8.4.4, Table 56).</summary>
    SetMiterLimit,

    /// <summary><c>d</c>: set the line dash pattern (§8.4.4, Table 56).</summary>
    SetDash,

    /// <summary><c>ri</c>: set the colour rendering intent (§8.4.4, Table 56).</summary>
    SetRenderingIntent,

    /// <summary><c>i</c>: set the flatness tolerance (§8.4.4, Table 56).</summary>
    SetFlatness,

    /// <summary><c>gs</c>: set parameters from a graphics state parameter dictionary (§8.4.4, Table 56; §8.4.5).</summary>
    SetGraphicsStateParameters,

    /// <summary><c>m</c>: begin a subpath (§8.5.2, Table 58).</summary>
    MoveTo,

    /// <summary><c>l</c>: append a line segment (§8.5.2, Table 58).</summary>
    LineTo,

    /// <summary><c>c</c>: append a cubic Bézier curve (§8.5.2, Table 58).</summary>
    CurveTo,

    /// <summary><c>v</c>: append a cubic Bézier curve whose first control point is the current point (§8.5.2, Table 58).</summary>
    CurveToInitialPointReplicated,

    /// <summary><c>y</c>: append a cubic Bézier curve whose second control point is its end point (§8.5.2, Table 58).</summary>
    CurveToFinalPointReplicated,

    /// <summary><c>h</c>: close the current subpath (§8.5.2, Table 58).</summary>
    ClosePath,

    /// <summary><c>re</c>: append a rectangle as a complete subpath (§8.5.2, Table 58).</summary>
    Rectangle,

    /// <summary><c>S</c>: stroke the path (§8.5.3, Table 59).</summary>
    Stroke,

    /// <summary><c>s</c>: close and stroke the path (§8.5.3, Table 59).</summary>
    CloseAndStroke,

    /// <summary><c>f</c>: fill the path, non-zero winding number rule (§8.5.3, Table 59).</summary>
    Fill,

    /// <summary><c>F</c>: as <c>f</c>; deprecated in PDF 2.0 but readers shall accept it (§8.5.3, Table 59).</summary>
    FillObsolete,

    /// <summary><c>f*</c>: fill the path, even-odd rule (§8.5.3, Table 59).</summary>
    FillEvenOdd,

    /// <summary><c>B</c>: fill, non-zero rule, then stroke (§8.5.3, Table 59).</summary>
    FillAndStroke,

    /// <summary><c>B*</c>: fill, even-odd rule, then stroke (§8.5.3, Table 59).</summary>
    FillEvenOddAndStroke,

    /// <summary><c>b</c>: close, fill with the non-zero rule, then stroke (§8.5.3, Table 59).</summary>
    CloseFillAndStroke,

    /// <summary><c>b*</c>: close, fill with the even-odd rule, then stroke (§8.5.3, Table 59).</summary>
    CloseFillEvenOddAndStroke,

    /// <summary><c>n</c>: end the path without painting it (§8.5.3, Table 59).</summary>
    EndPath,

    /// <summary><c>W</c>: clip with the path, non-zero rule, after the next painting operator (§8.5.4, Table 60).</summary>
    Clip,

    /// <summary><c>W*</c>: clip with the path, even-odd rule, after the next painting operator (§8.5.4, Table 60).</summary>
    ClipEvenOdd,

    /// <summary><c>BT</c>: begin a text object (§9.4.1, Table 105).</summary>
    BeginText,

    /// <summary><c>ET</c>: end a text object (§9.4.1, Table 105).</summary>
    EndText,

    /// <summary><c>Tc</c>: set the character spacing (§9.3.1, Table 103).</summary>
    SetCharacterSpacing,

    /// <summary><c>Tw</c>: set the word spacing (§9.3.1, Table 103).</summary>
    SetWordSpacing,

    /// <summary><c>Tz</c>: set the horizontal scaling (§9.3.1, Table 103).</summary>
    SetHorizontalScaling,

    /// <summary><c>TL</c>: set the leading (§9.3.1, Table 103).</summary>
    SetLeading,

    /// <summary><c>Tf</c>: set the font and size (§9.3.1, Table 103).</summary>
    SetFont,

    /// <summary><c>Tr</c>: set the text rendering mode (§9.3.1, Table 103).</summary>
    SetTextRenderingMode,

    /// <summary><c>Ts</c>: set the text rise (§9.3.1, Table 103).</summary>
    SetTextRise,

    /// <summary><c>Td</c>: move to the start of the next line (§9.4.2, Table 106).</summary>
    MoveText,

    /// <summary><c>TD</c>: move to the start of the next line and set the leading (§9.4.2, Table 106).</summary>
    MoveTextSetLeading,

    /// <summary><c>Tm</c>: set the text matrix and text line matrix (§9.4.2, Table 106).</summary>
    SetTextMatrix,

    /// <summary><c>T*</c>: move to the start of the next line by the leading (§9.4.2, Table 106).</summary>
    NextLine,

    /// <summary><c>Tj</c>: show a string (§9.4.3, Table 107).</summary>
    ShowText,

    /// <summary><c>TJ</c>: show strings with individual position adjustments (§9.4.3, Table 107).</summary>
    ShowTextAdjusted,

    /// <summary><c>'</c>: move to the next line and show a string (§9.4.3, Table 107).</summary>
    NextLineShowText,

    /// <summary><c>"</c>: set word and character spacing, move to the next line and show a string (§9.4.3, Table 107).</summary>
    NextLineShowTextWithSpacing,

    /// <summary><c>d0</c>: set the glyph width of a Type 3 glyph (§9.6.4, Table 111).</summary>
    SetGlyphWidth,

    /// <summary><c>d1</c>: set the glyph width and bounding box of a Type 3 glyph (§9.6.4, Table 111).</summary>
    SetGlyphWidthAndBoundingBox,

    /// <summary><c>CS</c>: set the stroking colour space (§8.6.8, Table 73).</summary>
    SetStrokeColorSpace,

    /// <summary><c>cs</c>: set the non-stroking colour space (§8.6.8, Table 73).</summary>
    SetFillColorSpace,

    /// <summary><c>SC</c>: set the stroking colour (§8.6.8, Table 73).</summary>
    SetStrokeColor,

    /// <summary><c>SCN</c>: set the stroking colour, including Pattern, Separation, DeviceN and ICCBased (§8.6.8, Table 73).</summary>
    SetStrokeColorExtended,

    /// <summary><c>sc</c>: set the non-stroking colour (§8.6.8, Table 73).</summary>
    SetFillColor,

    /// <summary><c>scn</c>: set the non-stroking colour, including Pattern, Separation, DeviceN and ICCBased (§8.6.8, Table 73).</summary>
    SetFillColorExtended,

    /// <summary><c>G</c>: set DeviceGray and a stroking gray level (§8.6.8, Table 73).</summary>
    SetStrokeGray,

    /// <summary><c>g</c>: set DeviceGray and a non-stroking gray level (§8.6.8, Table 73).</summary>
    SetFillGray,

    /// <summary><c>RG</c>: set DeviceRGB and a stroking colour (§8.6.8, Table 73).</summary>
    SetStrokeRgb,

    /// <summary><c>rg</c>: set DeviceRGB and a non-stroking colour (§8.6.8, Table 73).</summary>
    SetFillRgb,

    /// <summary><c>K</c>: set DeviceCMYK and a stroking colour (§8.6.8, Table 73).</summary>
    SetStrokeCmyk,

    /// <summary><c>k</c>: set DeviceCMYK and a non-stroking colour (§8.6.8, Table 73).</summary>
    SetFillCmyk,

    /// <summary><c>sh</c>: paint a shading (§8.7.4.2, Table 76).</summary>
    PaintShading,

    /// <summary><c>BI</c>: an inline image; its dictionary is the operand and its data travels with the operator (§8.9.7, Table 90).</summary>
    BeginInlineImage,

    /// <summary><c>ID</c>: begin inline image data; only reported when it appears outside an inline image (§8.9.7, Table 90).</summary>
    BeginInlineImageData,

    /// <summary><c>EI</c>: end an inline image; only reported when it appears outside an inline image (§8.9.7, Table 90).</summary>
    EndInlineImage,

    /// <summary><c>Do</c>: paint an external object (§8.8, Table 86).</summary>
    PaintXObject,

    /// <summary><c>MP</c>: a marked-content point (§14.6, Table 352).</summary>
    MarkPoint,

    /// <summary><c>DP</c>: a marked-content point with a property list (§14.6, Table 352).</summary>
    MarkPointWithProperties,

    /// <summary><c>BMC</c>: begin a marked-content sequence (§14.6, Table 352).</summary>
    BeginMarkedContent,

    /// <summary><c>BDC</c>: begin a marked-content sequence with a property list (§14.6, Table 352).</summary>
    BeginMarkedContentWithProperties,

    /// <summary><c>EMC</c>: end a marked-content sequence (§14.6, Table 352).</summary>
    EndMarkedContent,

    /// <summary><c>BX</c>: begin a compatibility section (§7.8.2, Table 33).</summary>
    BeginCompatibility,

    /// <summary><c>EX</c>: end a compatibility section (§7.8.2, Table 33).</summary>
    EndCompatibility,
}
