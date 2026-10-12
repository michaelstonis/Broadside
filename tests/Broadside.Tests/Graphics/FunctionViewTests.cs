using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Graphics.Functions;
using Broadside.Objects;
using static Broadside.Tests.Graphics.FunctionTesting;

namespace Broadside.Tests.Graphics;

/// <summary>
/// What every function shares (ISO 32000-2 §7.10.1, Table 38) and how <see cref="PdfFunction"/> behaves as a live view
/// (ADR 0004): one view per object, recompiled after a change, batch evaluation, and the forms consumers accept.
/// </summary>
public sealed class FunctionViewTests
{
    [Fact]
    public void The_same_object_gives_the_same_view()
    {
        using PdfDocument document = PdfDocument.Create();
        CosObject dictionary = Cos("<< /FunctionType 2 /Domain [0 1] /N 1 >>");

        Assert.Same(document.GetFunction(dictionary), document.GetFunction(dictionary));
        Assert.NotSame(document.GetFunction(dictionary), document.GetFunction(Cos("<< /FunctionType 2 /Domain [0 1] /N 1 >>")));
    }

    [Fact]
    public void Entries_are_read_live_and_evaluation_follows_a_change_to_the_dictionary()
    {
        using PdfDocument document = PdfDocument.Create();
        var dictionary = (CosDictionary)Cos("<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >>");
        PdfFunction function = document.Function(dictionary);
        Near([0.5f], function.At(0.5f));

        dictionary[new CosName("N")] = new CosInteger(2);
        ((CosArray)dictionary[new CosName("C1")])[0] = new CosInteger(3);

        Assert.Equal(2.0, ((PdfExponentialFunction)function).Exponent);
        Assert.Equal([3.0], ((PdfExponentialFunction)function).C1);
        Near([0.75f], function.At(0.5f));
    }

    [Fact]
    public void A_change_to_a_sampled_function_data_is_seen()
    {
        using PdfDocument document = PdfDocument.Create();
        CosStream stream = Stream("<< /FunctionType 0 /Domain [0 1] /Range [0 255] /Size [2] /BitsPerSample 8 >>", [0, 100]);
        PdfFunction function = document.Function(stream);
        Near([50f], function.At(0.5f));

        stream.EncodedData = new byte[] { 0, 200 };

        Near([100f], function.At(0.5f));
    }

    [Fact]
    public void Changing_the_function_type_gives_a_new_view_and_invalidates_the_old_one()
    {
        using PdfDocument document = PdfDocument.Create();
        var dictionary = (CosDictionary)Cos("<< /FunctionType 2 /Domain [0 1] /N 1 /Functions [<< /FunctionType 2 /Domain [0 1] /N 2 >>] /Bounds [] /Encode [0 1] >>");
        PdfFunction exponential = document.Function(dictionary);

        dictionary[new CosName("FunctionType")] = new CosInteger(3);
        PdfFunction stitching = document.Function(dictionary);

        Assert.IsType<PdfStitchingFunction>(stitching);
        Near([0.25f], stitching.At(0.5f));
        Assert.False(exponential.IsValid);
    }

    [Fact]
    public void A_batch_evaluates_points_one_after_the_other()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Calculator("{ dup 2 mul exch 3 mul }", 1, 2));
        float[] inputs = [1f, 2f, 3f];
        float[] outputs = new float[6];

        function.Evaluate(inputs, outputs, 3);

        Assert.Equal([2f, 3f, 4f, 6f, 6f, 9f], outputs);
        Assert.Throws<ArgumentException>(() => function.Evaluate(inputs, new float[5], 3));
        Assert.Throws<ArgumentException>(() => function.Evaluate(inputs, outputs, 4));
        Assert.Throws<ArgumentException>(() => function.Evaluate(ReadOnlySpan<float>.Empty, new float[2]));
        Assert.Throws<ArgumentException>(() => function.Evaluate([1f], new float[1]));
    }

    public static TheoryData<string> NotFunctions => new()
    {
        "<< /FunctionType 1 /Domain [0 1] >>",
        "<< /Domain [0 1] /N 1 >>",
        "[0 1]",
        "/Identity",
    };

    [Theory]
    [MemberData(nameof(NotFunctions))]
    public void An_object_that_is_not_a_function_gives_no_view(string syntax)
    {
        using PdfDocument document = PdfDocument.Create();

        Assert.Null(document.GetFunction(Cos(syntax)));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("FunctionInvalid", diagnostic.Code);
    }

    [Fact]
    public void Odd_and_reversed_domain_entries_are_repaired()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Cos("<< /FunctionType 2 /Domain [1 0 5] /N 1 >>"));

        Near([1f], function.At(3f));
        Near([0f], function.At(-3f));

        // Two repairs of one kind on one (direct) object: the sink keeps one diagnostic per code and object.
        Assert.Equal(["FunctionEntryInvalid"], document.Codes());
    }

    [Fact]
    public void Consumers_get_identity_and_arrays_of_one_output_functions_where_their_entry_allows_them()
    {
        using PdfDocument document = PdfDocument.Create();
        CosObject identity = Cos("/Identity");
        CosObject array = Cos("[<< /FunctionType 2 /Domain [0 1] /N 1 >> << /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>]");
        Span<float> output = stackalloc float[2];

        FunctionEvaluator? transfer = document.Functions.GetEvaluator(identity, FunctionForms.Identity, 1, 1);
        Assert.NotNull(transfer);
        Assert.Equal(FunctionStatus.Ok, transfer.Evaluate([0.3f], output));
        Assert.Equal(0.3f, output[0]);

        FunctionEvaluator? shading = document.Functions.GetEvaluator(array, FunctionForms.Array, 1, 2);
        Assert.NotNull(shading);
        Assert.Equal(2, shading.OutputCount);
        Assert.Equal(FunctionStatus.Ok, shading.Evaluate([0.25f], output));
        Assert.Equal([0.25f, 0.75f], output.ToArray());
        Assert.Empty(document.Diagnostics);

        Assert.Null(document.Functions.GetEvaluator(identity, FunctionForms.Single, 1, 4));
        Assert.Null(document.Functions.GetEvaluator(array, FunctionForms.Single, 1, 2));
        Assert.Equal(["FunctionInvalid"], document.Codes());
    }

    [Fact]
    public void A_missing_range_is_repaired_from_the_consumer_output_count()
    {
        using PdfDocument document = PdfDocument.Create();
        CosStream stream = Program("<< /FunctionType 4 /Domain [0 1] >>", "{ dup 2 mul }");

        FunctionEvaluator? tint = document.Functions.GetEvaluator(stream, FunctionForms.Single, 1, 2);

        Assert.NotNull(tint);
        Assert.True(tint.IsValid);
        Span<float> output = stackalloc float[2];
        Assert.Equal(FunctionStatus.Ok, tint.Evaluate([0.75f], output));
        Assert.Equal([0.75f, 1.5f], output.ToArray());
        Assert.Equal(["FunctionEntryInvalid"], document.Codes());
    }
}
