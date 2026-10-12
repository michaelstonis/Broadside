namespace Broadside.Content;

/// <summary>The category of an operator, as Table 50 groups them; Figure 9 says in which context each category may appear.</summary>
/// <remarks>ISO 32000-2 §8.2, Table 50 and Figure 9 (2020 edition: <c>q</c> and <c>Q</c> are general graphics state operators).</remarks>
internal enum OperatorCategory : byte
{
    Unknown,
    GeneralGraphicsState,
    SpecialGraphicsState,
    PathConstruction,
    PathPainting,
    ClippingPath,
    TextObject,
    TextState,
    TextPositioning,
    TextShowing,
    Type3Font,
    Color,
    Shading,
    InlineImage,
    XObject,
    MarkedContent,
    Compatibility,
}

/// <summary>
/// What an operator takes: a fixed list of operand kinds, or a variable count (the colour operators). Kinds: <c>n</c> number,
/// <c>N</c> name, <c>s</c> string, <c>a</c> array, <c>d</c> dictionary, <c>D</c> dictionary or name.
/// </summary>
internal readonly ref struct OperandSignature(ReadOnlySpan<byte> kinds, bool variable)
{
    /// <summary>Gets the operand kinds, in order; for a variable signature, the kinds every operand may have.</summary>
    public ReadOnlySpan<byte> Kinds { get; } = kinds;

    /// <summary>Gets a value indicating whether the count varies: <c>SC</c>, <c>sc</c> (numbers), <c>SCN</c>, <c>scn</c> (numbers, then optionally a name).</summary>
    public bool IsVariable { get; } = variable;
}

/// <summary>
/// The operator keywords of Annex A, Table A.1: lookup from bytes without creating a string, the keyword of each operator, its
/// operands and its category.
/// </summary>
/// <remarks>ISO 32000-2 Annex A and the clause tables 33, 56, 58-60, 73, 76, 86, 90, 103, 105-107, 111 and 352.</remarks>
internal static class OperatorTable
{
    /// <summary>The longest operator keyword.</summary>
    public const int MaxKeywordLength = 3;

    /// <summary>Returns the operator named by <paramref name="keyword"/>, or <see cref="ContentOperatorCode.Unknown"/>.</summary>
    public static ContentOperatorCode Lookup(ReadOnlySpan<byte> keyword) => keyword.Length switch
    {
        1 => One((char)keyword[0]),
        2 => Two((char)keyword[0], (char)keyword[1]),
        3 => Three((char)keyword[0], (char)keyword[1], (char)keyword[2]),
        _ => ContentOperatorCode.Unknown,
    };

