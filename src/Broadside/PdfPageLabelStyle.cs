namespace Broadside;

/// <summary>How the numeric portion of a page label is written: a page label dictionary's <c>S</c> entry.</summary>
/// <remarks>ISO 32000-2 §12.4.2, Table 161.</remarks>
public enum PdfPageLabelStyle
{
    /// <summary>No numeric portion: the label is the prefix alone (<c>S</c> absent).</summary>
    None,

    /// <summary>Decimal arabic numerals (<c>D</c>).</summary>
    Arabic,

    /// <summary>Uppercase roman numerals (<c>R</c>).</summary>
    UppercaseRoman,

    /// <summary>Lowercase roman numerals (<c>r</c>).</summary>
    LowercaseRoman,

    /// <summary>Uppercase letters, A to Z, then AA to ZZ, and so on (<c>A</c>).</summary>
    UppercaseLetters,

    /// <summary>Lowercase letters, a to z, then aa to zz, and so on (<c>a</c>).</summary>
    LowercaseLetters,
}
