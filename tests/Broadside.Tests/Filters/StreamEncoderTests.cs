using System.Buffers;
using System.IO.Compression;
using System.Text;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>The managed default encoders through the encoder contract, checked against the decoding filters. ISO 32000-2 §7.4.</summary>
public sealed class StreamEncoderTests
{
    /// <summary>Lengths around every group, run and line boundary of the four encoders, and one long enough for several deflate blocks.</summary>
    private static readonly int[] Lengths = [0, 1, 2, 3, 4, 5, 63, 64, 65, 127, 128, 129, 130, 255, 256, 1000, 70_000];

    public static TheoryData<string, string, int> EncodersShapesAndLengths
    {
        get
        {
            var data = new TheoryData<string, string, int>();
            foreach (string encoder in new[] { "ASCIIHexDecode", "ASCII85Decode", "FlateDecode", "RunLengthDecode" })
            {
                foreach (string shape in new[] { "random", "runs", "zeros", "text" })
                {
                    foreach (int length in Lengths)
                    {
                        data.Add(encoder, shape, length);
                    }
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EncodersShapesAndLengths))]
    public void Every_encoder_round_trips_through_the_decoding_filter_of_its_name(string name, string shape, int length)
    {
        byte[] data = Sample(shape, length, seed: length);
        (IStreamEncoder encoder, IStreamFilter filter) = Pair(name);

        byte[] encoded = Encode(encoder, data);
        (byte[] decoded, string[] codes) = FilterTesting.Run(filter, encoded, mode: PdfReadingMode.Strict);

        Assert.Equal(name, encoder.Name.Value);
        Assert.Equal(data, decoded);
        Assert.Empty(codes);
    }

    [Theory]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.Fastest)]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.SmallestSize)]
    public void Flate_round_trips_at_every_compression_level(CompressionLevel level)
    {
        byte[] data = Sample("text", 100_000, seed: 7);

        byte[] encoded = Encode(new FlateEncoder(), data, new StreamEncoderContext { CompressionLevel = level });

        Assert.Equal(data, FilterTesting.Run(new FlateDecodeFilter(), encoded, mode: PdfReadingMode.Strict).Decoded);
        if (level == CompressionLevel.NoCompression)
        {
            Assert.True(encoded.Length > data.Length, "Stored deflate blocks carry the data as it is, plus their headers.");
        }
        else
        {
            Assert.True(encoded.Length < data.Length * 3 / 4, $"{level} compressed text to {encoded.Length} bytes.");
        }
    }

    [Fact]
    public void Flate_writes_a_zlib_header_and_compresses_harder_at_the_smallest_size_level()
    {
        byte[] data = Sample("text", 200_000, seed: 3);

        byte[] fastest = Encode(new FlateEncoder(), data, new StreamEncoderContext { CompressionLevel = CompressionLevel.Fastest });
        byte[] smallest = Encode(new FlateEncoder(), data, new StreamEncoderContext { CompressionLevel = CompressionLevel.SmallestSize });

        // RFC 1950 §2.2: CMF 0x78 is deflate with a 32 KB window; FCHECK makes CMF * 256 + FLG a multiple of 31.
        Assert.Equal(0x78, smallest[0]);
        Assert.Equal(0, ((smallest[0] << 8) | smallest[1]) % 31);
        Assert.True(smallest.Length <= fastest.Length, $"SmallestSize wrote {smallest.Length} bytes, Fastest {fastest.Length}.");
    }

    [Fact]
    public void Ascii_hex_writes_two_upper_case_digits_per_byte_and_the_end_marker()
    {
        Assert.Equal("00AB7F>", Encoding.ASCII.GetString(Encode(new AsciiHexEncoder(), [0x00, 0xAB, 0x7F])));
        Assert.Equal(">", Encoding.ASCII.GetString(Encode(new AsciiHexEncoder(), [])));
    }

    [Fact]
    public void Flate_writes_a_complete_zlib_stream_for_no_data()
    {
        // Python's zlib.compress(b"") gives these eight bytes: header, an empty final block, Adler-32 of nothing.
        Assert.Equal([0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01], Encode(new FlateEncoder(), []));
    }

    [Fact]
    public void Ascii85_writes_z_for_a_group_of_zeros_a_short_final_group_and_the_end_marker()
    {
        // §7.4.3: four zero bytes are "z"; a final group of n bytes is padded with zeros and written as its first n + 1 characters.
        // 0x61000000 = 31 * 85^4 + 14 * 85^3 + 79 * 85^2 + 24 * 85 + 12, so the group starts "@/" (Python's base64.a85encode agrees).
        Assert.Equal("z@/~>", Encoding.ASCII.GetString(Encode(new Ascii85Encoder(), [0, 0, 0, 0, 0x61])));
        Assert.Equal("~>", Encoding.ASCII.GetString(Encode(new Ascii85Encoder(), [])));
    }

    [Fact]
    public void Ascii_encoders_break_their_output_into_short_lines()
    {
        byte[] data = Sample("random", 10_000, seed: 1);

        foreach (IStreamEncoder encoder in new IStreamEncoder[] { new AsciiHexEncoder(), new Ascii85Encoder() })
        {
            string text = Encoding.ASCII.GetString(Encode(encoder, data));
            Assert.All(text.Split('\n'), line => Assert.InRange(line.Length, 1, 128));
        }
    }

    [Fact]
    public void Run_length_writes_repeated_runs_literal_runs_and_the_end_of_data_byte()
    {
        // §7.4.5: length 0 to 127 copies length + 1 bytes; 129 to 255 repeats the next byte 257 - length times; 128 is EOD.
        Assert.Equal([0xFC, (byte)'A', 0x00, (byte)'B', 0x80], Encode(new RunLengthEncoder(), "AAAAAB"u8.ToArray()));
        Assert.Equal([0x80], Encode(new RunLengthEncoder(), []));
    }

    [Fact]
    public void Run_length_splits_runs_longer_than_128_bytes()
    {
        byte[] repeated = new byte[300];
        byte[] encoded = Encode(new RunLengthEncoder(), repeated);

        // 300 = 128 + 128 + 44: two runs of the longest repeat (length 129) and one of 44 (length 213).
        Assert.Equal([129, 0, 129, 0, 213, 0, 0x80], encoded);
    }

    [Fact]
    public void A_null_output_or_context_is_rejected()
    {
        foreach (IStreamEncoder encoder in new IStreamEncoder[] { new AsciiHexEncoder(), new Ascii85Encoder(), new FlateEncoder(), new RunLengthEncoder() })
        {
            Assert.Throws<ArgumentNullException>(() => encoder.Encode([1], null!, new StreamEncoderContext()));
            Assert.Throws<ArgumentNullException>(() => encoder.Encode([1], new ArrayBufferWriter<byte>(), null!));
        }
    }

    /// <summary>Data of a recognizable shape: random bytes, runs of random lengths, all zeros, or ASCII text with repeated words.</summary>
