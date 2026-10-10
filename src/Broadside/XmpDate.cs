using System.Globalization;

namespace Broadside;

/// <summary>A date as XMP writes it, a subset of ISO 8601 such as <c>2014-09-24T21:23:03+02:00</c>: the instant and the text.</summary>
/// <remarks>
/// <para>
/// ISO 16684-1 §8.2.1.2: <c>YYYY</c>, <c>YYYY-MM</c>, <c>YYYY-MM-DD</c>, <c>YYYY-MM-DDThh:mmTZD</c>, <c>YYYY-MM-DDThh:mm:ssTZD</c> or
/// <c>YYYY-MM-DDThh:mm:ss.sTZD</c>, where the time zone designator <c>TZD</c> is <c>Z</c> or <c>+hh:mm</c> / <c>-hh:mm</c>. Fields
/// not given default to 01 (month and day) or zero.
/// </para>
/// <para>
/// The time zone is optional and its absence means the zone is unknown, not Universal Time. <see cref="Value"/> then carries the
/// local time with a zero offset and <see cref="HasTimeZone"/> is <see langword="false"/>: compare such a value as a local time, not
/// as an instant.
/// </para>
/// </remarks>
public readonly struct XmpDate : IEquatable<XmpDate>
{
    private XmpDate(DateTimeOffset value, bool hasTimeZone, PdfDatePrecision precision, string text)
    {
        Value = value;
        HasTimeZone = hasTimeZone;
        Precision = precision;
        Text = text;
    }

    /// <summary>Gets the date; with a zero offset and the local time when <see cref="HasTimeZone"/> is <see langword="false"/>.</summary>
    public DateTimeOffset Value { get; }

    /// <summary>Gets a value indicating whether the text gives a time zone designator.</summary>
    public bool HasTimeZone { get; }

    /// <summary>Gets the last field the text gives.</summary>
    public PdfDatePrecision Precision { get; }

    /// <summary>Gets the text the date was read from.</summary>
    public string Text => field ?? string.Empty;

    /// <summary>Returns whether two dates are equal: the same value, offset, precision and text.</summary>
    /// <param name="left">The first date.</param>
    /// <param name="right">The second date.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public static bool operator ==(XmpDate left, XmpDate right) => left.Equals(right);

    /// <summary>Returns whether two dates differ.</summary>
    /// <param name="left">The first date.</param>
    /// <param name="right">The second date.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(XmpDate left, XmpDate right) => !left.Equals(right);

    /// <summary>Parses an XMP date; never throws.</summary>
    /// <param name="text">The text, such as <c>2014-09-24T21:23:03Z</c>. Leading and trailing white-space is ignored.</param>
    /// <param name="date">The date, or <see langword="default"/> when <paramref name="text"/> is not one.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> is a date in one of the forms the remarks list.</returns>
    /// <remarks>ISO 16684-1 §8.2.1.2.</remarks>
    public static bool TryParse(string? text, out XmpDate date)
    {
        date = default;
        if (text is null)
        {
            return false;
        }

        ReadOnlySpan<char> rest = text.AsSpan().Trim();
        if (!TryNumber(ref rest, 4, out int year))
        {
            return false;
        }

        int month = 1, day = 1, hour = 0, minute = 0, second = 0;
        long fractionTicks = 0;
        PdfDatePrecision precision = PdfDatePrecision.Year;
        bool hasTimeZone = false;
        int offsetMinutes = 0;
        if (Take(ref rest, '-'))
        {
            if (!TryNumber(ref rest, 2, out month))
            {
                return false;
            }

            precision = PdfDatePrecision.Month;
            if (Take(ref rest, '-'))
            {
                if (!TryNumber(ref rest, 2, out day))
                {
                    return false;
                }

                precision = PdfDatePrecision.Day;
                if (Take(ref rest, 'T'))
                {
                    if (!TryNumber(ref rest, 2, out hour) || !Take(ref rest, ':') || !TryNumber(ref rest, 2, out minute))
                    {
                        return false;
                    }

                    precision = PdfDatePrecision.Minute;
                    if (Take(ref rest, ':'))
                    {
                        if (!TryNumber(ref rest, 2, out second))
                        {
                            return false;
                        }

                        precision = PdfDatePrecision.Second;
                        if (Take(ref rest, '.'))
                        {
                            if (!TryFraction(ref rest, out fractionTicks))
                            {
                                return false;
                            }

                            precision = PdfDatePrecision.FractionOfSecond;
                        }
                    }

                    if (!TryTimeZone(ref rest, out hasTimeZone, out offsetMinutes))
                    {
                        return false;
                    }
                }
            }
        }

        if (!rest.IsEmpty || year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)
            || hour > 23 || minute > 59 || second > 59)
        {
            return false;
        }

        var local = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified).AddTicks(fractionTicks);
        long utcTicks = local.Ticks - TimeSpan.FromMinutes(offsetMinutes).Ticks;
        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        date = new XmpDate(new DateTimeOffset(local, TimeSpan.FromMinutes(offsetMinutes)), hasTimeZone, precision, text);
        return true;
    }

    /// <inheritdoc/>
    public bool Equals(XmpDate other) =>
        Value.Equals(other.Value) && Value.Offset == other.Value.Offset && HasTimeZone == other.HasTimeZone
        && Precision == other.Precision && string.Equals(Text, other.Text, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is XmpDate other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Value, HasTimeZone, Precision, StringComparer.Ordinal.GetHashCode(Text));

    /// <summary>Returns the text the date was read from.</summary>
    /// <returns><see cref="Text"/>.</returns>
    public override string ToString() => Text;

    private static bool Take(ref ReadOnlySpan<char> text, char expected)
    {
        if (!text.IsEmpty && text[0] == expected)
        {
            text = text[1..];
            return true;
        }

        return false;
    }

    private static bool TryNumber(ref ReadOnlySpan<char> text, int length, out int value)
    {
        value = 0;
        if (text.Length < length)
        {
            return false;
        }

        for (int index = 0; index < length; index++)
        {
            if (!char.IsAsciiDigit(text[index]))
            {
                return false;
            }

            value = (value * 10) + (text[index] - '0');
        }

        text = text[length..];
        return true;
    }

    private static bool TryFraction(ref ReadOnlySpan<char> text, out long ticks)
    {
        int length = 0;
        while (length < text.Length && char.IsAsciiDigit(text[length]))
        {
            length++;
        }

        ticks = 0;
        if (length == 0)
        {
            return false;
        }

        // Seven digits are a tick; further digits are below the resolution of DateTime.
        ReadOnlySpan<char> digits = text[..Math.Min(length, 7)];
        ticks = long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        for (int index = digits.Length; index < 7; index++)
        {
            ticks *= 10;
        }

        text = text[length..];
        return true;
    }

    private static bool TryTimeZone(ref ReadOnlySpan<char> text, out bool hasTimeZone, out int offsetMinutes)
    {
        hasTimeZone = false;
        offsetMinutes = 0;
        if (text.IsEmpty)
        {
            return true;
        }

        hasTimeZone = true;
        if (Take(ref text, 'Z'))
        {
            return true;
        }

        int sign = Take(ref text, '+') ? 1 : Take(ref text, '-') ? -1 : 0;
        if (sign == 0 || !TryNumber(ref text, 2, out int hours) || !Take(ref text, ':') || !TryNumber(ref text, 2, out int minutes)
            || hours > 14 || minutes > 59)
        {
            return false;
        }

        offsetMinutes = sign * ((hours * 60) + minutes);
        return Math.Abs(offsetMinutes) <= 14 * 60;
    }
}
