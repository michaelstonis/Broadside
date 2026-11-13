using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>
/// Scalar-versus-vector equality checks for SIMD kernels (issue #470). A kernel is described by its scalar and vector methods, its
/// input as a span of items (each <c>inputsPerItem</c> elements wide, for example 3 bytes for a YCbCr pixel) and the number of output
/// elements per item. Every check runs both methods on the same input into separate buffers and fails on the first differing output
/// element, naming the item, its input and both outputs. Bytes past the destination are filled with a guard pattern and must come
/// back unchanged, which catches a vector loop that stores past the end through unsafe code.
/// </summary>
/// <remarks>
/// Tests that use these checks carry <c>[Trait("Category", SimdKernelAssert.Category)]</c>: CI runs that category a second time with
/// <c>DOTNET_EnableHWIntrinsic=0</c>, where <see cref="System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated"/> is false, so the
/// dispatcher takes the scalar path and the vector method runs on the software fallback (as the WebAssembly interpreter does for the
/// operations it does not map). See the <c>adding-a-benchmark</c> skill, "SIMD kernels".
/// </remarks>
public static class SimdKernelAssert
{
    /// <summary>The test category of every kernel equality test.</summary>
    public const string Category = "SimdKernel";

    /// <summary>The largest exhaustive domain: every combination of three bytes (2^24 items).</summary>
    public const int MaxExhaustiveWidth = 3;

    /// <summary>The longest input, in items, tried at every length and start offset (several 16-byte vectors plus every tail).</summary>
    public const int ShortItems = 70;

    /// <summary>The start offsets, in items, tried for the short inputs (every misalignment of a 16-byte vector of bytes).</summary>
    public const int Offsets = 16;

    private const int GuardBytes = 64;
    private const byte GuardByte = 0xA5;
    private const int ChunkItems = 1 << 16;

    /// <summary>
    /// Compares the kernels over <paramref name="source"/> as a whole, then over every length from 0 to <see cref="ShortItems"/>
    /// items at every start offset below <see cref="Offsets"/> items (as far as the input reaches).
    /// </summary>
    /// <typeparam name="TIn">The input element.</typeparam>
    /// <typeparam name="TOut">The output element.</typeparam>
    /// <param name="scalar">The scalar method (the reference).</param>
    /// <param name="vector">The vector method.</param>
    /// <param name="source">The input; a partial trailing item is ignored.</param>
    /// <param name="inputsPerItem">Input elements per item.</param>
    /// <param name="outputsPerItem">Output elements per item.</param>
    public static void Equal<TIn, TOut>(SpanKernel<TIn, TOut> scalar, SpanKernel<TIn, TOut> vector, ReadOnlySpan<TIn> source, int inputsPerItem = 1, int outputsPerItem = 1)
        where TIn : unmanaged
        where TOut : unmanaged, IEquatable<TOut>
    {
        ArgumentNullException.ThrowIfNull(scalar);
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputsPerItem, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(outputsPerItem, 1);
        int items = source.Length / inputsPerItem;
        var buffers = new Buffers<TOut>(items * outputsPerItem);
        Compare(scalar, vector, source[..(items * inputsPerItem)], inputsPerItem, outputsPerItem, 0, buffers);
        for (int offset = 0; offset < Offsets && offset <= items; offset++)
        {
            for (int length = 0; length <= ShortItems && offset + length <= items; length++)
            {
                Compare(scalar, vector, source.Slice(offset * inputsPerItem, length * inputsPerItem), inputsPerItem, outputsPerItem, offset, buffers);
            }
        }
    }

