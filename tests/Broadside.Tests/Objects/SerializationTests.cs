using System.Buffers;
using System.Text;
using Broadside.Objects;
using static Broadside.Tests.Objects.ScalarParsingTests;

namespace Broadside.Tests.Objects;

/// <summary>
/// Writing COS objects back to bytes (ISO 32000-2 §7.2, §7.3) and the equality a write-then-parse round trip preserves.
/// </summary>
public class SerializationTests
{
    public static TheoryData<string, string> CanonicalSyntax => new()
    {
        { "true", "true" },
        { "false", "false" },
        { "null", "null" },
        { "+17", "17" },
        { "-98", "-98" },
        { "12 0 R", "12 0 R" },
        { "34.5", "34.5" },
        { "4.", "4.0" },
        { "-.002", "-0.002" },
        { "0.00001", "0.00001" },
        { "12345678901234567890", "12345678901234567000.0" },
        { "/Lime#20Green", "/Lime#20Green" },
        { "/The_Key_of_F#23_Minor", "/The_Key_of_F#23_Minor" },
        { "/A#42", "/AB" },
        { "/", "/" },
        { "(It has zero (0) length.)", @"(It has zero \(0\) length.)" },
        { "(a\\\\b\nc\rd\te\bf\fg)", @"(a\\b\nc\nd\te\bf\fg)" },
        { @"(\000\177\200\377)", @"(\000\177\200\377)" },
        { "<901fa>", "<901FA0>" },
        { "[549 3.14 false (Ralph) /SomeName]", "[549 3.14 false (Ralph) /SomeName]" },
        { "[ ]", "[]" },
        { "<</Type/Page/MediaBox[0 0 612 792]/Resources<<>>>>", "<< /Type /Page /MediaBox [0 0 612 792] /Resources << >> >>" },
    };

    [Theory]
    [MemberData(nameof(CanonicalSyntax))]
    public void Writes_canonical_syntax(string syntax, string expected)
    {
        CosObject parsed = CosObject.Parse(Latin1(syntax));

        Assert.Equal(expected, Write(parsed));
        Assert.Equal(expected, parsed.ToString());
    }

    public static TheoryData<string, string> NameEscapes => new()
    {
        { "paired()parentheses", "/paired#28#29parentheses" },
        { "Lime Green", "/Lime#20Green" },
        { "a/b%c<d>e[f]g{h}i#j", "/a#2Fb#25c#3Cd#3Ee#5Bf#5Dg#7Bh#7Di#23j" },
        { "Café", "/Caf#C3#A9" },
    };

    [Theory]
    [MemberData(nameof(NameEscapes))]
    public void Names_escape_delimiters_white_space_number_signs_and_non_ascii_bytes(string name, string expected)
    {
        Assert.Equal(expected, Write(new CosName(name)));
    }

    [Fact]
    public void Writes_a_stream_with_a_direct_length_equal_to_its_data()
    {
        CosObject parsed = CosObject.Parse("<< /Filter /AHx /Length 9 0 R >>\nstream\n414243>\nendstream"u8);

        Assert.Equal("<< /Filter /AHx /Length 7 >>\nstream\n414243>\nendstream", Write(parsed));
        Assert.Equal("<< /Length 0 >>\nstream\n\nendstream", Write(new CosStream(new CosDictionary(), ReadOnlyMemory<byte>.Empty)));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(-0.1)]
    [InlineData(1e-7)]
    [InlineData(1.0 / 3.0)]
    [InlineData(123456789.123456789)]
    [InlineData(1e21)]
    [InlineData(-1e300)]
    [InlineData(double.Epsilon)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    [InlineData(5.0)]
    public void Reals_round_trip_exactly_without_exponent_notation(double value)
    {
        string written = Write(new CosReal(value));

        Assert.DoesNotContain('E', written);
        Assert.Contains('.', written);
        Assert.Equal(value, Assert.IsType<CosReal>(CosObject.Parse(Encoding.ASCII.GetBytes(written))).Value);
    }

    [Fact]
    public void A_real_must_be_finite()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CosReal(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CosReal(double.PositiveInfinity));
    }

    public static TheoryData<string> RoundTripSyntax => new()
    {
        { "[549 3.14 false (Ralph) /SomeName]" },
        { "<< /A [1 2.5 << /B (x) >>] /C 3 0 R /D <00FF> /E null /F /G#20H >>" },
        { "(Strings can contain balanced parentheses ()\nand special characters ( * ! & } ^ %and so on) .)" },
        { "<< /Length 8 0 R >>\nstream\r\nline one\nline two\r\nendstream" },
        { "<< /Length 0 >>\nstream\n\nendstream" },
    };

