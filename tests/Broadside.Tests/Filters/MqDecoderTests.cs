using Broadside.Filters.Codecs;

#pragma warning disable CA5394 // pseudo-random test input, not security

namespace Broadside.Tests.Filters;

/// <summary>
/// The MQ decoder's register path (<see cref="MqDecoder.Decode(ref byte, ReadOnlySpan{byte}, ref uint, ref uint, ref int, ref int)"/>,
/// which JPEG 2000 tier-1 keeps in locals across a coding pass) against DECODE on the decoder itself: the same decisions and the same
/// context states, decision after decision (ITU-T T.800 C.3.2 to C.3.4).
/// </summary>
public sealed class MqDecoderTests
{
    public static TheoryData<int, int> Streams => new()
    {
        { 1, 0 },        // random bytes, a few contexts: decisions close to even
        { 2, 1 },        // mostly zero bytes: skewed contexts that stay in the MPS sub-interval
        { 3, 2 },        // 0xFF bytes and markers: stuffed bits and the 1-bits fed at a marker
        { 4, 3 },        // short segment: decoding runs past its end
    };

    [Theory]
    [MemberData(nameof(Streams))]
    public void The_register_path_decides_and_adapts_as_DECODE_does(int seed, int kind)
    {
        var random = new Random(seed);
        byte[] data = new byte[kind == 3 ? 7 : 4096];
        random.NextBytes(data);
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = kind switch
            {
                1 => random.Next(16) == 0 ? data[i] : (byte)0,
                2 => random.Next(4) == 0 ? (byte)0xFF : data[i] is > 0x8F and < 0xFF && random.Next(8) != 0 ? (byte)0x7F : data[i],
                _ => data[i],
            };
        }

        // Contexts start in states spread over Table C.2, as the codecs' tables do (T.800 Table D.7, T.88 Table E.1).
        byte[] expectedContexts = new byte[19];
        for (int i = 0; i < expectedContexts.Length; i++)
        {
            expectedContexts[i] = MqDecoder.Context(random.Next(MqDecoder.StateCount), random.Next(2));
        }

        byte[] actualContexts = (byte[])expectedContexts.Clone();
        int[] sequence = new int[40_000];
        for (int i = 0; i < sequence.Length; i++)
        {
            sequence[i] = random.Next(kind == 0 ? 3 : expectedContexts.Length);
        }

        var expected = new MqDecoder(data);
        var actual = new MqDecoder(data);
        actual.GetRegisters(out uint a, out uint c, out int ct, out int position);
        for (int i = 0; i < sequence.Length; i++)
        {
            int cx = sequence[i];
            int want = expected.Decode(ref expectedContexts[cx]);
            int got = MqDecoder.Decode(ref actualContexts[cx], actual.Data, ref a, ref c, ref ct, ref position);

            Assert.True(want == got && expectedContexts[cx] == actualContexts[cx], $"decision {i} in context {cx}: expected {want}, got {got}");
        }

        actual.SetRegisters(a, c, ct, position);
        Assert.Equal(expectedContexts, actualContexts);
        Assert.Equal(expected.Decode(ref expectedContexts[0]), actual.Decode(ref actualContexts[0]));
    }

    [Fact]
    public void Every_state_and_symbol_gives_the_decision_and_transition_of_Table_C2()
    {
        // One decision from each context state with the same stream: the register path agrees in every state, with either MPS.
        byte[] data = [0x12, 0xA7, 0xFF, 0x3C, 0x91, 0x00, 0x5E, 0xFF, 0x95, 0x44];
        for (int start = 0; start < 16; start++)
        {
            for (int state = 0; state < MqDecoder.StateCount; state++)
            {
                for (int mps = 0; mps < 2; mps++)
                {
                    ReadOnlySpan<byte> segment = data.AsSpan(start % data.Length);
                    var expected = new MqDecoder(segment);
                    var actual = new MqDecoder(segment);
                    byte want = MqDecoder.Context(state, mps);
                    byte got = want;
                    actual.GetRegisters(out uint a, out uint c, out int ct, out int position);
                    for (int i = 0; i < start + 1; i++)
                    {
                        Assert.Equal(expected.Decode(ref want), MqDecoder.Decode(ref got, actual.Data, ref a, ref c, ref ct, ref position));
                        Assert.Equal(want, got);
                    }
                }
            }
        }
    }
}
