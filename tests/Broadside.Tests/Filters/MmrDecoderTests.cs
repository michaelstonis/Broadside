using Broadside.Filters.Ccitt;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// The MMR seam JBIG2 decodes through (<see cref="MmrDecoder"/>): T.6 coding without PDF parameters, 1 = black, EOFB optional,
/// consumption rounded up to a byte. ITU-T T.88 §6.2.6; ITU-T T.6 §2.
/// </summary>
public sealed class MmrDecoderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decodes_a_bitmap_with_black_as_1_with_or_without_EOFB_and_reports_the_bytes_consumed(bool endOfBlock)
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(43, 17);
        byte[] encoded = CcittEncoder.Encode(bitmap, new CcittEncoding(K: -1, EndOfBlock: endOfBlock));
        byte[] destination = new byte[6 * 17];

        MmrResult result = MmrDecoder.Decode([.. encoded, 0xAA, 0x55], 43, 17, destination, 6);

        Assert.Equal(new MmrResult(MmrStatus.Ok, 17, encoded.Length), result);
        Assert.Equal(CcittEncoder.Pack(bitmap, blackIs1: true), destination);
    }

    [Fact]
    public void Two_bitmaps_back_to_back_decode_one_after_the_other_from_the_bytes_consumed()
    {
        bool[][] first = CcittEncoder.SampleBitmap(20, 5, seed: 1);
        bool[][] second = CcittEncoder.SampleBitmap(20, 5, seed: 2);
        byte[] encodedFirst = CcittEncoder.Encode(first, new CcittEncoding(K: -1, EndOfBlock: true));
        byte[] data = [.. encodedFirst, .. CcittEncoder.Encode(second, new CcittEncoding(K: -1, EndOfBlock: true))];
        byte[] one = new byte[15];
        byte[] two = new byte[15];

        MmrResult a = MmrDecoder.Decode(data, 20, 5, one, 3);
        MmrResult b = MmrDecoder.Decode(data.AsSpan(a.BytesConsumed), 20, 5, two, 3);

        Assert.Equal(encodedFirst.Length, a.BytesConsumed);
        Assert.Equal((MmrStatus.Ok, MmrStatus.Ok), (a.Status, b.Status));
        Assert.Equal(CcittEncoder.Pack(first, blackIs1: true), one);
        Assert.Equal(CcittEncoder.Pack(second, blackIs1: true), two);
    }

    [Fact]
    public void An_EOFB_before_the_last_row_ends_the_bitmap()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(16, 3);
        byte[] encoded = CcittEncoder.Encode(bitmap, new CcittEncoding(K: -1, EndOfBlock: true));
        byte[] destination = new byte[2 * 6];

        MmrResult result = MmrDecoder.Decode(encoded, 16, 6, destination, 2);

        Assert.Equal(new MmrResult(MmrStatus.EndOfBlock, 3, encoded.Length), result);
        Assert.Equal([.. CcittEncoder.Pack(bitmap, blackIs1: true), 0, 0, 0, 0, 0, 0], destination);
    }

    [Fact]
    public void Extension_codes_are_invalid_and_data_that_ends_early_is_truncated()
    {
        byte[] destination = new byte[4];

        MmrResult invalid = MmrDecoder.Decode([0b0000_0011, 0b1100_0000], 8, 4, destination, 1);
        MmrResult truncated = MmrDecoder.Decode([0b1100_0000], 8, 4, destination, 1);

        Assert.Equal(new MmrResult(MmrStatus.Invalid, 1, 2), invalid);
        Assert.Equal((MmrStatus.Truncated, 3), (truncated.Status, truncated.Rows));
    }
}
