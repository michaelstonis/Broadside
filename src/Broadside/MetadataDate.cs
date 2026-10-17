using System.Globalization;

namespace Broadside;

/// <summary>
/// A document date as the metadata writer carries it between the Info dictionary and the XMP packet: an instant, whether its time
/// zone is known, and how many fields it gives. Written as a PDF date (ISO 32000-2 §7.9.4) or an XMP date (ISO 16684-1 §8.2.1.2) that
/// denote the same instant to the same precision, which is what §14.3.4 calls "fully equivalent".
/// </summary>
internal readonly record struct MetadataDate(DateTimeOffset Value, bool HasTimeZone, PdfDatePrecision Precision)
{
    /// <summary>A date set through the API: to the second, in the offset it carries.</summary>
    public static MetadataDate FromInstant(DateTimeOffset value) =>
        new(Truncate(value), HasTimeZone: true, PdfDatePrecision.Second);

    /// <summary>A date from the Info dictionary; one without an offset is GMT (§7.9.4).</summary>
    public static MetadataDate From(PdfDate date) => new(Truncate(date.Value), HasTimeZone: true, Min(date.Precision));

    /// <summary>A date from the XMP packet; one without a time zone stays without one.</summary>
    public static MetadataDate From(XmpDate date) => new(Truncate(date.Value), date.HasTimeZone, Min(date.Precision));

    /// <summary>Formats the date as a PDF date, <c>D:YYYYMMDDHHmmSSOHH'mm</c> cut to the precision.</summary>
    /// <param name="terminatingApostrophe">Whether to end the offset with the apostrophe of PDF 1.7 and earlier (§7.9.4 NOTE 2).</param>
    public string ToPdfDate(bool terminatingApostrophe)
    {
        DateTime local = Value.DateTime;
        string text = "D:" + local.ToString(
            Precision switch
            {
                PdfDatePrecision.Year => "yyyy",
                PdfDatePrecision.Month => "yyyyMM",
                PdfDatePrecision.Day => "yyyyMMdd",
                PdfDatePrecision.Hour => "yyyyMMddHH",
                PdfDatePrecision.Minute => "yyyyMMddHHmm",
                _ => "yyyyMMddHHmmss",
            },
            CultureInfo.InvariantCulture);
        if (Precision < PdfDatePrecision.Hour || !HasTimeZone)
        {
            return text;
        }

        TimeSpan offset = Value.Offset;
        if (offset == TimeSpan.Zero)
        {
            return text + "Z";
        }

        TimeSpan magnitude = offset.Duration();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{text}{(offset < TimeSpan.Zero ? '-' : '+')}{magnitude.Hours:00}'{magnitude.Minutes:00}{(terminatingApostrophe ? "'" : string.Empty)}");
    }

    /// <summary>Formats the date as an XMP date, <c>YYYY-MM-DDThh:mm:ssTZD</c> cut to the precision (hours alone are written with minutes).</summary>
    public string ToXmpDate()
    {
        DateTime local = Value.DateTime;
        string text = local.ToString(
            Precision switch
            {
                PdfDatePrecision.Year => "yyyy",
                PdfDatePrecision.Month => "yyyy-MM",
                PdfDatePrecision.Day => "yyyy-MM-dd",
                PdfDatePrecision.Hour or PdfDatePrecision.Minute => "yyyy-MM-dd'T'HH:mm",
                _ => "yyyy-MM-dd'T'HH:mm:ss",
            },
            CultureInfo.InvariantCulture);
        if (Precision < PdfDatePrecision.Hour || !HasTimeZone)
        {
            return text;
        }

        TimeSpan offset = Value.Offset;
        return offset == TimeSpan.Zero
            ? text + "Z"
            : string.Create(CultureInfo.InvariantCulture, $"{text}{(offset < TimeSpan.Zero ? '-' : '+')}{offset.Duration().Hours:00}:{offset.Duration().Minutes:00}");
    }

    /// <summary>Whether two dates are the same instant to the same precision (the text may differ).</summary>
    public bool IsEquivalentTo(MetadataDate other) =>
        Value.UtcDateTime == other.Value.UtcDateTime && Precision == other.Precision && (HasTimeZone == other.HasTimeZone || Precision < PdfDatePrecision.Hour);

    private static DateTimeOffset Truncate(DateTimeOffset value) => new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), value.Offset);

    // Fractions of a second are written by neither form here: a PDF date has none.
    private static PdfDatePrecision Min(PdfDatePrecision precision) => precision > PdfDatePrecision.Second ? PdfDatePrecision.Second : precision;
}
