namespace Broadside.Filters.Dct;

/// <summary>The marker codes of ITU-T T.81 Table B.1 (the byte after <c>FF</c>) and the zig-zag sequence of Figure A.6.</summary>
/// <remarks>ITU-T T.81 §B.1.1.3, Table B.1; §A.3.6, Figure A.6.</remarks>
internal static class JpegMarkers
{
    public const byte Sof0 = 0xC0;
    public const byte Sof1 = 0xC1;
    public const byte Sof2 = 0xC2;
    public const byte Sof3 = 0xC3;
    public const byte Dht = 0xC4;
    public const byte Sof15 = 0xCF;
    public const byte Jpg = 0xC8;
    public const byte Sof9 = 0xC9;
    public const byte Sof10 = 0xCA;
    public const byte Dac = 0xCC;
    public const byte Rst0 = 0xD0;
    public const byte Rst7 = 0xD7;
    public const byte Soi = 0xD8;
    public const byte Eoi = 0xD9;
    public const byte Sos = 0xDA;
    public const byte Dqt = 0xDB;
    public const byte Dnl = 0xDC;
    public const byte Dri = 0xDD;
    public const byte Dhp = 0xDE;
    public const byte Exp = 0xDF;
    public const byte App0 = 0xE0;
    public const byte App14 = 0xEE;
    public const byte App15 = 0xEF;
    public const byte Com = 0xFE;
    public const byte Tem = 0x01;

    /// <summary>
    /// Zig-zag index to natural (row-major) index, Figure A.6, padded with 16 entries of 63 so that a corrupt run length that
    /// carries the index past 63 lands on the last coefficient instead of outside the block (as libjpeg does).
    /// </summary>
    public static readonly byte[] NaturalOrder =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
        63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63,
    ];

    /// <summary>Whether <paramref name="marker"/> is a start-of-frame marker (SOF0 to SOF15 without DHT, JPG and DAC).</summary>
    public static bool IsStartOfFrame(byte marker) => marker is >= Sof0 and <= Sof15 and not Dht and not Jpg and not Dac;

    /// <summary>Whether <paramref name="marker"/> is RST0 to RST7.</summary>
    public static bool IsRestart(byte marker) => marker is >= Rst0 and <= Rst7;

    /// <summary>Whether <paramref name="marker"/> stands alone, without a length (§B.1.1.4: SOI, EOI, RST0-RST7, TEM).</summary>
    public static bool IsStandalone(byte marker) => marker is Soi or Eoi or Tem || IsRestart(marker);
}
