using System.Runtime.InteropServices;
using Broadside.Graphics;
using Broadside.Graphics.Colors;
using Broadside.TestSupport;

namespace Broadside.Tests.Colors;

/// <summary>
/// The <c>Vector128</c> path of the CMYK characterization (issue #476) against the scalar one: 20 million 8-bit CMYK colors as images
/// deliver them, colors outside 0..1 and the special values the clip handles, every short row length and misalignment, the
/// dispatcher, and the run detection in front of it. In the <see cref="SimdKernelAssert.Category"/> category, which CI reruns with
/// hardware intrinsics off.
/// </summary>
[Trait("Category", SimdKernelAssert.Category)]
public sealed class CmykCharacterizationKernelTests
{
    private const int Colors = 20_000_000;
    private const int ChunkColors = 1 << 16;

    [Fact]
    public void Twenty_million_8_bit_colors_convert_to_the_same_bits_on_both_paths()
    {
        float[] cmyk = new float[4 * ChunkColors];
        float[] expected = new float[3 * ChunkColors];
        float[] actual = new float[3 * ChunkColors];
        float[] dispatched = new float[3 * ChunkColors];
        for (int done = 0, seed = 0; done < Colors; done += ChunkColors, seed++)
        {
            byte[] samples = SimdKernelAssert.RandomBytes(cmyk.Length, seed);
            for (int i = 0; i < samples.Length; i++)
            {
                cmyk[i] = (float)(samples[i] / 255.0);
            }

            CmykCharacterization.ToRgbRowScalar(cmyk, expected);
            CmykCharacterization.ToRgbRowVector(cmyk, actual);
            CmykCharacterization.ToRgbRow(cmyk, dispatched);
            AssertSameBits(cmyk, expected, actual);
            AssertSameBits(cmyk, expected, dispatched);
        }
    }

    [Fact]
    public void Colors_outside_0_to_1_and_special_values_clip_alike_at_every_length_and_offset()
    {
        float[] special = [float.NaN, -0f, 0f, float.Epsilon, -float.Epsilon, 1f, 1.0000001f, 0.99999994f, -1f, 2f, float.PositiveInfinity, float.NegativeInfinity];
        byte[] bytes = SimdKernelAssert.RandomBytes(4 * 4 * 10_001, seed: 476);
        float[] cmyk = new float[4 * 10_001];
        for (int i = 0; i < cmyk.Length; i++)
        {
            float wide = (BitConverter.ToUInt32(bytes, 4 * i) / (float)uint.MaxValue * 1.5f) - 0.25f;
            cmyk[i] = i % 7 == 0 ? special[(i / 7) % special.Length] : wide;
        }

        SimdKernelAssert.Equal<float, float>(CmykCharacterization.ToRgbRowScalar, CmykCharacterization.ToRgbRowVector, cmyk, inputsPerItem: 4, outputsPerItem: 3);
        SimdKernelAssert.Equal<float, float>(CmykCharacterization.ToRgbRowScalar, CmykCharacterization.ToRgbRow, cmyk, inputsPerItem: 4, outputsPerItem: 3);
    }

    [Fact]
    public void A_row_with_runs_of_one_color_converts_as_each_color_alone()
    {
        // Runs of 1 to 9 equal colors between stretches of distinct ones: the run copies and the vector rows meet at every phase.
        using PdfDocument document = PdfDocument.Create();
        PdfColorConverter converter = document.GetColorConverter(PdfDeviceCmykColorSpace.Instance);
        byte[] samples = SimdKernelAssert.RandomBytes(4 * 3_000, seed: 4761);
        float[] cmyk = new float[samples.Length];
        int count = cmyk.Length / 4;
        int color = 0;
        for (int run = 1; color < count; run = (run % 9) + 1)
        {
            int first = color;
            for (int i = 0; i < run && color < count; i++, color++)
            {
                for (int c = 0; c < 4; c++)
                {
                    cmyk[(4 * color) + c] = (float)(samples[(4 * first) + c] / 255.0);
                }
            }

            for (int i = 0; i < run && color < count; i++, color++)
            {
                for (int c = 0; c < 4; c++)
                {
                    cmyk[(4 * color) + c] = (float)(samples[(4 * color) + c] / 255.0);
                }
            }
        }

        float[] batch = new float[3 * count];
        float[] single = new float[3 * count];
        converter.Convert(cmyk, batch, count);
        for (int i = 0; i < count; i++)
        {
            converter.Convert(cmyk.AsSpan(4 * i, 4), single.AsSpan(3 * i, 3), 1);
        }

        AssertSameBits(cmyk, single, batch);
    }

    private static void AssertSameBits(ReadOnlySpan<float> cmyk, ReadOnlySpan<float> expected, ReadOnlySpan<float> actual)
    {
        ReadOnlySpan<uint> want = MemoryMarshal.Cast<float, uint>(expected);
        ReadOnlySpan<uint> got = MemoryMarshal.Cast<float, uint>(actual);
        for (int i = 0; i < want.Length; i++)
        {
            if (want[i] != got[i])
            {
                int color = i / 3;
                Assert.Fail($"Color {color} ({cmyk[4 * color]}, {cmyk[(4 * color) + 1]}, {cmyk[(4 * color) + 2]}, {cmyk[(4 * color) + 3]}), channel {i % 3}: expected {expected[i]:R}, got {actual[i]:R}.");
            }
        }
    }
}
