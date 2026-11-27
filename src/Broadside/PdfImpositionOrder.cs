namespace Broadside;

/// <summary>The order in which <see cref="PdfDocument.CreateNUp"/> fills the cells of a sheet with source pages.</summary>
/// <remarks>ISO 32000-2 §8.10.1: each page is placed as a form XObject; the order itself is a layout convention of print imposition, not a specification feature.</remarks>
public enum PdfImpositionOrder
{
    /// <summary>Across each row from left to right, rows from top to bottom.</summary>
    LeftToRightThenDown,

    /// <summary>Across each row from right to left, rows from top to bottom, for right-to-left reading.</summary>
    RightToLeftThenDown,

    /// <summary>Down each column from top to bottom, columns from left to right.</summary>
    TopToBottomThenRight,
}
