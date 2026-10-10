using Broadside.Graphics;
using Broadside.Tests.Document;
using static Broadside.Tests.Graphics.FunctionTesting;

namespace Broadside.Tests.Graphics;

/// <summary>
/// A compiled function is shared by every thread that renders the document (CLAUDE.md thread-safety contract; ISO 32000-2 §7.10):
/// its scratch space lives on each caller's stack, so concurrent evaluations cannot disturb each other. Runs in the heavy
/// collection because it saturates the machine.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class FunctionConcurrencyTests
{
    [Fact]
    public void Concurrent_evaluations_agree_with_a_single_threaded_one()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.Function(Program(
            "<< /FunctionType 4 /Domain [-1 1 -1 1] /Range [-1 1] >>",
            "{360 mul sin 2 div exch 360 mul sin 2 div add}"));
        float[] expected = [.. Enumerable.Range(0, 1000).Select(i => function.At(i / 1000f, 1 - (i / 500f))[0])];

        Parallel.For(0, 64, _ =>
        {
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], function.At(i / 1000f, 1 - (i / 500f))[0]);
            }
        });
    }
}
