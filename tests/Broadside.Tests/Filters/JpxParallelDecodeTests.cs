using Broadside.Filters;
using Broadside.Filters.Jpx;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// Issue #505: on native builds the code-blocks of a tile-component (ITU-T T.800 Annex D) are decoded on several threads. Every
/// vector, intact and with corrupted code-block data, decodes to the same samples and the same diagnostics, in the same order, on
/// one thread and on many (one thread per code-block sample forces the parallel path on these small images).
/// </summary>
public sealed class JpxParallelDecodeTests
{
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (JpxVector vector in JpxSamples.Vectors)
        {
            data.Add(vector.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Every_vector_decodes_the_same_on_one_thread_and_on_many(string name)
    {
        byte[] data = JpxSamples.Vector(name).Data;
        byte[] corrupted = [.. data];
        for (int i = corrupted.Length / 2; i < corrupted.Length - 2; i += 7)
        {
            corrupted[i] ^= 0x5A;
        }

        foreach (byte[] input in (byte[][])[data, corrupted])
        {
            for (int reduction = 0; reduction <= 2; reduction++)
            {
                (int[]? serial, string[] serialDiagnostics) = Decode(input, reduction, samplesPerThread: long.MaxValue);
                for (int run = 0; run < 3; run++)
                {
                    (int[]? parallel, string[] parallelDiagnostics) = Decode(input, reduction, samplesPerThread: 1);
                    Assert.Equal(serial, parallel);
                    Assert.Equal(serialDiagnostics, parallelDiagnostics);
                }
            }
        }
    }

    [Fact]
    public void Small_images_stay_on_one_thread_and_browser_builds_never_use_threads()
    {
        Assert.InRange(JpxBlockJob.MaxThreads, 1, 8);
        Assert.Equal(!OperatingSystem.IsBrowser() && !OperatingSystem.IsWasi() && Environment.ProcessorCount > 1, JpxBlockJob.Enabled);
        Assert.Equal(1 << 16, JpxBlockJob.SamplesPerThread);
    }

    private static (int[]? Samples, string[] Diagnostics) Decode(byte[] data, int reduction, long samplesPerThread)
    {
        JpxBlockJob.SamplesPerThreadOverride = samplesPerThread;
        try
        {
            var context = new FilterContext();
            using DecodedImage? image = new JpxDecodeFilter().DecodeImage(data, new ImageFilterContext(context) { Reduction = reduction });
            return (image is null ? null : JpxTesting.Raw(image), [.. context.Diagnostics.Select(static d => $"{d.Code}: {d.Message}")]);
        }
        finally
        {
            JpxBlockJob.SamplesPerThreadOverride = 0;
        }
    }
}
