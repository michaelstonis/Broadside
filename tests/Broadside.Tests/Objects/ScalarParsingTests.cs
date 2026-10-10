using Broadside.Objects;

namespace Broadside.Tests.Objects;

/// <summary>ISO 32000-2 §7.3.2, §7.3.3, §7.3.9 and §7.3.10: the scalar objects, parsed through <see cref="CosObject.Parse"/>.</summary>
public sealed class ScalarParsingTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Parses_booleans(string syntax, bool expected)
    {
        CosObject parsed = CosObject.Parse(Latin1(syntax));

        Assert.Equal(expected, Assert.IsType<CosBoolean>(parsed).Value);
    }

    [Theory]
    [InlineData("123", 123L)]
    [InlineData("43445", 43445L)]
    [InlineData("+17", 17L)]
    [InlineData("-98", -98L)]
    [InlineData("0", 0L)]
    [InlineData("-0", 0L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    public void Parses_integers_from_the_spec_examples(string syntax, long expected)
    {
        CosObject parsed = CosObject.Parse(Latin1(syntax));

        Assert.Equal(expected, Assert.IsType<CosInteger>(parsed).Value);
    }

    [Theory]
    [InlineData("34.5", 34.5)]
    [InlineData("-3.62", -3.62)]
    [InlineData("+123.6", 123.6)]
    [InlineData("4.", 4.0)]
    [InlineData("-.002", -0.002)]
    [InlineData("0.0", 0.0)]
    [InlineData(".5", 0.5)]
    public void Parses_reals_from_the_spec_examples(string syntax, double expected)
    {
        CosObject parsed = CosObject.Parse(Latin1(syntax));

        Assert.Equal(expected, Assert.IsType<CosReal>(parsed).Value);
    }

    [Fact]
    public void Integer_too_large_for_64_bits_is_read_as_a_real()
    {
        CosObject parsed = CosObject.Parse("12345678901234567890"u8);

        Assert.Equal(12345678901234567890d, Assert.IsType<CosReal>(parsed).Value);
    }

    [Fact]
    public void Integers_and_reals_are_both_numbers()
    {
        var integer = (CosNumber)CosObject.Parse("7"u8);
        var real = (CosNumber)CosObject.Parse("7.25"u8);

        Assert.Equal(7d, integer.ToDouble());
        Assert.Equal(7.25d, real.ToDouble());
    }

    [Fact]
    public void Parses_null_as_the_single_null_object()
    {
        CosObject parsed = CosObject.Parse("null"u8);

        Assert.Same(CosNull.Instance, parsed);
    }

    [Theory]
    [InlineData("12 0 R", 12, 0)]
    [InlineData("1 65535 R", 1, 65535)]
    [InlineData("  7\r\n3%comment\nR  ", 7, 3)]
    public void Parses_indirect_references(string syntax, int objectNumber, int generation)
    {
        CosObject parsed = CosObject.Parse(Latin1(syntax));

        CosReference reference = Assert.IsType<CosReference>(parsed);
        Assert.Equal(objectNumber, reference.ObjectNumber);
        Assert.Equal(generation, reference.Generation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   % only a comment")]
    [InlineData("1 2")]
    [InlineData("12 0 obj")]
    [InlineData("0 0 R")]
    [InlineData("1 -1 R")]
    [InlineData("1e5")]
    [InlineData("1.2.3")]
    [InlineData("--5")]
    [InlineData("nul")]
    [InlineData(")")]
    [InlineData(">")]
    [InlineData("]")]
    [InlineData(">>")]
    public void Rejects_input_that_is_not_exactly_one_object(string syntax)
    {
        Assert.Throws<FormatException>(() => CosObject.Parse(Latin1(syntax)));
        Assert.False(CosObject.TryParse(Latin1(syntax), out CosObject? result));
        Assert.Null(result);
    }

    [Fact]
    public void Whitespace_and_comments_around_an_object_are_ignored()
    {
        CosObject parsed = CosObject.Parse("\0\t\n\f\r %a comment\r\n  true  % trailing\n"u8);

        Assert.Same(CosBoolean.True, parsed);
    }

    internal static byte[] Latin1(string text) => System.Text.Encoding.Latin1.GetBytes(text);
}
