using System.Globalization;
using Broadside.Objects;

namespace Broadside;

/// <summary>A version of the PDF specification, <c>major.minor</c>, as a file header or a catalog's <c>Version</c> entry states it.</summary>
/// <remarks>
/// ISO 32000-2 §7.5.2 (the header is <c>%PDF-1.n</c> or <c>%PDF-2.n</c> with a single-digit <c>n</c>) and §7.7.2, Table 29 (the
/// catalog's <c>Version</c> is a name such as <c>/1.7</c>). Versions compare numerically on the major, then the minor number.
/// </remarks>
public readonly struct PdfVersion : IEquatable<PdfVersion>, IComparable<PdfVersion>
{
    /// <summary>Initializes a new instance of the <see cref="PdfVersion"/> struct.</summary>
    /// <param name="major">The major version number.</param>
    /// <param name="minor">The minor version number.</param>
    /// <exception cref="ArgumentOutOfRangeException">A number is negative.</exception>
    public PdfVersion(int major, int minor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        Major = major;
        Minor = minor;
    }

    /// <summary>Gets the major version number: 1 or 2 for every published version.</summary>
    public int Major { get; }

    /// <summary>Gets the minor version number.</summary>
    public int Minor { get; }

    /// <summary>Returns whether two versions are equal.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public static bool operator ==(PdfVersion left, PdfVersion right) => left.Equals(right);

    /// <summary>Returns whether two versions differ.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(PdfVersion left, PdfVersion right) => !left.Equals(right);

    /// <summary>Returns whether <paramref name="left"/> is an earlier version than <paramref name="right"/>.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator <(PdfVersion left, PdfVersion right) => left.CompareTo(right) < 0;

    /// <summary>Returns whether <paramref name="left"/> is a later version than <paramref name="right"/>.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator >(PdfVersion left, PdfVersion right) => left.CompareTo(right) > 0;

    /// <summary>Returns whether <paramref name="left"/> is the same as or earlier than <paramref name="right"/>.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator <=(PdfVersion left, PdfVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Returns whether <paramref name="left"/> is the same as or later than <paramref name="right"/>.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator >=(PdfVersion left, PdfVersion right) => left.CompareTo(right) >= 0;

    /// <inheritdoc/>
    public int CompareTo(PdfVersion other) => Major != other.Major ? Major.CompareTo(other.Major) : Minor.CompareTo(other.Minor);

    /// <inheritdoc/>
    public bool Equals(PdfVersion other) => Major == other.Major && Minor == other.Minor;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PdfVersion other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Major, Minor);

    /// <summary>Returns the version as the header and the catalog write it, such as <c>1.7</c>.</summary>
    /// <returns>The version text.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");

    /// <summary>Parses <c>d.d</c> with a major version from 1 to 9, the form a header and a catalog <c>Version</c> name use.</summary>
    internal static bool TryParse(ReadOnlySpan<byte> text, out PdfVersion version)
    {
        if (text.Length == 3 && text[0] is >= (byte)'1' and <= (byte)'9' && text[1] == (byte)'.' && text[2] is >= (byte)'0' and <= (byte)'9')
        {
            version = new PdfVersion(text[0] - '0', text[2] - '0');
            return true;
        }

        version = default;
        return false;
    }

    /// <summary>Reads a version written as a number (<c>1.7</c> instead of <c>/1.7</c>), a common writer error.</summary>
    internal static bool TryFromNumber(CosNumber number, out PdfVersion version)
    {
        double tenths = Math.Round(number.ToDouble() * 10);
        if (tenths is >= 10 and < 100)
        {
            version = new PdfVersion((int)(tenths / 10), (int)(tenths % 10));
            return true;
        }

        version = default;
        return false;
    }
}