    /// <summary>
    /// Compares byte kernels over every combination of <paramref name="width"/> input bytes (256, 65,536 or 16,777,216 items), in
    /// chunks of 65,536 items; the first chunk also goes through every short length and offset (<see cref="Equal"/>).
    /// </summary>
    /// <typeparam name="TOut">The output element.</typeparam>
    /// <param name="width">Input bytes per item, 1 to <see cref="MaxExhaustiveWidth"/>.</param>
    /// <param name="scalar">The scalar method (the reference).</param>
    /// <param name="vector">The vector method.</param>
    /// <param name="outputsPerItem">Output elements per item.</param>
    public static void ExhaustiveBytes<TOut>(int width, SpanKernel<byte, TOut> scalar, SpanKernel<byte, TOut> vector, int outputsPerItem = 1)
        where TOut : unmanaged, IEquatable<TOut>
    {
        ArgumentNullException.ThrowIfNull(scalar);
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxExhaustiveWidth);
        int total = 1 << (8 * width);
        int chunk = Math.Min(total, ChunkItems);
        byte[] input = new byte[chunk * width];
        var buffers = new Buffers<TOut>(chunk * outputsPerItem);
        for (int first = 0; first < total; first += chunk)
        {
            for (int item = 0; item < chunk; item++)
            {
                int value = first + item;
                for (int b = 0; b < width; b++)
                {
                    input[(item * width) + b] = (byte)(value >> (8 * b));
                }
            }

            if (first == 0)
            {
                Equal(scalar, vector, input, width, outputsPerItem);
            }
            else
            {
                Compare(scalar, vector, input, width, outputsPerItem, first, buffers);
            }
        }
    }

    /// <summary>Deterministic pseudo-random bytes, for kernels whose domain is too large to enumerate.</summary>
    /// <param name="length">The number of bytes.</param>
    /// <param name="seed">The seed (keep it fixed so a failure reproduces).</param>
    /// <returns>The bytes.</returns>
    public static byte[] RandomBytes(int length, int seed = 470)
    {
        byte[] bytes = new byte[length];
#pragma warning disable CA5394 // test input, not security
        new Random(seed).NextBytes(bytes);
#pragma warning restore CA5394
        return bytes;
    }

    private static void Compare<TIn, TOut>(SpanKernel<TIn, TOut> scalar, SpanKernel<TIn, TOut> vector, ReadOnlySpan<TIn> source, int inputsPerItem, int outputsPerItem, int firstItem, Buffers<TOut> buffers)
        where TIn : unmanaged
        where TOut : unmanaged, IEquatable<TOut>
    {
        int outputs = source.Length / inputsPerItem * outputsPerItem;
        Span<TOut> expected = buffers.Prepare(actual: false, outputs);
        Span<TOut> actual = buffers.Prepare(actual: true, outputs);
        scalar(source, expected);
        vector(source, actual);
        CheckGuard(buffers.Expected, outputs, "scalar", source.Length);
        CheckGuard(buffers.Actual, outputs, "vector", source.Length);
        for (int i = 0; i < outputs; i++)
        {
            if (!expected[i].Equals(actual[i]))
            {
                int item = i / outputsPerItem;
                var message = new StringBuilder();
                message.Append(CultureInfo.InvariantCulture, $"The vector kernel differs from the scalar kernel at output {i} (item {item}, element {i % outputsPerItem}) of a {source.Length / inputsPerItem}-item input starting at item {firstItem}: ");
                message.Append(CultureInfo.InvariantCulture, $"input [{string.Join(", ", source.Slice(item * inputsPerItem, inputsPerItem).ToArray())}], ");
                message.Append(CultureInfo.InvariantCulture, $"scalar {expected[i]}, vector {actual[i]}.");
                throw new SimdKernelMismatchException(message.ToString());
            }
        }
    }

    private static void CheckGuard<TOut>(TOut[] buffer, int outputs, string which, int inputs)
        where TOut : unmanaged
    {
        ReadOnlySpan<byte> guard = MemoryMarshal.AsBytes(buffer.AsSpan(outputs, Buffers<TOut>.GuardElements));
        int index = guard.IndexOfAnyExcept(GuardByte);
        if (index >= 0)
        {
            throw new SimdKernelMismatchException(
                string.Create(CultureInfo.InvariantCulture, $"The {which} kernel wrote byte {index} past the end of its {outputs}-element destination (input of {inputs} elements)."));
        }
    }

    private sealed class Buffers<TOut>(int outputs)
        where TOut : unmanaged
    {
        public TOut[] Expected { get; } = new TOut[outputs + GuardElements];

        public TOut[] Actual { get; } = new TOut[outputs + GuardElements];

        public static int GuardElements => (GuardBytes + Unsafe.SizeOf<TOut>() - 1) / Unsafe.SizeOf<TOut>();

        /// <summary>Fills a buffer with the guard pattern and returns its first <paramref name="outputs"/> elements.</summary>
        public Span<TOut> Prepare(bool actual, int outputs)
        {
            Span<TOut> span = (actual ? Actual : Expected).AsSpan(0, outputs + GuardElements);
            MemoryMarshal.AsBytes(span).Fill(GuardByte);
            return span[..outputs];
        }
    }
}

/// <summary>A vector kernel that differs from its scalar kernel, or a kernel that wrote past its destination.</summary>
public sealed class SimdKernelMismatchException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SimdKernelMismatchException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What differed.</param>
    public SimdKernelMismatchException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">What differed.</param>
    /// <param name="innerException">The cause.</param>
    public SimdKernelMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
