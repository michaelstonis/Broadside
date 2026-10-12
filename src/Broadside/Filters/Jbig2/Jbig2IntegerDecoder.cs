using Broadside.Filters.Codecs;

namespace Broadside.Filters.Jbig2;

/// <summary>The arithmetic integer decoding procedures of ITU-T T.88 Annex A.2, one context set of 512 bytes each.</summary>
internal enum Jbig2IntegerProcedure
{
    /// <summary>IAAI: the number of symbol instances in an aggregation.</summary>
    Iaai,

    /// <summary>IADH: the height class delta height.</summary>
    Iadh,

    /// <summary>IADS: the subsequent symbol instance S coordinate.</summary>
    Iads,

    /// <summary>IADT: the strip delta T.</summary>
    Iadt,

    /// <summary>IADW: the symbol delta width.</summary>
    Iadw,

    /// <summary>IAEX: the export run length.</summary>
    Iaex,

    /// <summary>IAFS: the first symbol instance S coordinate.</summary>
    Iafs,

    /// <summary>IAIT: the symbol instance T coordinate.</summary>
    Iait,

    /// <summary>IARDH: the refinement delta height.</summary>
    Iardh,

    /// <summary>IARDW: the refinement delta width.</summary>
    Iardw,

    /// <summary>IARDX: the refinement X offset.</summary>
    Iardx,

    /// <summary>IARDY: the refinement Y offset.</summary>
    Iardy,

    /// <summary>IARI: the refinement bit.</summary>
    Iari,
}

/// <summary>
/// The arithmetic integer decoding procedure (ITU-T T.88 Annex A.2, Figure A.1, Table A.1) and the symbol ID decoding procedure IAID
/// (A.3), over the shared MQ decoder (Annex E.3). Allocates nothing: the contexts belong to the caller.
/// </summary>
internal static class Jbig2IntegerDecoder
{
    /// <summary>The bytes of one integer procedure's context set (PREV is at most 9 bits).</summary>
    public const int ContextCount = 512;

    /// <summary>The largest SBSYMCODELEN decoded with IAID: 2^20 symbols and contexts (1 MiB), far beyond any real symbol count.</summary>
    public const int MaxSymbolCodeLength = 20;

    /// <summary>Decodes one integer (A.2); returns <see langword="false"/> for OOB.</summary>
    /// <param name="decoder">The MQ decoder.</param>
    /// <param name="contexts">The procedure's 512 contexts.</param>
    /// <param name="value">The value; its magnitude may exceed 32 bits when the data is damaged.</param>
    /// <returns><see langword="false"/> when the value is the out-of-band value OOB.</returns>
    public static bool Decode(ref MqDecoder decoder, Span<byte> contexts, out long value)
    {
        int prev = 1;
        int sign = Bit(ref decoder, contexts, ref prev);
        int bits;
        long offset;
        if (Bit(ref decoder, contexts, ref prev) == 0)
        {
            (bits, offset) = (2, 0);
        }
        else if (Bit(ref decoder, contexts, ref prev) == 0)
        {
            (bits, offset) = (4, 4);
        }
        else if (Bit(ref decoder, contexts, ref prev) == 0)
        {
            (bits, offset) = (6, 20);
        }
        else if (Bit(ref decoder, contexts, ref prev) == 0)
        {
            (bits, offset) = (8, 84);
        }
        else if (Bit(ref decoder, contexts, ref prev) == 0)
        {
            (bits, offset) = (12, 340);
        }
        else
        {
            (bits, offset) = (32, 4436);
        }

        long v = 0;
        for (int i = 0; i < bits; i++)
        {
            v = (v << 1) | (uint)Bit(ref decoder, contexts, ref prev);
        }

        v += offset;
        if (sign != 0 && v == 0)
        {
            value = 0;
            return false;
        }

        value = sign != 0 ? -v : v;
        return true;
    }

    /// <summary>Decodes a symbol ID of <paramref name="codeLength"/> bits (A.3).</summary>
    /// <param name="decoder">The MQ decoder.</param>
    /// <param name="contexts">The IAID contexts, at least 2^<paramref name="codeLength"/> bytes.</param>
    /// <param name="codeLength">SBSYMCODELEN; 0 decodes nothing and gives 0.</param>
    /// <returns>The symbol ID.</returns>
    public static int DecodeId(ref MqDecoder decoder, Span<byte> contexts, int codeLength)
    {
        int prev = 1;
        for (int i = 0; i < codeLength; i++)
        {
            prev = (prev << 1) | decoder.Decode(ref contexts[prev]);
        }

        return prev - (1 << codeLength);
    }

    private static int Bit(ref MqDecoder decoder, Span<byte> contexts, ref int prev)
    {
        int d = decoder.Decode(ref contexts[prev]);
        prev = prev < 256 ? (prev << 1) | d : (((prev << 1) | d) & 511) | 256;
        return d;
    }
}