    /// <summary>Returns the keyword of <paramref name="code"/>; empty for <see cref="ContentOperatorCode.Unknown"/>.</summary>
    public static ReadOnlySpan<byte> Keyword(ContentOperatorCode code) => code switch
    {
        ContentOperatorCode.SaveState => "q"u8,
        ContentOperatorCode.RestoreState => "Q"u8,
        ContentOperatorCode.ConcatenateMatrix => "cm"u8,
        ContentOperatorCode.SetLineWidth => "w"u8,
        ContentOperatorCode.SetLineCap => "J"u8,
        ContentOperatorCode.SetLineJoin => "j"u8,
        ContentOperatorCode.SetMiterLimit => "M"u8,
        ContentOperatorCode.SetDash => "d"u8,
        ContentOperatorCode.SetRenderingIntent => "ri"u8,
        ContentOperatorCode.SetFlatness => "i"u8,
        ContentOperatorCode.SetGraphicsStateParameters => "gs"u8,
        ContentOperatorCode.MoveTo => "m"u8,
        ContentOperatorCode.LineTo => "l"u8,
        ContentOperatorCode.CurveTo => "c"u8,
        ContentOperatorCode.CurveToInitialPointReplicated => "v"u8,
        ContentOperatorCode.CurveToFinalPointReplicated => "y"u8,
        ContentOperatorCode.ClosePath => "h"u8,
        ContentOperatorCode.Rectangle => "re"u8,
        ContentOperatorCode.Stroke => "S"u8,
        ContentOperatorCode.CloseAndStroke => "s"u8,
        ContentOperatorCode.Fill => "f"u8,
        ContentOperatorCode.FillObsolete => "F"u8,
        ContentOperatorCode.FillEvenOdd => "f*"u8,
        ContentOperatorCode.FillAndStroke => "B"u8,
        ContentOperatorCode.FillEvenOddAndStroke => "B*"u8,
        ContentOperatorCode.CloseFillAndStroke => "b"u8,
        ContentOperatorCode.CloseFillEvenOddAndStroke => "b*"u8,
        ContentOperatorCode.EndPath => "n"u8,
        ContentOperatorCode.Clip => "W"u8,
        ContentOperatorCode.ClipEvenOdd => "W*"u8,
        ContentOperatorCode.BeginText => "BT"u8,
        ContentOperatorCode.EndText => "ET"u8,
        ContentOperatorCode.SetCharacterSpacing => "Tc"u8,
        ContentOperatorCode.SetWordSpacing => "Tw"u8,
        ContentOperatorCode.SetHorizontalScaling => "Tz"u8,
        ContentOperatorCode.SetLeading => "TL"u8,
        ContentOperatorCode.SetFont => "Tf"u8,
        ContentOperatorCode.SetTextRenderingMode => "Tr"u8,
        ContentOperatorCode.SetTextRise => "Ts"u8,
        ContentOperatorCode.MoveText => "Td"u8,
        ContentOperatorCode.MoveTextSetLeading => "TD"u8,
        ContentOperatorCode.SetTextMatrix => "Tm"u8,
        ContentOperatorCode.NextLine => "T*"u8,
        ContentOperatorCode.ShowText => "Tj"u8,
        ContentOperatorCode.ShowTextAdjusted => "TJ"u8,
        ContentOperatorCode.NextLineShowText => "'"u8,
        ContentOperatorCode.NextLineShowTextWithSpacing => "\""u8,
        ContentOperatorCode.SetGlyphWidth => "d0"u8,
        ContentOperatorCode.SetGlyphWidthAndBoundingBox => "d1"u8,
        ContentOperatorCode.SetStrokeColorSpace => "CS"u8,
        ContentOperatorCode.SetFillColorSpace => "cs"u8,
        ContentOperatorCode.SetStrokeColor => "SC"u8,
        ContentOperatorCode.SetStrokeColorExtended => "SCN"u8,
        ContentOperatorCode.SetFillColor => "sc"u8,
        ContentOperatorCode.SetFillColorExtended => "scn"u8,
        ContentOperatorCode.SetStrokeGray => "G"u8,
        ContentOperatorCode.SetFillGray => "g"u8,
        ContentOperatorCode.SetStrokeRgb => "RG"u8,
        ContentOperatorCode.SetFillRgb => "rg"u8,
        ContentOperatorCode.SetStrokeCmyk => "K"u8,
        ContentOperatorCode.SetFillCmyk => "k"u8,
        ContentOperatorCode.PaintShading => "sh"u8,
        ContentOperatorCode.BeginInlineImage => "BI"u8,
        ContentOperatorCode.BeginInlineImageData => "ID"u8,
        ContentOperatorCode.EndInlineImage => "EI"u8,
        ContentOperatorCode.PaintXObject => "Do"u8,
        ContentOperatorCode.MarkPoint => "MP"u8,
        ContentOperatorCode.MarkPointWithProperties => "DP"u8,
        ContentOperatorCode.BeginMarkedContent => "BMC"u8,
        ContentOperatorCode.BeginMarkedContentWithProperties => "BDC"u8,
        ContentOperatorCode.EndMarkedContent => "EMC"u8,
        ContentOperatorCode.BeginCompatibility => "BX"u8,
        ContentOperatorCode.EndCompatibility => "EX"u8,
        _ => [],
    };

