using System.Buffers.Binary;
using System.IO.Compression;
using Broadside.Images;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>png-reader</c> target (issue #153): an input that starts with the PNG signature is read as it is; any other input
/// becomes a PNG: byte 0 picks the colour type and bit depth, bytes 1 and 2 the width and height (1 to 32), byte 3 interlacing
/// and a tRNS chunk, and the rest, zlib-compressed, is the IDAT data (so filters and lengths are the fuzzer's). The file goes
/// through <see cref="PdfDocument.CreateImageFromPng"/>; refusals are <see cref="ArgumentException"/> and anything else is a
/// finding. An image that is made must decode to exactly its width x height samples, as must its soft mask, and be the same
/// image when made again.
/// </summary>
/// <remarks>W3C PNG (Third Edition) §5 to §11; ISO 32000-2 §8.9.5, §7.4.4.4, §11.6.5.2.</remarks>
internal static class PngTarget
{
    private static readonly (int ColorType, int Depth)[] Layouts =
    [
        (0, 1), (0, 2), (0, 4), (0, 8), (0, 16), (2, 8), (2, 16), (3, 1), (3, 2), (3, 4), (3, 8), (4, 8), (4, 16), (6, 8), (6, 16),
    ];

    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Target(ReadOnlySpan<byte> data)
    {
        byte[] png = data.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]) ? data.ToArray() : Synthesize(data);
        using PdfDocument document = PdfDocument.Create();
        PdfImage image;
        try
        {
            image = document.CreateImageFromPng(png);
        }
        catch (ArgumentException)
        {
            return;
        }

        Check(image);
        if (image.MaskKind == PdfImageMaskKind.Soft)
        {
            Check(image.Mask!);
        }

        if (!ReferenceEquals(image.Stream, document.CreateImageFromPng(png).Stream))
        {
            throw new InvalidOperationException("The same PNG made a second image.");
        }
    }

    private static void Check(PdfImage image)
    {
        using DecodedImage decoded = image.Decode() ?? throw new InvalidOperationException("A made image does not decode.");
        if (decoded.Width != image.Width || decoded.DecodedRows != image.Height || decoded.Width <= 0)
        {
            throw new InvalidOperationException($"A made {image.Width} x {image.Height} image decodes to {decoded.Width} x {decoded.DecodedRows}.");
        }
    }

    private static byte[] Synthesize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return [];
        }

        (int colorType, int depth) = Layouts[data[0] % Layouts.Length];
        int width = 1 + (data[1] % 32);
        int height = 1 + (data[2] % 32);
        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = (byte)depth;
        header[9] = (byte)colorType;
        header[12] = (byte)(data[3] & 1);
        Chunk(output, "IHDR"u8, header);
        if (colorType == 3)
        {
            byte[] palette = new byte[3 << depth];
            for (int i = 0; i < palette.Length; i++)
            {
                palette[i] = (byte)(i * 41);
            }

            Chunk(output, "PLTE"u8, palette);
        }

        if ((data[3] & 2) != 0 && colorType is 0 or 2 or 3)
        {
            Chunk(output, "tRNS"u8, colorType == 2 ? [0, 1, 0, 2, 0, 3] : [0, (byte)(data[3] >> 4)]);
        }

        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(data[4..]);
            }

            Chunk(output, "IDAT"u8, compressed.ToArray());
        }

        Chunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void Chunk(MemoryStream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> body)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, body.Length);
        output.Write(number);
        output.Write(type);
        output.Write(body);
        uint crc = Crc(Crc(0xFFFFFFFF, type), body) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        output.Write(number);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
