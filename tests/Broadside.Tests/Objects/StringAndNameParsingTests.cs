using Broadside.Objects;
using static Broadside.Tests.Objects.ScalarParsingTests;

namespace Broadside.Tests.Objects;

/// <summary>ISO 32000-2 §7.3.4 (strings) and §7.3.5 (names), with the expected values taken from the clauses' own examples.</summary>
public class StringAndNameParsingTests
{
    [Theory]
    [InlineData("(This is a string)", "This is a string")]
    [InlineData("(Strings can contain newlines\nand such.)", "Strings can contain newlines\nand such.")]
    [InlineData("(Strings can contain balanced parentheses ()\nand special characters ( * ! & } ^ %and so on) .)", "Strings can contain balanced parentheses ()\nand special characters ( * ! & } ^ %and so on) .")]
    [InlineData("()", "")]
    [InlineData("(It has zero (0) length.)", "It has zero (0) length.")]
    public void Literal_strings_from_example_1_keep_balanced_parentheses(string syntax, string expected)
    {
        Assert.Equal(Latin1(expected), StringBytes(syntax));
    }

    [Theory]
    [InlineData(@"(\n)", "\n")]
    [InlineData(@"(\r)", "\r")]
    [InlineData(@"(\t)", "\t")]
    [InlineData(@"(\b)", "\b")]
    [InlineData(@"(\f)", "\f")]
    [InlineData(@"(\()", "(")]
    [InlineData(@"(\))", ")")]
    [InlineData(@"(\\)", "\\")]
    [InlineData(@"(\q)", "q")]
    [InlineData(@"(unbalanced \( only)", "unbalanced ( only")]
    public void Literal_strings_decode_every_escape_in_table_3(string syntax, string expected)
    {
        Assert.Equal(Latin1(expected), StringBytes(syntax));
    }

    [Theory]
    [InlineData("(These \\\ntwo strings \\\nare the same.)")]
    [InlineData("(These \\\r\ntwo strings \\\rare the same.)")]
    [InlineData("(These two strings are the same.)")]
    public void A_backslash_before_an_end_of_line_continues_the_string(string syntax)
    {
        Assert.Equal("These two strings are the same."u8.ToArray(), StringBytes(syntax));
    }

    [Theory]
    [InlineData("(This string has an end-of-line at the end of it.\n)")]
    [InlineData("(This string has an end-of-line at the end of it.\r)")]
    [InlineData("(This string has an end-of-line at the end of it.\r\n)")]
    [InlineData(@"(This string has an end-of-line at the end of it.\n)")]
    public void An_unescaped_end_of_line_is_read_as_a_line_feed(string syntax)
    {
        Assert.Equal("This string has an end-of-line at the end of it.\n"u8.ToArray(), StringBytes(syntax));
    }

    [Theory]
    [InlineData(@"(This string contains \245two octal characters\307.)", "This string contains ¥two octal charactersÇ.")]
    [InlineData(@"(\0053)", "\u0005" + "3")]
    [InlineData(@"(\053)", "+")]
    [InlineData(@"(\53)", "+")]
    [InlineData(@"(\5)", "\u0005")]
    [InlineData(@"(\777)", "ÿ")]
    [InlineData(@"(\1234)", "S4")]
    public void Octal_escapes_take_one_to_three_digits_and_ignore_overflow(string syntax, string expected)
    {
        Assert.Equal(Latin1(expected), StringBytes(syntax));
    }

    [Fact]
    public void Literal_strings_may_hold_any_byte()
    {
        byte[] syntax = [(byte)'(', 0x00, 0x80, 0xFF, (byte)'%', (byte)')'];

        Assert.Equal(new byte[] { 0x00, 0x80, 0xFF, (byte)'%' }, Assert.IsType<CosString>(CosObject.Parse(syntax)).Bytes.ToArray());
    }

    [Theory]
    [InlineData("<4E6F762073686D6F7A206B6120706F702E>", "Nov shmoz ka pop.")]
    [InlineData("<901FA3>", "\u0090\u001F£")]
    [InlineData("<901FA>", "\u0090\u001F ")]
    [InlineData("<>", "")]
    [InlineData("< 4e 6F\r\n76\t>", "Nov")]
    public void Hexadecimal_strings_ignore_white_space_and_pad_an_odd_digit_with_zero(string syntax, string expected)
    {
        CosString parsed = Assert.IsType<CosString>(CosObject.Parse(Latin1(syntax)));

        Assert.Equal(Latin1(expected), parsed.Bytes.ToArray());
        Assert.True(parsed.IsHexadecimal);
    }

    [Theory]
    [InlineData("(unterminated")]
    [InlineData("(unterminated \\)")]
    [InlineData("<4E6F")]
    [InlineData("<4G>")]
    public void Malformed_strings_are_rejected(string syntax)
    {
        Assert.Throws<FormatException>(() => CosObject.Parse(Latin1(syntax)));
    }

    [Theory]
    [InlineData("/Name1", "Name1")]
    [InlineData("/ASomewhatLongerName", "ASomewhatLongerName")]
    [InlineData("/A;Name_With-Various***Characters?", "A;Name_With-Various***Characters?")]
    [InlineData("/1.2", "1.2")]
    [InlineData("/$$", "$$")]
    [InlineData("/@pattern", "@pattern")]
    [InlineData("/.notdef", ".notdef")]
    [InlineData("/Lime#20Green", "Lime Green")]
    [InlineData("/paired#28#29parentheses", "paired()parentheses")]
    [InlineData("/The_Key_of_F#23_Minor", "The_Key_of_F#_Minor")]
    [InlineData("/A#42", "AB")]
    [InlineData("/", "")]
    public void Names_from_table_4_expand_number_sign_escapes(string syntax, string expected)
    {
        CosName parsed = Assert.IsType<CosName>(CosObject.Parse(Latin1(syntax)));

        Assert.Equal(Latin1(expected), parsed.Bytes.ToArray());
        Assert.Equal(new CosName(expected), parsed);
    }

    [Fact]
    public void Names_are_equal_when_their_expanded_bytes_are_equal()
    {
        Assert.Equal(CosObject.Parse("/A#42"u8), CosObject.Parse("/AB"u8));
        Assert.NotEqual(CosObject.Parse("/ab"u8), CosObject.Parse("/AB"u8));
    }

    [Fact]
    public void Name_value_is_the_utf8_reading_of_its_bytes()
    {
        CosName parsed = Assert.IsType<CosName>(CosObject.Parse("/Caf#C3#A9"u8));

        Assert.Equal("Café", parsed.Value);
    }

    [Theory]
    [InlineData("/Bad#2")]
    [InlineData("/Bad#ZZ")]
    [InlineData("/Null#00")]
    public void Malformed_names_are_rejected(string syntax)
    {
        Assert.Throws<FormatException>(() => CosObject.Parse(Latin1(syntax)));
    }

    [Fact]
    public void A_name_cannot_hold_the_byte_zero()
    {
        Assert.Throws<ArgumentException>(() => new CosName("A\0B"));
        Assert.Throws<ArgumentException>(() => new CosName([0x41, 0x00]));
    }

    private static byte[] StringBytes(string syntax) =>
        Assert.IsType<CosString>(CosObject.Parse(Latin1(syntax))).Bytes.ToArray();
}
