using System.Globalization;

namespace Broadside;

/// <summary>A range of pages, first to last, numbered from 1: one pair of the viewer preference <c>PrintPageRange</c>.</summary>
/// <remarks>ISO 32000-2 §12.2, Table 147 (PDF 1.7). Unlike page indices elsewhere in PDF, these numbers start at 1.</remarks>
public readonly struct PdfPageRange : IEquatable<PdfPageRange>
{
    /// <summary>Initializes a new instance of the <see cref="PdfPageRange"/> struct.</summary>
    /// <param name="first">The first page, numbered from 1.</param>
    /// <param name="last">The last page, numbered from 1.</param>
    public PdfPageRange(int first, int last)
    {
        First = first;
        Last = last;
    }

    /// <summary>Gets the first page of the range, numbered from 1.</summary>
    public int First { get; }

    /// <summary>Gets the last page of the range, numbered from 1.</summary>
    public int Last { get; }

    /// <summary>Returns whether two ranges are equal.</summary>
    /// <param name="left">The first range.</param>
    /// <param name="right">The second range.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public static bool operator ==(PdfPageRange left, PdfPageRange right) => left.Equals(right);

    /// <summary>Returns whether two ranges differ.</summary>
    /// <param name="left">The first range.</param>
    /// <param name="right">The second range.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(PdfPageRange left, PdfPageRange right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(PdfPageRange other) => First == other.First && Last == other.Last;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PdfPageRange other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(First, Last);

    /// <summary>Returns the range as <c>first-last</c>.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{First}-{Last}");
}