    /// <summary>Returns the operands <paramref name="code"/> takes.</summary>
    public static OperandSignature Signature(ContentOperatorCode code) => code switch
    {
        ContentOperatorCode.ConcatenateMatrix or ContentOperatorCode.CurveTo or ContentOperatorCode.SetTextMatrix
            or ContentOperatorCode.SetGlyphWidthAndBoundingBox => new("nnnnnn"u8, false),
        ContentOperatorCode.SetLineWidth or ContentOperatorCode.SetLineCap or ContentOperatorCode.SetLineJoin or ContentOperatorCode.SetMiterLimit
            or ContentOperatorCode.SetFlatness or ContentOperatorCode.SetCharacterSpacing or ContentOperatorCode.SetWordSpacing
            or ContentOperatorCode.SetHorizontalScaling or ContentOperatorCode.SetLeading or ContentOperatorCode.SetTextRenderingMode
            or ContentOperatorCode.SetTextRise or ContentOperatorCode.SetStrokeGray or ContentOperatorCode.SetFillGray => new("n"u8, false),
        ContentOperatorCode.SetDash => new("an"u8, false),
        ContentOperatorCode.SetRenderingIntent or ContentOperatorCode.SetGraphicsStateParameters or ContentOperatorCode.SetStrokeColorSpace
            or ContentOperatorCode.SetFillColorSpace or ContentOperatorCode.PaintShading or ContentOperatorCode.PaintXObject
            or ContentOperatorCode.MarkPoint or ContentOperatorCode.BeginMarkedContent => new("N"u8, false),
        ContentOperatorCode.MoveTo or ContentOperatorCode.LineTo or ContentOperatorCode.MoveText or ContentOperatorCode.MoveTextSetLeading
            or ContentOperatorCode.SetGlyphWidth => new("nn"u8, false),
        ContentOperatorCode.CurveToInitialPointReplicated or ContentOperatorCode.CurveToFinalPointReplicated or ContentOperatorCode.Rectangle
            or ContentOperatorCode.SetStrokeCmyk or ContentOperatorCode.SetFillCmyk => new("nnnn"u8, false),
        ContentOperatorCode.SetStrokeRgb or ContentOperatorCode.SetFillRgb => new("nnn"u8, false),
        ContentOperatorCode.SetFont => new("Nn"u8, false),
        ContentOperatorCode.ShowText or ContentOperatorCode.NextLineShowText => new("s"u8, false),
        ContentOperatorCode.ShowTextAdjusted => new("a"u8, false),
        ContentOperatorCode.NextLineShowTextWithSpacing => new("nns"u8, false),
        ContentOperatorCode.MarkPointWithProperties or ContentOperatorCode.BeginMarkedContentWithProperties => new("ND"u8, false),
        ContentOperatorCode.BeginInlineImage => new("d"u8, false),
        ContentOperatorCode.SetStrokeColor or ContentOperatorCode.SetFillColor => new("n"u8, true),
        ContentOperatorCode.SetStrokeColorExtended or ContentOperatorCode.SetFillColorExtended => new("nN"u8, true),
        _ => new([], false),
    };

    /// <summary>Returns the Table 50 category of <paramref name="code"/>.</summary>
    public static OperatorCategory Category(ContentOperatorCode code) => code switch
    {
        ContentOperatorCode.Unknown => OperatorCategory.Unknown,
        <= ContentOperatorCode.RestoreState => OperatorCategory.GeneralGraphicsState,
        ContentOperatorCode.ConcatenateMatrix => OperatorCategory.SpecialGraphicsState,
        <= ContentOperatorCode.SetGraphicsStateParameters => OperatorCategory.GeneralGraphicsState,
        <= ContentOperatorCode.Rectangle => OperatorCategory.PathConstruction,
        <= ContentOperatorCode.EndPath => OperatorCategory.PathPainting,
        <= ContentOperatorCode.ClipEvenOdd => OperatorCategory.ClippingPath,
        <= ContentOperatorCode.EndText => OperatorCategory.TextObject,
        <= ContentOperatorCode.SetTextRise => OperatorCategory.TextState,
        <= ContentOperatorCode.NextLine => OperatorCategory.TextPositioning,
        <= ContentOperatorCode.NextLineShowTextWithSpacing => OperatorCategory.TextShowing,
        <= ContentOperatorCode.SetGlyphWidthAndBoundingBox => OperatorCategory.Type3Font,
        <= ContentOperatorCode.SetFillCmyk => OperatorCategory.Color,
        ContentOperatorCode.PaintShading => OperatorCategory.Shading,
        <= ContentOperatorCode.EndInlineImage => OperatorCategory.InlineImage,
        ContentOperatorCode.PaintXObject => OperatorCategory.XObject,
        <= ContentOperatorCode.EndMarkedContent => OperatorCategory.MarkedContent,
        _ => OperatorCategory.Compatibility,
    };

    private static ContentOperatorCode One(char a) => a switch
    {
        'q' => ContentOperatorCode.SaveState,
        'Q' => ContentOperatorCode.RestoreState,
        'w' => ContentOperatorCode.SetLineWidth,
        'J' => ContentOperatorCode.SetLineCap,
        'j' => ContentOperatorCode.SetLineJoin,
        'M' => ContentOperatorCode.SetMiterLimit,
        'd' => ContentOperatorCode.SetDash,
        'i' => ContentOperatorCode.SetFlatness,
        'm' => ContentOperatorCode.MoveTo,
        'l' => ContentOperatorCode.LineTo,
        'c' => ContentOperatorCode.CurveTo,
        'v' => ContentOperatorCode.CurveToInitialPointReplicated,
        'y' => ContentOperatorCode.CurveToFinalPointReplicated,
        'h' => ContentOperatorCode.ClosePath,
        'S' => ContentOperatorCode.Stroke,
        's' => ContentOperatorCode.CloseAndStroke,
        'f' => ContentOperatorCode.Fill,
        'F' => ContentOperatorCode.FillObsolete,
        'B' => ContentOperatorCode.FillAndStroke,
        'b' => ContentOperatorCode.CloseFillAndStroke,
        'n' => ContentOperatorCode.EndPath,
        'W' => ContentOperatorCode.Clip,
        'G' => ContentOperatorCode.SetStrokeGray,
        'g' => ContentOperatorCode.SetFillGray,
        'K' => ContentOperatorCode.SetStrokeCmyk,
        'k' => ContentOperatorCode.SetFillCmyk,
        '\'' => ContentOperatorCode.NextLineShowText,
        '"' => ContentOperatorCode.NextLineShowTextWithSpacing,
        _ => ContentOperatorCode.Unknown,
    };