    [Theory]
    [MemberData(nameof(RoundTripSyntax))]
    public void A_written_object_parses_back_to_an_equal_object(string syntax)
    {
        CosObject original = CosObject.Parse(Latin1(syntax));

        CosObject reparsed = CosObject.Parse(WriteBytes(original));

        Assert.True(CosObject.DeepEquals(original, reparsed), $"{original} != {reparsed}");
        Assert.Equal(Write(original), Write(reparsed));
    }

    [Fact]
    public void Every_byte_value_survives_a_round_trip_in_strings_and_names()
    {
        byte[] everyByte = [.. Enumerable.Range(0, 256).Select(static value => (byte)value)];
        byte[] everyByteButZero = everyByte[1..];
        var array = new CosArray([new CosString(everyByte), new CosString(everyByte, hexadecimal: true), new CosName(everyByteButZero)]);

        CosArray reparsed = Assert.IsType<CosArray>(CosObject.Parse(WriteBytes(array)));

        Assert.True(CosObject.DeepEquals(array, reparsed));
        Assert.Equal(everyByte, Assert.IsType<CosString>(reparsed[0]).Bytes.ToArray());
        Assert.Equal(everyByteButZero, Assert.IsType<CosName>(reparsed[2]).Bytes.ToArray());
    }

    [Fact]
    public void A_constructed_tree_round_trips()
    {
        var page = new CosDictionary
        {
            [new CosName("Type")] = new CosName("Page"),
            [new CosName("MediaBox")] = new CosArray([new CosInteger(0), new CosInteger(0), new CosReal(595.276), new CosReal(841.89)]),
            [new CosName("Parent")] = new CosReference(2, 0),
            [new CosName("Contents")] = new CosStream(new CosDictionary(), "BT ET"u8.ToArray()),
        };

        Assert.True(CosObject.DeepEquals(page, CosObject.Parse(WriteBytes(page))));
    }

    [Fact]
    public void Deep_equality_requires_the_same_kind_of_object()
    {
        Assert.False(CosObject.DeepEquals(new CosInteger(1), new CosReal(1)));
        Assert.False(CosObject.DeepEquals(new CosName("A"), new CosString("A"u8)));
        Assert.False(CosObject.DeepEquals(CosNull.Instance, null));
        Assert.True(CosObject.DeepEquals(null, null));
    }

    [Fact]
    public void Deep_equality_ignores_string_form_dictionary_order_and_stream_length()
    {
        Assert.True(CosObject.DeepEquals(CosObject.Parse("(AB)"u8), CosObject.Parse("<4142>"u8)));
        Assert.True(CosObject.DeepEquals(CosObject.Parse("<< /A 1 /B 2 >>"u8), CosObject.Parse("<< /B 2 /A 1 >>"u8)));
        Assert.True(CosObject.DeepEquals(
            CosObject.Parse("<< /Length 3 >>\nstream\nabc\nendstream"u8),
            new CosStream(new CosDictionary(), "abc"u8.ToArray())));
        Assert.False(CosObject.DeepEquals(
            CosObject.Parse("<< /Length 3 >>\nstream\nabc\nendstream"u8),
            new CosStream(new CosDictionary(), "abd"u8.ToArray())));
        Assert.False(CosObject.DeepEquals(CosObject.Parse("[1 2]"u8), CosObject.Parse("[2 1]"u8)));
    }

    [Fact]
    public void Equals_is_value_equality_for_immutable_objects_and_identity_for_containers()
    {
        Assert.Equal(new CosInteger(5), new CosInteger(5));
        Assert.Equal(new CosString("x"u8), new CosString("x"u8, hexadecimal: true));
        Assert.Equal(new CosReference(1, 0), CosObject.Parse("1 0 R"u8));
        Assert.Equal(new CosName("Type").GetHashCode(), CosObject.Parse("/Typ#65"u8).GetHashCode());
        Assert.False(new CosArray().Equals(new CosArray()));
        Assert.False(new CosDictionary().Equals(new CosDictionary()));
    }

    private static string Write(CosObject value) => Encoding.Latin1.GetString(WriteBytes(value));

    private static byte[] WriteBytes(CosObject value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        value.WriteTo(buffer);
        return buffer.WrittenSpan.ToArray();
    }
}
