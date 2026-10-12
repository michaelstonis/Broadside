namespace Broadside.Filters.Jbig2;

/// <summary>The segment types of ITU-T T.88 §7.3.</summary>
internal static class Jbig2SegmentType
{
    public const int SymbolDictionary = 0;
    public const int IntermediateTextRegion = 4;
    public const int ImmediateTextRegion = 6;
    public const int ImmediateLosslessTextRegion = 7;
    public const int PatternDictionary = 16;
    public const int IntermediateHalftoneRegion = 20;
    public const int ImmediateHalftoneRegion = 22;
    public const int ImmediateLosslessHalftoneRegion = 23;
    public const int IntermediateGenericRegion = 36;
    public const int ImmediateGenericRegion = 38;
    public const int ImmediateLosslessGenericRegion = 39;
    public const int IntermediateRefinementRegion = 40;
    public const int ImmediateRefinementRegion = 42;
    public const int ImmediateLosslessRefinementRegion = 43;
    public const int PageInformation = 48;
    public const int EndOfPage = 49;
    public const int EndOfStripe = 50;
    public const int EndOfFile = 51;
    public const int Profiles = 52;
    public const int Tables = 53;
    public const int Extension = 62;

    /// <summary>Whether <paramref name="type"/> is one of the region segment types (primary type 0-2, subtype direct or refinement).</summary>
    public static bool IsRegion(int type) => type is 4 or 6 or 7 or 20 or 22 or 23 or 36 or 38 or 39 or 40 or 42 or 43;
}

/// <summary>
/// One parsed segment header (ITU-T T.88 §7.2) and where its data part lies in the stream it came from. Retention flags are not kept:
/// producers set them wrongly (jbig2enc leaves symbol dictionaries unretained), so no segment is ever discarded during a decode.
/// </summary>
/// <param name="Number">The segment number (§7.2.2).</param>
/// <param name="Type">The segment type (§7.2.3, §7.3).</param>
/// <param name="Page">The segment page association; 0 for none (§7.2.6).</param>
/// <param name="DataStart">The offset of the data part in its stream.</param>
/// <param name="DataLength">The length of the data part, cut to what the stream holds.</param>
/// <param name="ReferredStart">The index of the first referred-to segment number in <see cref="Jbig2SegmentList.ReferredTo"/>.</param>
/// <param name="ReferredCount">The number of referred-to segments (§7.2.4, §7.2.5).</param>
/// <param name="UnknownLength">
/// Whether the header gave the length 0xFFFFFFFF (§7.2.7) and the data part was found to end with its terminator and four-byte row
/// count (§7.4.6.4), which <see cref="DataLength"/> includes.
/// </param>
internal readonly record struct Jbig2Segment(
    uint Number,
    int Type,
    uint Page,
    int DataStart,
    int DataLength,
    int ReferredStart,
    int ReferredCount,
    bool UnknownLength);
