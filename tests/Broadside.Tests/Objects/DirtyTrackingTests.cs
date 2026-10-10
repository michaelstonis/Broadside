using Broadside.Objects;

namespace Broadside.Tests.Objects;

/// <summary>
/// Every COS object carries a dirty flag: false after parse, true after any mutation through the public API. The flag is what an
/// incremental update (ISO 32000-2 §7.5.6) writes from.
/// </summary>
public sealed class DirtyTrackingTests
{
    private static readonly CosName A = new("A");
    private static readonly CosName B = new("B");

    [Theory]
    [InlineData("true")]
    [InlineData("-12")]
    [InlineData("3.5")]
    [InlineData("(text)")]
    [InlineData("<00FF>")]
    [InlineData("/Name")]
    [InlineData("null")]
    [InlineData("4 0 R")]
    [InlineData("[1 [2 << /A [3] >>]]")]
    [InlineData("<< /A << /B [1 2] >> /C 5 0 R >>")]
    [InlineData("<< /Length 3 >>\nstream\nabc\nendstream")]
    public void Nothing_is_dirty_after_parse(string syntax)
    {
        CosObject parsed = CosObject.Parse(ScalarParsingTests.Latin1(syntax));

        Assert.False(parsed.IsDirty);
        Assert.All(Descendants(parsed), static item => Assert.False(item.IsDirty));
    }

    [Fact]
    public void A_newly_constructed_container_is_clean_until_changed()
    {
        Assert.False(new CosArray().IsDirty);
        Assert.False(new CosArray([CosNull.Instance]).IsDirty);
        Assert.False(new CosDictionary().IsDirty);
        Assert.False(new CosStream(new CosDictionary(), ReadOnlyMemory<byte>.Empty).IsDirty);
    }

    public static TheoryData<string, Action<CosArray>> ArrayMutations => new()
    {
        { "add", static array => array.Add(CosBoolean.True) },
        { "insert", static array => array.Insert(0, CosBoolean.True) },
        { "set", static array => array[0] = new CosInteger(9) },
        { "remove at", static array => array.RemoveAt(0) },
        { "remove", static array => array.Remove(new CosInteger(1)) },
        { "clear", static array => array.Clear() },
    };

    [Theory]
    [MemberData(nameof(ArrayMutations))]
    public void Any_array_mutation_marks_it_dirty(string mutation, Action<CosArray> mutate)
    {
        _ = mutation;
        CosArray array = Assert.IsType<CosArray>(CosObject.Parse("[1 2]"u8));

        mutate(array);

        Assert.True(array.IsDirty);
    }

    public static TheoryData<string, Action<CosDictionary>> DictionaryMutations => new()
    {
        { "add", static dictionary => dictionary.Add(B, CosBoolean.True) },
        { "set", static dictionary => dictionary[A] = new CosInteger(9) },
        { "set null", static dictionary => dictionary[A] = CosNull.Instance },
        { "remove", static dictionary => dictionary.Remove(A) },
        { "clear", static dictionary => dictionary.Clear() },
        { "remove pair", static dictionary => ((ICollection<KeyValuePair<CosName, CosObject>>)dictionary).Remove(new(A, new CosInteger(1))) },
    };

    [Theory]
    [MemberData(nameof(DictionaryMutations))]
    public void Any_dictionary_mutation_marks_it_dirty(string mutation, Action<CosDictionary> mutate)
    {
        _ = mutation;
        CosDictionary dictionary = Assert.IsType<CosDictionary>(CosObject.Parse("<< /A 1 >>"u8));

        mutate(dictionary);

        Assert.True(dictionary.IsDirty);
    }

    [Fact]
    public void Operations_that_change_nothing_leave_the_object_clean()
    {
        CosArray array = Assert.IsType<CosArray>(CosObject.Parse("[1]"u8));
        CosDictionary dictionary = Assert.IsType<CosDictionary>(CosObject.Parse("<< /A 1 >>"u8));

        Assert.False(array.Remove(new CosInteger(7)));
        Assert.False(dictionary.Remove(B));
        dictionary[B] = CosNull.Instance;

        Assert.False(array.IsDirty);
        Assert.False(dictionary.IsDirty);
    }

    [Fact]
    public void Replacing_stream_data_or_changing_its_dictionary_marks_the_stream_dirty()
    {
        CosStream replaced = Assert.IsType<CosStream>(CosObject.Parse("<< /Length 3 >>\nstream\nabc\nendstream"u8));
        CosStream edited = Assert.IsType<CosStream>(CosObject.Parse("<< /Length 3 >>\nstream\nabc\nendstream"u8));

        replaced.EncodedData = "xyz"u8.ToArray();
        edited.Dictionary[new CosName("Filter")] = new CosName("FlateDecode");

        Assert.True(replaced.IsDirty);
        Assert.True(edited.IsDirty);
    }

    [Fact]
    public void A_change_to_a_nested_direct_object_makes_every_container_above_it_dirty()
    {
        CosDictionary root = Assert.IsType<CosDictionary>(CosObject.Parse("<< /A [1 << /B [2] >>] /C 3 >>"u8));
        CosArray outer = Assert.IsType<CosArray>(root[A]);
        CosDictionary middle = Assert.IsType<CosDictionary>(outer[1]);
        CosArray inner = Assert.IsType<CosArray>(middle[B]);

        inner.Add(new CosInteger(4));

        Assert.True(inner.IsDirty);
        Assert.True(middle.IsDirty);
        Assert.True(outer.IsDirty);
        Assert.True(root.IsDirty);
    }

    private static IEnumerable<CosObject> Descendants(CosObject value) => value switch
    {
        CosArray array => array.SelectMany(static item => Descendants(item).Prepend(item)),
        CosDictionary dictionary => dictionary.Values.SelectMany(static item => Descendants(item).Prepend(item)),
        CosStream stream => Descendants(stream.Dictionary).Prepend(stream.Dictionary),
        _ => [],
    };
}
