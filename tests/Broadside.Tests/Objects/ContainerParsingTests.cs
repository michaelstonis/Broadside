using System.Text;
using Broadside.Objects;
using static Broadside.Tests.Objects.ScalarParsingTests;

namespace Broadside.Tests.Objects;

/// <summary>ISO 32000-2 §7.3.6 (arrays), §7.3.7 (dictionaries) and §7.3.8 (streams).</summary>
public class ContainerParsingTests
{
    [Fact]
    public void Parses_the_array_example()
    {
        CosArray array = Assert.IsType<CosArray>(CosObject.Parse("[549 3.14 false (Ralph) /SomeName]"u8));

        Assert.Collection(
            array,
            item => Assert.Equal(549, Assert.IsType<CosInteger>(item).Value),
            item => Assert.Equal(3.14, Assert.IsType<CosReal>(item).Value),
            item => Assert.Same(CosBoolean.False, item),
            item => Assert.Equal("Ralph"u8.ToArray(), Assert.IsType<CosString>(item).Bytes.ToArray()),
            item => Assert.Equal(new CosName("SomeName"), item));
    }

    [Fact]
    public void Arrays_nest_hold_references_and_keep_null_elements()
    {
        CosArray array = Assert.IsType<CosArray>(CosObject.Parse("[[] [1 [2]] 3 0 R null<<>>]"u8));

        Assert.Equal(5, array.Count);
        Assert.Empty(Assert.IsType<CosArray>(array[0]));
        Assert.Equal(2, Assert.IsType<CosArray>(array[1]).Count);
        Assert.Equal(new CosReference(3, 0), array[2]);
        Assert.Same(CosNull.Instance, array[3]);
        Assert.Empty(Assert.IsType<CosDictionary>(array[4]));
    }

    [Fact]
    public void Parses_the_dictionary_example()
    {
        CosDictionary dictionary = Assert.IsType<CosDictionary>(CosObject.Parse("""
            <</Type /Example
            /Subtype /DictionaryExample
            /Version 0.01
            /IntegerItem 12
            /StringItem (a string)
            /Subdictionary <<
            /Item1 0.4
            /Item2 true
            /LastItem (not !)
            /VeryLastItem (OK)
            >>
            >>
            """u8));

        Assert.Equal(new CosName("Example"), dictionary[new CosName("Type")]);
        Assert.Equal(new CosName("DictionaryExample"), dictionary[new CosName("Subtype")]);
        Assert.Equal(0.01, Assert.IsType<CosReal>(dictionary[new CosName("Version")]).Value);
        Assert.Equal(12, Assert.IsType<CosInteger>(dictionary[new CosName("IntegerItem")]).Value);
        Assert.Equal("a string"u8.ToArray(), Assert.IsType<CosString>(dictionary[new CosName("StringItem")]).Bytes.ToArray());
        CosDictionary subdictionary = Assert.IsType<CosDictionary>(dictionary[new CosName("Subdictionary")]);
        Assert.Equal(["Item1", "Item2", "LastItem", "VeryLastItem"], subdictionary.Keys.Select(static key => key.Value));
        Assert.Same(CosBoolean.True, subdictionary[new CosName("Item2")]);
    }

    [Fact]
    public void A_null_valued_entry_is_the_same_as_an_absent_one()
    {
        CosDictionary dictionary = Assert.IsType<CosDictionary>(CosObject.Parse("<< /A null /B 1 >>"u8));

        Assert.False(dictionary.ContainsKey(new CosName("A")));
        Assert.Single(dictionary);
        Assert.True(CosObject.DeepEquals(dictionary, CosObject.Parse("<< /B 1 >>"u8)));
    }

    [Theory]
    [InlineData("[1 2")]
    [InlineData("<< /A 1")]
    [InlineData("<< /A >>")]
    [InlineData("<< 1 2 >>")]
    [InlineData("<< /A 1 /A 2 >>")]
    [InlineData("[1 2 >>")]
    [InlineData("[1 foo]")]
    public void Malformed_containers_are_rejected(string syntax)
    {
        Assert.Throws<FormatException>(() => CosObject.Parse(Latin1(syntax)));
    }

    [Fact]
    public void Nesting_beyond_the_parser_limit_is_rejected_without_overflowing_the_stack()
    {
        byte[] deep = Encoding.ASCII.GetBytes(new string('[', 100_000) + new string(']', 100_000));

        Assert.False(CosObject.TryParse(deep, out _));
    }

    [Fact]
    public void Nesting_within_the_parser_limit_parses()
    {
        byte[] nested = Encoding.ASCII.GetBytes(new string('[', 200) + new string(']', 200));

        Assert.IsType<CosArray>(CosObject.Parse(nested));
    }

    [Theory]
    [InlineData("<< /Length 11 >>\nstream\nhello world\nendstream")]
    [InlineData("<< /Length 11 >>\r\nstream\r\nhello world\r\nendstream")]
    [InlineData("<< /Length 11 >>stream\nhello worldendstream")]
    [InlineData("<< /Length 8 0 R >>\nstream\nhello world\nendstream")]
    [InlineData("<< /Length 8 0 R >>\nstream\r\nhello world\r\nendstream")]
    public void A_stream_takes_its_data_from_its_length_or_from_endstream(string syntax)
    {
        CosStream stream = Assert.IsType<CosStream>(CosObject.Parse(Latin1(syntax)));

        Assert.Equal("hello world"u8.ToArray(), stream.EncodedData.ToArray());
        Assert.True(stream.Dictionary.ContainsKey(new CosName("Length")));
    }

    [Fact]
    public void Stream_data_is_binary_and_may_contain_the_endstream_keyword_when_length_says_so()
    {
        byte[] syntax = [.. "<< /Length 13 /Filter /FlateDecode >>\nstream\n"u8, 0x00, 0xFF, .. "endstream\r\n"u8, 0x0D, 0x0A, .. "\nendstream"u8];

        CosStream stream = Assert.IsType<CosStream>(CosObject.Parse(syntax));

        Assert.Equal(13, stream.EncodedData.Length);
        Assert.Equal(new CosName("FlateDecode"), stream.Dictionary[new CosName("Filter")]);
    }

    [Theory]
    [InlineData("<< /Length 3 >>\nstream\nhello world\nendstream")]
    [InlineData("<< >>\nstream\nhello world\nendstream")]
    [InlineData("<< /Length 11 >>\nstream\nhello world")]
    [InlineData("<< /Length 11 >>\rstream\rhello world\nendstream")]
    public void Streams_with_a_wrong_length_or_no_endstream_are_rejected(string syntax)
    {
        Assert.Throws<FormatException>(() => CosObject.Parse(Latin1(syntax)));
    }

    [Fact]
    public void A_stream_is_built_from_its_dictionary_and_data()
    {
        var dictionary = new CosDictionary { [new CosName("Filter")] = new CosName("ASCIIHexDecode") };

        var stream = new CosStream(dictionary, "48656C6C6F>"u8.ToArray());

        Assert.Same(dictionary, stream.Dictionary);
        Assert.Equal("48656C6C6F>"u8.ToArray(), stream.EncodedData.ToArray());
    }
}
