using System.Runtime.Intrinsics;
using Broadside.TestSupport;

namespace Broadside.Tests.Simd;

/// <summary>
/// The shared SIMD kernel checks (issue #470) on the test-only <see cref="SimdExampleKernel"/>: the pattern every kernel test follows
/// (exhaustive equality of the vector and scalar methods, the dispatcher included, in the <see cref="SimdKernelAssert.Category"/>
/// category that CI reruns with <c>DOTNET_EnableHWIntrinsic=0</c>), and proof that the checks catch a wrong lane and an overrun.
/// </summary>
[Trait("Category", SimdKernelAssert.Category)]
public sealed class SimdKernelAssertTests
{
    [Fact]
    public void The_vector_kernel_matches_the_scalar_kernel_on_every_pair()
    {
        SimdKernelAssert.ExhaustiveBytes<byte>(2, SimdExampleKernel.MultiplyScalar, SimdExampleKernel.MultiplyVector);
        SimdKernelAssert.ExhaustiveBytes<byte>(2, SimdExampleKernel.MultiplyScalar, SimdExampleKernel.Multiply);
    }

    [Fact]
    public void The_vector_kernel_matches_the_scalar_kernel_on_random_rows()
    {
        // For kernels whose domain is too large to enumerate: a fixed-seed row, every short length and every misalignment.
        SimdKernelAssert.Equal<byte, byte>(SimdExampleKernel.MultiplyScalar, SimdExampleKernel.MultiplyVector, SimdKernelAssert.RandomBytes(10_001), inputsPerItem: 2);
    }

    [Fact]
    public void Hardware_acceleration_follows_the_runtime_switch()
    {
        // The CI rerun only covers the fallback if the switch reaches the kernels.
        if (Environment.GetEnvironmentVariable("DOTNET_EnableHWIntrinsic") == "0")
        {
            Assert.False(Vector128.IsHardwareAccelerated);
        }
    }

    [Fact]
    public void A_wrong_lane_is_reported_with_its_input()
    {
        var error = Assert.Throws<SimdKernelMismatchException>(() =>
            SimdKernelAssert.ExhaustiveBytes<byte>(2, SimdExampleKernel.MultiplyScalar, static (source, destination) =>
            {
                SimdExampleKernel.MultiplyVector(source, destination);
                for (int i = 0; i < destination.Length; i++)
                {
                    if (source[2 * i] == 200 && source[(2 * i) + 1] == 3)
                    {
                        destination[i]++;
                    }
                }
            }));

        Assert.Contains("input [200, 3]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_store_past_the_destination_is_reported()
    {
        byte[] spill = new byte[64];
        var error = Assert.Throws<SimdKernelMismatchException>(() =>
            SimdKernelAssert.Equal<byte, byte>(SimdExampleKernel.MultiplyScalar, (source, destination) =>
            {
                SimdExampleKernel.MultiplyVector(source, destination);

                // What an unchecked 16-byte store at the tail does: write the bytes after the destination.
                ref byte end = ref System.Runtime.CompilerServices.Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(destination), destination.Length);
                end = 1;
            }, spill, inputsPerItem: 2));

        Assert.Contains("past the end", error.Message, StringComparison.Ordinal);
    }
}
