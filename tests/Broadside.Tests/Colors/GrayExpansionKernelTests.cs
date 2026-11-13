using Broadside.Graphics;
using Broadside.Graphics.Colors;
using Broadside.TestSupport;

namespace Broadside.Tests.Colors;

/// <summary>
/// The <c>Vector128</c> path of the 8-bit gray to RGB expansion (issue #472) against the scalar one: every sample, every short row length
/// and misalignment, random long rows, the dispatcher included. In the <see cref="SimdKernelAssert.Category"/> category, which CI reruns
/// with hardware intrinsics off.
/// </summary>
[Trait("Category", SimdKernelAssert.Category)]
public sealed class GrayExpansionKernelTests
{
    [Fact]
    public void The_vector_expansion_matches_the_scalar_expansion_on_every_sample()
    {
        SimdKernelAssert.ExhaustiveBytes<byte>(1, GrayExpansion.ToRgbScalar, GrayExpansion.ToRgbVector, outputsPerItem: 3);
        SimdKernelAssert.ExhaustiveBytes<byte>(1, GrayExpansion.ToRgbScalar, GrayExpansion.ToRgb, outputsPerItem: 3);
        SimdKernelAssert.Equal<byte, byte>(GrayExpansion.ToRgbScalar, GrayExpansion.ToRgbVector, SimdKernelAssert.RandomBytes(10_001, seed: 472), outputsPerItem: 3);
    }

    [Fact]
    public void DeviceGray_samples_convert_to_equal_RGB_components()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfColorConverter converter = document.GetColorConverter(PdfDeviceGrayColorSpace.Instance);
        byte[] samples = SimdKernelAssert.RandomBytes(1_000, seed: 1);
        byte[] rgb = new byte[3 * samples.Length];

        converter.Convert(samples, rgb, samples.Length);

        for (int i = 0; i < samples.Length; i++)
        {
            Assert.Equal(samples[i], rgb[3 * i]);
            Assert.Equal(samples[i], rgb[(3 * i) + 1]);
            Assert.Equal(samples[i], rgb[(3 * i) + 2]);
        }
    }
}
