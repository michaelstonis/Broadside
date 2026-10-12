namespace Broadside;

/// <summary>The last field a date string gives; every later field takes its default.</summary>
/// <remarks>
/// ISO 32000-2 §7.9.4: each field of a PDF date may be present only if all the fields before it are, so the fields present are
/// always a prefix and this one value describes them. ISO 16684-1 §8.2.1.2 restricts an XMP date the same way.
/// </remarks>
public enum PdfDatePrecision
{
    /// <summary>The year only (<c>YYYY</c>).</summary>
    Year,

    /// <summary>The year and month.</summary>
    Month,

    /// <summary>The year, month and day.</summary>
    Day,

    /// <summary>Down to the hour (PDF dates only; an XMP date gives hours and minutes together).</summary>
    Hour,

    /// <summary>Down to the minute.</summary>
    Minute,

    /// <summary>Down to the second.</summary>
    Second,

    /// <summary>Down to a fraction of a second (XMP dates only).</summary>
    FractionOfSecond,
}
