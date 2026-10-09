namespace Broadside;

/// <summary>A date as a PDF file writes it, <c>D:YYYYMMDDHHmmSSOHH'mm</c>: the instant it denotes and the text it was read from.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.9.4. A date is a text string (decode it with <see cref="Objects.CosString.DecodeText"/> first). The prefix <c>D:</c>
/// and the year are required; every other field is optional, but only when all the fields before it are present, and defaults to 01
/// (month and day) or zero. <c>O</c> is the relation of local time to Universal Time: <c>+</c>, <c>-</c> or <c>Z</c>, followed by
/// the offset hours (00-23), an apostrophe and the offset minutes. Without it the time is GMT. The apostrophe that PDF 1.7 and
/// earlier put after the offset minutes is accepted (NOTE 2), as are hours and minutes after <c>Z</c> (NOTE 3).
/// </para>
/// <para>
/// <see cref="TryParse"/> never throws. It also reads the forms real files use that deviate from the clause, with each deviating field
/// clamped into range: no <c>D:</c> prefix, an offset without the apostrophe (<c>+0100</c>), a month, day, hour, minute or second out
/// of range (day 31 of a 30-day month reads as day 30), trailing white-space. Anything else does not parse, and a document keeps the
/// text so nothing is lost: free text such as <c>Monday, March 3, 1997</c>, the year-2000 bug <c>D:19100...</c> (a field with too
/// many digits), separators, a one-digit offset.
/// </para>
/// <para>
/// <see cref="Value"/> carries the offset the string gives. <see cref="DateTimeOffset"/> holds offsets up to ±14 hours and PDF allows
/// up to ±23:59; for a larger offset <see cref="Value"/> is the same instant in Universal Time and <see cref="UtcOffsetMinutes"/> keeps
/// the offset as written.
/// </para>
/// </remarks>
public readonly struct PdfDate : IEquatable<PdfDate>
{
    private static readonly long MaxOffsetTicks = TimeSpan.FromHours(14).Ticks;

    private PdfDate(DateTimeOffset value, int? utcOffsetMinutes, PdfDatePrecision precision, string text)
    {
        Value = value;
        UtcOffsetMinutes = utcOffsetMinutes;
        Precision = precision;
        Text = text;
    }

    /// <summary>Gets the instant the date denotes, with the offset the string gives when <see cref="DateTimeOffset"/> can hold it.</summary>
    public DateTimeOffset Value { get; }

    /// <summary>
    /// Gets the offset of local time from Universal Time in minutes as the string writes it (negative west of Greenwich), or
    /// <see langword="null"/> when the string has no <c>O</c> field and the time is read as GMT.
    /// </summary>
    public int? UtcOffsetMinutes { get; }

    /// <summary>Gets the last field the string gives.</summary>
    public PdfDatePrecision Precision { get; }

    /// <summary>Gets the text the date was read from.</summary>
    public string Text => field ?? string.Empty;

    /// <summary>Returns whether two dates are equal: the same instant, offset, precision and text.</summary>
    /// <param name="left">The first date.</param>
    /// <param name="right">The second date.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public static bool operator ==(PdfDate left, PdfDate right) => left.Equals(right);

    /// <summary>Returns whether two dates differ.</summary>
    /// <param name="left">The first date.</param>
    /// <param name="right">The second date.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(PdfDate left, PdfDate right) => !left.Equals(right);

    /// <summary>Parses a PDF date; never throws.</summary>
    /// <param name="text">The date as decoded text, such as <c>D:199812231952-08'00</c>.</param>
    /// <param name="date">The date, or <see langword="default"/> when <paramref name="text"/> is not one.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> is a date, including the deviating forms the remarks list.</returns>
    /// <remarks>ISO 32000-2 §7.9.4.</remarks>
    public static bool TryParse(string? text, out PdfDate date) => Parse(text, out date) != DateParseOutcome.Unreadable;

    /// <inheritdoc/>
    public bool Equals(PdfDate other) =>
        Value.Equals(other.Value) && Value.Offset == other.Value.Offset && UtcOffsetMinutes == other.UtcOffsetMinutes
        && Precision == other.Precision && string.Equals(Text, other.Text, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PdfDate other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Value, UtcOffsetMinutes, Precision, StringComparer.Ordinal.GetHashCode(Text));

    /// <summary>Returns the text the date was read from.</summary>
    /// <returns><see cref="Text"/>.</returns>
    public override string ToString() => Text;

    /// <summary>Parses a PDF date and says whether it conforms to §7.9.4, deviates but was read, or could not be read.</summary>
    internal static DateParseOutcome Parse(string? text, out PdfDate date)
    {
        date = default;
        if (text is null)
        {
            return DateParseOutcome.Unreadable;
        }

        bool repaired = false;
        ReadOnlySpan<char> rest = text.AsSpan();
        int trimmed = rest.TrimEnd().Length;
        if (trimmed != rest.Length)
        {
            repaired = true;
            rest = rest[..trimmed];
        }

        if (rest.StartsWith("D:", StringComparison.Ordinal))
        {
            rest = rest[2..];
        }
        else
        {
            repaired = true;
        }

        // The fields before O are a run of 4 to 14 digits, two per field after the year.
        int digits = 0;
        while (digits < rest.Length && char.IsAsciiDigit(rest[digits]))
        {
            digits++;
        }

        if (digits is < 4 or > 14 || digits % 2 != 0)
        {
            return DateParseOutcome.Unreadable;
        }

        ReadOnlySpan<char> fields = rest[..digits];
        rest = rest[digits..];
        var precision = (PdfDatePrecision)((digits - 4) / 2);
        int year = Number(fields[..4]);
        int month = digits >= 6 ? Number(fields.Slice(4, 2)) : 1;
        int day = digits >= 8 ? Number(fields.Slice(6, 2)) : 1;
        int hour = digits >= 10 ? Number(fields.Slice(8, 2)) : 0;
        int minute = digits >= 12 ? Number(fields.Slice(10, 2)) : 0;
        int second = digits >= 14 ? Number(fields.Slice(12, 2)) : 0;
        repaired |= Clamp(ref year, 1, 9999) | Clamp(ref month, 1, 12) | Clamp(ref day, 1, DateTime.DaysInMonth(year, month))
            | Clamp(ref hour, 0, 23) | Clamp(ref minute, 0, 59) | Clamp(ref second, 0, 59);

        int? offsetMinutes = null;
        if (!rest.IsEmpty)
        {
            if (!TryParseOffset(rest, ref repaired, out int minutes))
            {
                return DateParseOutcome.Unreadable;
            }

            offsetMinutes = minutes;
        }

        var local = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        long offsetTicks = TimeSpan.FromMinutes(offsetMinutes ?? 0).Ticks;
        long utcTicks = local.Ticks - offsetTicks;
        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
        {
            // The offset would move the instant outside the representable range (year 1 or 9999): read it as GMT.
            offsetTicks = 0;
            utcTicks = local.Ticks;
            repaired = true;
        }

        DateTimeOffset value = Math.Abs(offsetTicks) <= MaxOffsetTicks
            ? new DateTimeOffset(local, TimeSpan.FromTicks(offsetTicks))
            : new DateTimeOffset(utcTicks, TimeSpan.Zero);
        date = new PdfDate(value, offsetMinutes, precision, text);
        return repaired ? DateParseOutcome.Repaired : DateParseOutcome.Valid;
    }

    /// <summary>Reads <c>O</c>, <c>HH</c>, the apostrophe, <c>mm</c> and the legacy apostrophe.</summary>
    private static bool TryParseOffset(ReadOnlySpan<char> text, ref bool repaired, out int minutes)
    {
        minutes = 0;
        int sign = text[0] switch
        {
            '+' => 1,
            '-' => -1,
            'Z' => 0,
            _ => 2,
        };
        if (sign == 2)
        {
            return false;
        }

        text = text[1..];
        if (text.IsEmpty)
        {
            return sign == 0;
        }

        if (text.Length < 2 || !char.IsAsciiDigit(text[0]) || !char.IsAsciiDigit(text[1]))
        {
            return false;
        }

        int hours = Number(text[..2]);
        text = text[2..];
        int offsetMinutes = 0;
        if (!text.IsEmpty && text[0] == '\'')
        {
            text = text[1..];
        }
        else if (!text.IsEmpty)
        {
            repaired = true; // +0100: the apostrophe after the hours is missing.
        }

        if (!text.IsEmpty)
        {
            if (text.Length < 2 || !char.IsAsciiDigit(text[0]) || !char.IsAsciiDigit(text[1]))
            {
                return false;
            }

            offsetMinutes = Number(text[..2]);
            text = text[2..];
            if (!text.IsEmpty && text[0] == '\'')
            {
                text = text[1..]; // §7.9.4 NOTE 2: the terminating apostrophe of PDF 1.7 and earlier.
            }
        }

        if (!text.IsEmpty)
        {
            return false;
        }

        repaired |= Clamp(ref hours, 0, 23) | Clamp(ref offsetMinutes, 0, 59);
        minutes = sign * ((hours * 60) + offsetMinutes);
        return true;
    }

    private static int Number(ReadOnlySpan<char> digits)
    {
        int value = 0;
        foreach (char digit in digits)
        {
            value = (value * 10) + (digit - '0');
        }

        return value;
    }

    private static bool Clamp(ref int value, int minimum, int maximum)
    {
        int clamped = Math.Clamp(value, minimum, maximum);
        bool changed = clamped != value;
        value = clamped;
        return changed;
    }
}

/// <summary>What parsing a date found.</summary>
internal enum DateParseOutcome : byte
{
    /// <summary>The text conforms to the date format.</summary>
    Valid,

    /// <summary>The text deviates from the format but was read.</summary>
    Repaired,

    /// <summary>The text is not a date.</summary>
    Unreadable,
}