#pragma warning disable CA5394 // Reproducible test data, not security.
    internal static byte[] Sample(string shape, int length, int seed)
    {
        var random = new Random(seed);
        byte[] data = new byte[length];
        switch (shape)
        {
            case "random":
                random.NextBytes(data);
                break;
            case "runs":
                for (int index = 0; index < length;)
                {
                    int run = Math.Min(length - index, random.Next(1, 300));
                    data.AsSpan(index, run).Fill((byte)random.Next(256));
                    index += run;
                }

                break;
            case "text":
                byte[] text = FilterEncoders.SampleData(length);
                text.CopyTo(data, 0);
                break;
            default:
                break;
        }

        return data;
    }
#pragma warning restore CA5394

    internal static (IStreamEncoder Encoder, IStreamFilter Filter) Pair(string name) => name switch
    {
        "ASCIIHexDecode" => (new AsciiHexEncoder(), new AsciiHexDecodeFilter()),
        "ASCII85Decode" => (new Ascii85Encoder(), new Ascii85DecodeFilter()),
        "FlateDecode" => (new FlateEncoder(), new FlateDecodeFilter()),
        "RunLengthDecode" => (new RunLengthEncoder(), new RunLengthDecodeFilter()),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    internal static byte[] Encode(IStreamEncoder encoder, byte[] data, StreamEncoderContext? context = null)
    {
        var output = new ArrayBufferWriter<byte>();
        encoder.Encode(data, output, context ?? new StreamEncoderContext());
        return output.WrittenSpan.ToArray();
    }
}