    private static ContentOperatorCode Two(char a, char b) => (a, b) switch
    {
        ('c', 'm') => ContentOperatorCode.ConcatenateMatrix,
        ('r', 'i') => ContentOperatorCode.SetRenderingIntent,
        ('g', 's') => ContentOperatorCode.SetGraphicsStateParameters,
        ('r', 'e') => ContentOperatorCode.Rectangle,
        ('f', '*') => ContentOperatorCode.FillEvenOdd,
        ('B', '*') => ContentOperatorCode.FillEvenOddAndStroke,
        ('b', '*') => ContentOperatorCode.CloseFillEvenOddAndStroke,
        ('W', '*') => ContentOperatorCode.ClipEvenOdd,
        ('B', 'T') => ContentOperatorCode.BeginText,
        ('E', 'T') => ContentOperatorCode.EndText,
        ('T', 'c') => ContentOperatorCode.SetCharacterSpacing,
        ('T', 'w') => ContentOperatorCode.SetWordSpacing,
        ('T', 'z') => ContentOperatorCode.SetHorizontalScaling,
        ('T', 'L') => ContentOperatorCode.SetLeading,
        ('T', 'f') => ContentOperatorCode.SetFont,
        ('T', 'r') => ContentOperatorCode.SetTextRenderingMode,
        ('T', 's') => ContentOperatorCode.SetTextRise,
        ('T', 'd') => ContentOperatorCode.MoveText,
        ('T', 'D') => ContentOperatorCode.MoveTextSetLeading,
        ('T', 'm') => ContentOperatorCode.SetTextMatrix,
        ('T', '*') => ContentOperatorCode.NextLine,
        ('T', 'j') => ContentOperatorCode.ShowText,
        ('T', 'J') => ContentOperatorCode.ShowTextAdjusted,
        ('d', '0') => ContentOperatorCode.SetGlyphWidth,
        ('d', '1') => ContentOperatorCode.SetGlyphWidthAndBoundingBox,
        ('C', 'S') => ContentOperatorCode.SetStrokeColorSpace,
        ('c', 's') => ContentOperatorCode.SetFillColorSpace,
        ('S', 'C') => ContentOperatorCode.SetStrokeColor,
        ('s', 'c') => ContentOperatorCode.SetFillColor,
        ('R', 'G') => ContentOperatorCode.SetStrokeRgb,
        ('r', 'g') => ContentOperatorCode.SetFillRgb,
        ('s', 'h') => ContentOperatorCode.PaintShading,
        ('B', 'I') => ContentOperatorCode.BeginInlineImage,
        ('I', 'D') => ContentOperatorCode.BeginInlineImageData,
        ('E', 'I') => ContentOperatorCode.EndInlineImage,
        ('D', 'o') => ContentOperatorCode.PaintXObject,
        ('M', 'P') => ContentOperatorCode.MarkPoint,
        ('D', 'P') => ContentOperatorCode.MarkPointWithProperties,
        ('B', 'X') => ContentOperatorCode.BeginCompatibility,
        ('E', 'X') => ContentOperatorCode.EndCompatibility,
        _ => ContentOperatorCode.Unknown,
    };

    private static ContentOperatorCode Three(char a, char b, char c) => (a, b, c) switch
    {
        ('S', 'C', 'N') => ContentOperatorCode.SetStrokeColorExtended,
        ('s', 'c', 'n') => ContentOperatorCode.SetFillColorExtended,
        ('B', 'M', 'C') => ContentOperatorCode.BeginMarkedContent,
        ('B', 'D', 'C') => ContentOperatorCode.BeginMarkedContentWithProperties,
        ('E', 'M', 'C') => ContentOperatorCode.EndMarkedContent,
        _ => ContentOperatorCode.Unknown,
    };
}
