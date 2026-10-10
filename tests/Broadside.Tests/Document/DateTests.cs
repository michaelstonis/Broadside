namespace Broadside.Tests.Document;

/// <summary>PDF dates (ISO 32000-2 §7.9.4) and XMP dates (ISO 16684-1 §8.2.1.2), parsed without ever throwing.</summary>
public sealed class DateTests
{
    public static TheoryData<string, string, PdfDatePrecision, int?> WellFormedDates => new()
    {
        // The example of §7.9.4, and its legacy form with the terminating apostrophe (NOTE 2).
        { "D:199812231952-08'00", "1998-12-23T19:52:00-08:00", PdfDatePrecision.Minute, -480 },
        { "D:19981223195200-08'00'", "1998-12-23T19:52:00-08:00", PdfDatePrecision.Second, -480 },
        { "D:20140314124211+01'00", "2014-03-14T12:42:11+01:00", PdfDatePrecision.Second, 60 },
        { "D:20140924212303Z", "2014-09-24T21:23:03+00:00", PdfDatePrecision.Second, 0 },
        { "D:20140924212303Z00'00'", "2014-09-24T21:23:03+00:00", PdfDatePrecision.Second, 0 },
        { "D:20140924212303+05", "2014-09-24T21:23:03+05:00", PdfDatePrecision.Second, 300 },
        { "D:20140924212303+05'", "2014-09-24T21:23:03+05:00", PdfDatePrecision.Second, 300 },
        { "D:2014", "2014-01-01T00:00:00+00:00", PdfDatePrecision.Year, null },
        { "D:201409", "2014-09-01T00:00:00+00:00", PdfDatePrecision.Month, null },
        { "D:20140924", "2014-09-24T00:00:00+00:00", PdfDatePrecision.Day, null },
        { "D:2014092421", "2014-09-24T21:00:00+00:00", PdfDatePrecision.Hour, null },
    };

    [Theory]
    [MemberData(nameof(WellFormedDates))]
    public void A_date_reads_every_field_its_string_has_and_defaults_the_rest(string text, string expected, PdfDatePrecision precision, int? offset)
    {
        Assert.True(PdfDate.TryParse(text, out PdfDate date));

        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), date.Value);
        Assert.Equal(expected, date.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(precision, date.Precision);
        Assert.Equal(offset, date.UtcOffsetMinutes);
        Assert.Equal(text, date.Text);
        Assert.Equal(text, date.ToString());
    }

    [Fact]
    public void An_offset_beyond_what_DateTimeOffset_holds_keeps_the_instant_in_universal_time()
    {
        // §7.9.4 allows offset hours up to 23; DateTimeOffset holds at most 14.
        Assert.True(PdfDate.TryParse("D:20000101120000+23'30", out PdfDate date));

        Assert.Equal(new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero).AddMinutes(-(23 * 60) - 30), date.Value);
        Assert.Equal(TimeSpan.Zero, date.Value.Offset);
        Assert.Equal((23 * 60) + 30, date.UtcOffsetMinutes);
    }

    [Theory]
    [InlineData("20140924212303Z", "2014-09-24T21:23:03+00:00")]
    [InlineData("D:20140924212303+0100", "2014-09-24T21:23:03+01:00")]
    [InlineData("D:20140931", "2014-09-30T00:00:00+00:00")]
    [InlineData("D:20141324", "2014-12-24T00:00:00+00:00")]
    [InlineData("D:20140924256199", "2014-09-24T23:59:59+00:00")]
    [InlineData("D:20140924212303 ", "2014-09-24T21:23:03+00:00")]
    public void A_date_that_deviates_but_can_be_read_is_read_with_its_fields_clamped(string text, string expected)
    {
        Assert.True(PdfDate.TryParse(text, out PdfDate date));

        Assert.Equal(expected, date.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("")]
    [InlineData("D:")]
    [InlineData("D:201")]
    [InlineData("D:191001223195200-08'00'")]
    [InlineData("Monday, March 3, 1997")]
    [InlineData("D:2014-09-24")]
    [InlineData("D:20140924212303X")]
    [InlineData("D:20140924212303+1")]
    [InlineData(null)]
    public void A_string_that_is_not_a_date_does_not_parse(string? text)
    {
        Assert.False(PdfDate.TryParse(text, out PdfDate date));
        Assert.Equal(default, date);
    }

    [Fact]
    public void Dates_compare_by_value_and_text()
    {
        Assert.True(PdfDate.TryParse("D:2014", out PdfDate first));
        Assert.True(PdfDate.TryParse("D:2014", out PdfDate second));
        Assert.True(PdfDate.TryParse("D:201401", out PdfDate third));

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.NotEqual(first, third);
        Assert.True(first != third);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    public static TheoryData<string, string, PdfDatePrecision, bool> XmpDates => new()
    {
        { "2014", "2014-01-01T00:00:00+00:00", PdfDatePrecision.Year, false },
        { "2014-09", "2014-09-01T00:00:00+00:00", PdfDatePrecision.Month, false },
        { "2014-09-24", "2014-09-24T00:00:00+00:00", PdfDatePrecision.Day, false },
        { "2014-09-24T21:23+02:00", "2014-09-24T21:23:00+02:00", PdfDatePrecision.Minute, true },
        { "2014-09-24T21:23:03Z", "2014-09-24T21:23:03+00:00", PdfDatePrecision.Second, true },
        { "2014-09-24T21:23:03.25-05:30", "2014-09-24T21:23:03+00:00", PdfDatePrecision.FractionOfSecond, true },
        { "2014-09-24T21:23:03", "2014-09-24T21:23:03+00:00", PdfDatePrecision.Second, false },
    };

    [Theory]
    [MemberData(nameof(XmpDates))]
    public void An_xmp_date_reads_the_iso_8601_subset_and_knows_whether_it_has_a_time_zone(string text, string expectedLocal, PdfDatePrecision precision, bool hasTimeZone)
    {
        Assert.True(XmpDate.TryParse(text, out XmpDate date));

        Assert.Equal(precision, date.Precision);
        Assert.Equal(hasTimeZone, date.HasTimeZone);
        Assert.Equal(text, date.Text);
        if (precision != PdfDatePrecision.FractionOfSecond)
        {
            Assert.Equal(expectedLocal, date.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            Assert.Equal(new DateTimeOffset(2014, 9, 24, 21, 23, 3, 250, TimeSpan.FromMinutes(-330)), date.Value);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("14")]
    [InlineData("2014-13")]
    [InlineData("2014-02-30")]
    [InlineData("2014-09-24T21")]
    [InlineData("2014-09-24T25:00Z")]
    [InlineData("2014-09-24 21:23Z")]
    [InlineData("D:20140924")]
    public void A_string_that_is_not_an_xmp_date_does_not_parse(string text)
    {
        Assert.False(XmpDate.TryParse(text, out _));
    }
}
