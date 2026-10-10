using System.Buffers;

namespace Broadside.Filters.Jbig2;

/// <summary>
/// The arithmetic coding statistics of one decode (ITU-T T.88 Annex E.3.7): the GB contexts of the generic region procedure, the GR
/// contexts of the generic refinement procedure, the thirteen integer procedures' contexts and the IAID contexts. A segment resets
/// what its clause says (§7.4.2.2 steps 3 to 5, §7.4.3.2, §7.4.4.2, §7.4.5.2, §7.4.6.4, §7.4.7.5) and every procedure it invokes
/// continues the same contexts. Pooled; dispose returns the arrays.
/// </summary>
internal sealed class Jbig2Statistics : IDisposable
{
    private const int IntegerBytes = 13 * Jbig2IntegerDecoder.ContextCount;

    private byte[] _gb;
    private byte[] _gr;
    private byte[] _integers;
    private byte[] _id;
    private int _idLength;

    /// <summary>Initializes a new instance of the <see cref="Jbig2Statistics"/> class.</summary>
    public Jbig2Statistics()
    {
        _gb = ArrayPool<byte>.Shared.Rent(Jbig2GenericRegion.ContextCount);
        _gr = ArrayPool<byte>.Shared.Rent(Jbig2RefinementRegion.ContextCount);
        _integers = ArrayPool<byte>.Shared.Rent(IntegerBytes);
        _id = [];
    }

    /// <summary>Gets the GB contexts.</summary>
    public Span<byte> Generic => _gb.AsSpan(0, Jbig2GenericRegion.ContextCount);

    /// <summary>Gets the GR contexts.</summary>
    public Span<byte> Refinement => _gr.AsSpan(0, Jbig2RefinementRegion.ContextCount);

    /// <summary>Gets the IAID contexts sized by the last <see cref="ResetIntegers"/>.</summary>
    public Span<byte> Id => _id.AsSpan(0, _idLength);

    /// <summary>The contexts of one integer procedure.</summary>
    /// <param name="procedure">The procedure.</param>
    /// <returns>Its 512 contexts.</returns>
    public Span<byte> Integer(Jbig2IntegerProcedure procedure) =>
        _integers.AsSpan((int)procedure * Jbig2IntegerDecoder.ContextCount, Jbig2IntegerDecoder.ContextCount);

    /// <summary>Resets the GB contexts (E.3.7).</summary>
    public void ResetGeneric() => Generic.Clear();

    /// <summary>Resets the GR contexts (E.3.7).</summary>
    public void ResetRefinement() => Refinement.Clear();

    /// <summary>Resets every integer procedure's contexts and sizes the IAID contexts for <paramref name="symbolCodeLength"/> (A.3).</summary>
    /// <param name="symbolCodeLength">SBSYMCODELEN, at most <see cref="Jbig2IntegerDecoder.MaxSymbolCodeLength"/>.</param>
    public void ResetIntegers(int symbolCodeLength)
    {
        _integers.AsSpan(0, IntegerBytes).Clear();
        int length = 1 << symbolCodeLength;
        if (_id.Length < length)
        {
            if (_id.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(_id);
            }

            _id = ArrayPool<byte>.Shared.Rent(length);
        }

        _idLength = length;
        Id.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_gb);
        ArrayPool<byte>.Shared.Return(_gr);
        ArrayPool<byte>.Shared.Return(_integers);
        if (_id.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_id);
        }

        _gb = _gr = _integers = _id = [];
    }
}
