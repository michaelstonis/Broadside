using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using Broadside.Filters;
using Broadside.Filters.Ccitt;
using Broadside.Filters.Dct;
using Broadside.Objects;

namespace Broadside.Images.Import;

/// <summary>
/// Reads the pages of a TIFF file (TIFF 6.0, Adobe 1992, with the Adobe PageMaker 6.0 and TIFF Technical Note 2 additions) into
/// <see cref="ImportedImage"/>s: both byte orders, strips and tiles, chunky and planar samples of 1, 2, 4, 8 and 16 bits, bilevel,
/// gray, palette, RGB, CMYK and YCbCr (JPEG-compressed) images, extra samples as alpha, ICC profiles, resolution and orientation.
/// </summary>
/// <remarks>
/// <para>
/// CCITT data (compression 2, 3 and 4) of a page in one strip passes through as <c>CCITTFaxDecode</c> data (ISO 32000-2 §7.4.6),
/// bit-reversed when <c>FillOrder</c> is 2; Modified Huffman data (compression 2) in several strips is joined, since its rows are
/// independent and byte-aligned. JPEG data (compression 7) in one strip or tile of the image's size passes through as
/// <c>DCTDecode</c> data (§7.4.8) with the <c>JPEGTables</c> merged in. Everything else, and CCITT or JPEG data that cannot be
/// joined, is decoded: no compression, PackBits, LZW (TIFF's, and libtiff's old LSB-first form) and Deflate, with the horizontal
/// predictor undone; CCITT and JPEG by the library's own decoders.
/// </para>
/// <para>
/// Malformed data throws <see cref="InvalidDataException"/> with the reason; a feature outside baseline TIFF and the extensions
/// above (old-style JPEG, floating-point or signed samples, CIE L*a*b*, BigTIFF) throws <see cref="NotSupportedException"/>. A
/// strip or tile whose byte count is missing, zero or runs past the end of the file is read up to the next strip or the end of the
/// file, and the repair is listed in <see cref="ImportedImage.Repairs"/>. Decoding goes strip by strip (or tile by tile) through
/// pooled buffers and allocates nothing per row.
/// </para>
/// <para>ISO 32000-2 §8.9.5 (the image XObject each page becomes), §7.4.6 and §7.4.8.</para>
/// </remarks>
internal static class TiffReader
{
    private const int MaxPages = 65536;

    private static readonly byte[] ReversedBits = BuildReversedBits();

    /// <summary>Returns whether <paramref name="data"/> starts with a TIFF header (classic or BigTIFF, either byte order).</summary>
    public static bool IsTiff(ReadOnlySpan<byte> data) =>
        data is [(byte)'I', (byte)'I', 42 or 43, 0, ..] or [(byte)'M', (byte)'M', 0, 42 or 43, ..];

    /// <summary>Returns the offsets of the directories of the file's pages, in file order, skipping reduced-resolution images (thumbnails).</summary>
    /// <exception cref="InvalidDataException">The header or a directory is malformed, or the file has no page.</exception>
    /// <exception cref="NotSupportedException">The file is a BigTIFF.</exception>
    public static List<long> ListPages(ReadOnlySpan<byte> tiff, out bool little)
    {
        if (tiff.Length < 8 || !IsTiff(tiff))
        {
            throw new InvalidDataException("The data does not start with a TIFF header: II or MM and 42 (TIFF 6.0 §2).");
        }

        little = tiff[0] == 'I';
        if (tiff[little ? 2 : 3] == 43)
        {
            throw new NotSupportedException("BigTIFF files (version 43) are not supported; only classic TIFF (version 42) is.");
        }

        long offset = little ? BinaryPrimitives.ReadUInt32LittleEndian(tiff[4..]) : BinaryPrimitives.ReadUInt32BigEndian(tiff[4..]);
        var pages = new List<long>();
        var reduced = new List<long>();
        var seen = new HashSet<long>();
        while (offset != 0 && seen.Count < MaxPages)
        {
            if (!seen.Add(offset))
            {
                break;
            }

            TiffDirectory directory = TiffDirectory.Read(tiff, offset, little);
            ((directory.Value(tiff, TiffTag.NewSubfileType, 0) & 1) == 0 ? pages : reduced).Add(offset);

            offset = directory.Next;
        }

        if (pages.Count == 0)
        {
            pages = reduced;
        }

        if (pages.Count == 0)
        {
            throw new InvalidDataException("The TIFF file has no image file directory (TIFF 6.0 §2).");
        }

        return pages;
    }

    /// <summary>Reads the page whose directory is at <paramref name="offset"/>.</summary>
    /// <param name="tiff">The file.</param>
    /// <param name="offset">The directory's offset, from <see cref="ListPages"/>.</param>
    /// <param name="little">The file's byte order, from <see cref="ListPages"/>.</param>
    /// <param name="maxPixels">The largest width x height accepted.</param>
    /// <exception cref="InvalidDataException">The page is malformed or too large.</exception>
    /// <exception cref="NotSupportedException">The page uses a feature the importer does not read.</exception>
    public static ImportedImage ReadPage(ReadOnlySpan<byte> tiff, long offset, bool little, long maxPixels)
    {
        TiffDirectory d = TiffDirectory.Read(tiff, offset, little);
        var page = new Page(tiff, d, little, maxPixels);
        return page.Read();
    }

    private static byte[] BuildReversedBits()
    {
        byte[] table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            int value = 0;
            for (int bit = 0; bit < 8; bit++)
            {
                value |= ((i >> bit) & 1) << (7 - bit);
            }

            table[i] = (byte)value;
        }

        return table;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>One page: the directory's fields, validated, and the decoding.</summary>
    private ref struct Page
    {
        private readonly ReadOnlySpan<byte> _tiff;
        private readonly TiffDirectory _d;
        private readonly bool _little;
        private readonly List<string> _repairs = [];
        private int _width;
        private int _height;
        private int _samples;
        private int _bits;
        private int _compression;
        private int _photometric;
        private int _components;
        private int _planar;
        private int _predictor;
        private bool _reversed;
        private bool _alpha;
        private bool _premultiplied;
        private bool _tiled;
        private int _chunkWidth;
        private int _chunkHeight;
        private int _across;
        private int _down;
        private long[] _offsets = [];
        private long[] _counts = [];

        public Page(ReadOnlySpan<byte> tiff, TiffDirectory directory, bool little, long maxPixels)
        {
            _tiff = tiff;
            _d = directory;
            _little = little;
            ReadFields(maxPixels);
        }

        public readonly ImportedImage Read()
        {
            ImportedImage image = Decide();
            image.Repairs.AddRange(_repairs);
            foreach (int tag in _d.Dropped)
            {
                image.Repairs.Add(Invariant($"The value of TIFF tag {tag} lies outside the file and was ignored."));
            }

            double x = _d.Rational(_tiff, TiffTag.XResolution);
            double y = _d.Rational(_tiff, TiffTag.YResolution);
            long unit = _d.Value(_tiff, TiffTag.ResolutionUnit, 2);
            double scale = unit switch
            {
                2 => 1,
                3 => 2.54,
                _ => 0,
            };
            if (x > 0 && y > 0 && scale > 0)
            {
                image.DpiX = x * scale;
                image.DpiY = y * scale;
            }

            long orientation = _d.Value(_tiff, TiffTag.Orientation, 1);
            image.Orientation = orientation is >= 1 and <= 8 ? (int)orientation : 1;
            if (orientation is < 1 or > 8)
            {
                image.Repairs.Add(Invariant($"The Orientation {orientation} is not 1 to 8; 1 (top-left) is used."));
            }

            return image;
        }

        private void ReadFields(long maxPixels)
        {
            long width = _d.Value(_tiff, TiffTag.ImageWidth, 0);
            long height = _d.Value(_tiff, TiffTag.ImageLength, 0);
            if (width < 1 || height < 1 || width > int.MaxValue || height > int.MaxValue)
            {
                throw new InvalidDataException(Invariant($"The image is {width} x {height} pixels; ImageWidth and ImageLength shall be at least 1 (TIFF 6.0 §8)."));
            }

            if (width * height > maxPixels)
            {
                throw new InvalidDataException(Invariant($"The {width} x {height} image has more pixels than the {maxPixels} allowed (PdfOptions.MaxImagePixels)."));
            }

            _width = (int)width;
            _height = (int)height;
            _compression = (int)Math.Min(_d.Value(_tiff, TiffTag.Compression, 1), int.MaxValue);
            if (_compression == 6)
            {
                throw new NotSupportedException("Old-style JPEG compression (TIFF compression 6, TIFF 6.0 §22) is not supported; re-save the file with compression 7 (TIFF Technical Note 2).");
            }

            if (_compression is not (1 or 2 or 3 or 4 or 5 or 7 or 8 or 32946 or 32773))
            {
                throw new NotSupportedException(Invariant($"TIFF compression {_compression} is not supported (none, CCITT 2 to 4, LZW, JPEG, Deflate and PackBits are)."));
            }

            long samples = _d.Value(_tiff, TiffTag.SamplesPerPixel, 1);
            if (samples is < 1 or > 16)
            {
                throw new InvalidDataException(Invariant($"SamplesPerPixel is {samples}; 1 to 16 are read (TIFF 6.0 §8)."));
            }

            _samples = (int)samples;
            long[] bits = _d.Values(_tiff, TiffTag.BitsPerSample) ?? [1];
            _bits = (int)Math.Min(bits[0], 64);
            foreach (long value in bits)
            {
                if (value != bits[0])
                {
                    throw new NotSupportedException("Samples of different bit depths within one pixel are not supported.");
                }
            }

            if (_bits is not (1 or 2 or 4 or 8 or 16))
            {
                throw new NotSupportedException(Invariant($"{_bits}-bit samples are not supported (1, 2, 4, 8 and 16 are)."));
            }

            foreach (long format in _d.Values(_tiff, TiffTag.SampleFormat) ?? [1])
            {
                if (format != 1)
                {
                    throw new NotSupportedException(Invariant($"SampleFormat {format} is not supported; only unsigned integer samples (1) are."));
                }
            }

            bool ccitt = _compression is 2 or 3 or 4;
            if (!_d.Has(TiffTag.PhotometricInterpretation))
            {
                _repairs.Add("The PhotometricInterpretation is missing; " + (ccitt ? "WhiteIsZero" : _samples >= 3 ? "RGB" : "BlackIsZero") + " is assumed.");
            }

            _photometric = (int)Math.Min(_d.Value(_tiff, TiffTag.PhotometricInterpretation, ccitt ? 0 : _samples >= 3 ? 2 : 1), int.MaxValue);
            _components = _photometric switch
            {
                0 or 1 or 3 => 1,
                2 or 6 => 3,
                5 => 4,
                _ => throw new NotSupportedException(Invariant($"PhotometricInterpretation {_photometric} is not supported (WhiteIsZero, BlackIsZero, RGB, Palette, CMYK and YCbCr are).")),
            };
            if (_photometric == 5 && (_d.Value(_tiff, TiffTag.InkSet, 1) != 1 || _d.Value(_tiff, TiffTag.NumberOfInks, 4) != 4))
            {
                throw new NotSupportedException("Separated images other than CMYK (InkSet 1, four inks) are not supported.");
            }

            if (_photometric == 6 && _compression != 7)
            {
                throw new NotSupportedException("YCbCr images are supported only when JPEG-compressed (compression 7).");
            }

            if (_photometric == 3 && _bits > 8)
            {
                throw new NotSupportedException("Palette images of more than 8 bits are not supported.");
            }

            if (_samples < _components)
            {
                throw new InvalidDataException(Invariant($"SamplesPerPixel is {_samples}, fewer than the {_components} PhotometricInterpretation {_photometric} needs (TIFF 6.0 §8)."));
            }

            if (ccitt && (_samples != 1 || _bits != 1))
            {
                throw new InvalidDataException("CCITT compression applies only to bilevel images: 1 sample of 1 bit (TIFF 6.0 §10, §11).");
            }

            if (_compression == 7 && _bits != 8)
            {
                throw new NotSupportedException("JPEG-compressed images of other than 8 bits are not supported.");
            }

            if (_samples > _components)
            {
                long[] extra = _d.Values(_tiff, TiffTag.ExtraSamples) ?? [0];
                _alpha = extra[0] is 1 or 2;
                _premultiplied = extra[0] == 1;
            }

            _planar = (int)_d.Value(_tiff, TiffTag.PlanarConfiguration, 1);
            if (_planar is not (1 or 2))
            {
                _repairs.Add(Invariant($"PlanarConfiguration {_planar} is not 1 or 2; 1 (chunky) is used."));
                _planar = 1;
            }

            if (_samples == 1)
            {
                _planar = 1;
            }

            if (_compression == 7 && _planar == 2)
            {
                throw new NotSupportedException("JPEG-compressed images with separate planes (PlanarConfiguration 2) are not supported.");
            }

            _predictor = (int)_d.Value(_tiff, TiffTag.Predictor, 1);
            if (_predictor == 3)
            {
                throw new NotSupportedException("The floating-point predictor (Predictor 3) is not supported.");
            }

            if (_predictor is not (1 or 2))
            {
                _repairs.Add(Invariant($"Predictor {_predictor} is not 1 or 2; no prediction is undone."));
                _predictor = 1;
            }

            if (_predictor == 2 && _bits < 8)
            {
                _repairs.Add(Invariant($"The horizontal predictor is not defined for {_bits}-bit samples; it is ignored."));
                _predictor = 1;
            }

            _reversed = _d.Value(_tiff, TiffTag.FillOrder, 1) == 2;
            ReadChunks();
        }

        private void ReadChunks()
        {
            _tiled = _d.Has(TiffTag.TileWidth) || _d.Has(TiffTag.TileOffsets);
            if (_tiled)
            {
                long tw = _d.Value(_tiff, TiffTag.TileWidth, 0);
                long th = _d.Value(_tiff, TiffTag.TileLength, 0);
                if (tw < 1 || th < 1 || tw > 65536 || th > 65536 || tw * th > Math.Max(4L * _width * _height, 1 << 20))
                {
                    throw new InvalidDataException(Invariant($"The tiles are {tw} x {th}; TileWidth and TileLength shall be positive (TIFF 6.0 §15)."));
                }

                _chunkWidth = (int)tw;
                _chunkHeight = (int)th;
                _across = (int)((_width + tw - 1) / tw);
                _down = (int)((_height + th - 1) / th);
            }
            else
            {
                long rows = _d.Value(_tiff, TiffTag.RowsPerStrip, _height);
                _chunkWidth = _width;
                _chunkHeight = rows < 1 || rows > _height ? _height : (int)rows;
                _across = 1;
                _down = (_height + _chunkHeight - 1) / _chunkHeight;
            }

            long chunks = (long)_across * _down * (_planar == 2 ? _samples : 1);
            if (chunks > 1 << 24)
            {
                throw new InvalidDataException(Invariant($"The image is split into {chunks} strips or tiles, too many to read."));
            }

            int offsetsTag = _tiled ? TiffTag.TileOffsets : TiffTag.StripOffsets;
            int countsTag = _tiled ? TiffTag.TileByteCounts : TiffTag.StripByteCounts;
            long[] offsets = _d.Values(_tiff, offsetsTag) ?? throw new InvalidDataException((_tiled ? "TileOffsets" : "StripOffsets") + " is missing (TIFF 6.0 §8, §15).");
            if (offsets.Length < chunks)
            {
                throw new InvalidDataException(Invariant($"The image needs {chunks} {(_tiled ? "tiles" : "strips")} but has {offsets.Length} offsets (TIFF 6.0 §8, §15)."));
            }

            long[]? counts = _d.Values(_tiff, countsTag);
            bool missing = counts is null || counts.Length < chunks;
            if (missing)
            {
                _repairs.Add((_tiled ? "TileByteCounts" : "StripByteCounts") + " is missing or too short; each strip or tile is read up to the next one.");
                counts = new long[chunks];
            }

            counts ??= [];

            _offsets = offsets[..(int)chunks];
            _counts = counts[..(int)chunks];
            int repaired = 0;
            for (int i = 0; i < _offsets.Length; i++)
            {
                long start = _offsets[i];
                if (start >= _tiff.Length)
                {
                    throw new InvalidDataException(Invariant($"{(_tiled ? "Tile" : "Strip")} {i} starts at offset {start}, past the end of the file."));
                }

                long count = _counts[i];
                if (count <= 0 || start + count > _tiff.Length)
                {
                    long end = _tiff.Length;
                    foreach (long other in _offsets)
                    {
                        if (other > start && other < end)
                        {
                            end = other;
                        }
                    }

                    _counts[i] = end - start;
                    if (!missing)
                    {
                        repaired++;
                    }
                }
            }

            if (repaired > 0)
            {
                _repairs.Add(Invariant($"{repaired} {(_tiled ? "TileByteCounts" : "StripByteCounts")} values were zero or ran past the end of the file; those strips or tiles are read up to the next one."));
            }
        }

        private readonly ReadOnlySpan<byte> Chunk(int index) => _tiff.Slice((int)_offsets[index], (int)_counts[index]);

        private readonly ImportedImage Decide()
        {
            if (_compression is 2 or 3 or 4 && !_tiled && (_down == 1 || _compression == 2))
            {
                return PassCcitt();
            }

            ImportedImage? jpeg = _compression == 7 && !_alpha && _samples == _components && _offsets.Length == 1 ? TryPassJpeg() : null;
            return jpeg ?? DecodeAll();
        }

        private readonly ImportedImage PassCcitt()
        {
            long total = 0;
            foreach (long count in _counts)
            {
                total += count;
            }

            if (total > Array.MaxLength)
            {
                throw new InvalidDataException("The CCITT data is too large.");
            }

            byte[] data = new byte[total];
            int written = 0;
            for (int i = 0; i < _offsets.Length; i++)
            {
                ReadOnlySpan<byte> chunk = Chunk(i);
                chunk.CopyTo(data.AsSpan(written));
                written += chunk.Length;
            }

            if (_reversed)
            {
                Reverse(data);
            }

            var image = new ImportedImage(_width, _height, 1, ImportedColor.Gray);
            image.PassCcitt(data, CcittParameters());
            return image;
        }

        private readonly ImportedCcitt CcittParameters()
        {
            bool blackIs1 = _photometric == 1;
            if (_compression == 2)
            {
                return new ImportedCcitt(0, EndOfLine: false, EncodedByteAlign: true, blackIs1);
            }

            if (_compression == 4)
            {
                return new ImportedCcitt(-1, EndOfLine: false, EncodedByteAlign: false, blackIs1);
            }

            long options = _d.Value(_tiff, TiffTag.T4Options, 0);
            bool twoDimensional = (options & 1) != 0;

            // Fill bits (bit 2) pad the EOL so that it ends on a byte boundary; a T.4 decoder reads them as part of the EOL, so the
            // line itself is not byte-aligned and EncodedByteAlign stays false (it would make a decoder skip to the next byte first).
            return new ImportedCcitt(twoDimensional ? Math.Max(_height, 1) : 0, EndOfLine: true, EncodedByteAlign: false, blackIs1);
        }

        private readonly ImportedImage? TryPassJpeg()
        {
            byte[] merged = MergeJpegTables(Chunk(0));
            if (JpegInfo.Read(merged, readProfile: false, out _) is not { } info || info.Width != _width || info.Height != _height || info.Components != _components)
            {
                return null;
            }

            var image = new ImportedImage(_width, _height, 8, ColorFor(_photometric));
            int transform = _photometric == 2 && info.AdobeTransform is null && info.Components == 3 ? 0 : -1;
            image.PassDct(merged, transform);
            ReadProfile(image);
            return image;
        }

        /// <summary>Puts the JPEGTables (an abbreviated table-specification stream, TIFF Technical Note 2) in front of a strip's frame.</summary>
        private readonly byte[] MergeJpegTables(ReadOnlySpan<byte> strip)
        {
            ReadOnlySpan<byte> tables = _d.Bytes(_tiff, TiffTag.JpegTables);
            if (tables is not [0xFF, 0xD8, ..] || strip is not [0xFF, 0xD8, ..])
            {
                return strip.ToArray();
            }

            ReadOnlySpan<byte> body = tables[2..];
            if (body is [.., 0xFF, 0xD9])
            {
                body = body[..^2];
            }

            byte[] merged = new byte[2 + body.Length + strip.Length - 2];
            merged[0] = 0xFF;
            merged[1] = 0xD8;
            body.CopyTo(merged.AsSpan(2));
            strip[2..].CopyTo(merged.AsSpan(2 + body.Length));
            return merged;
        }

        private static ImportedColor ColorFor(int photometric) => photometric switch
        {
            2 or 6 => ImportedColor.Rgb,
            3 => ImportedColor.Palette,
            5 => ImportedColor.Cmyk,
            _ => ImportedColor.Gray,
        };

        private readonly void ReadProfile(ImportedImage image)
        {
            ReadOnlySpan<byte> profile = _d.Bytes(_tiff, TiffTag.IccProfile);
            if (profile.Length >= 128)
            {
                image.IccProfile = profile.ToArray();
            }
        }

        /// <summary>Decodes every strip or tile into one raw buffer in the file's layout, then turns it into the PDF planes.</summary>
        private readonly ImportedImage DecodeAll()
        {
            int planes = _planar == 2 ? _samples : 1;
            int perPixel = _planar == 2 ? 1 : _samples;
            long rawPlane = Stride(_width, perPixel) * _height;
            long chunkLength = Stride(_chunkWidth, perPixel) * _chunkHeight;
            if (rawPlane * planes > Array.MaxLength || chunkLength > Array.MaxLength)
            {
                throw new InvalidDataException("The decoded image is too large.");
            }

            int rawStride = (int)Stride(_width, perPixel);
            int chunkStride = (int)Stride(_chunkWidth, perPixel);

            byte[] raw = ArrayPool<byte>.Shared.Rent((int)(rawPlane * planes));
            byte[] chunk = ArrayPool<byte>.Shared.Rent((int)chunkLength);
            byte[]? scratch = null;
            ImportLzwDecoder? lzw = _compression == 5 ? new ImportLzwDecoder() : null;
            bool handed = false;
            try
            {
                raw.AsSpan(0, (int)(rawPlane * planes)).Clear();
                int shortChunks = 0;
                for (int p = 0; p < planes; p++)
                {
                    for (int cy = 0; cy < _down; cy++)
                    {
                        for (int cx = 0; cx < _across; cx++)
                        {
                            int index = (((p * _down) + cy) * _across) + cx;
                            int rows = _tiled ? _chunkHeight : Math.Min(_chunkHeight, _height - (cy * _chunkHeight));
                            int need = chunkStride * rows;
                            Span<byte> target = chunk.AsSpan(0, need);
                            target.Clear();
                            ReadOnlySpan<byte> data = Chunk(index);
                            if (_reversed)
                            {
                                scratch = Grow(scratch, data.Length);
                                data.CopyTo(scratch);
                                Reverse(scratch.AsSpan(0, data.Length));
                                data = scratch.AsSpan(0, data.Length);
                            }

                            int produced = DecodeChunk(data, chunk, target, chunkStride, rows, lzw, ref scratch);
                            if (produced < need)
                            {
                                shortChunks++;
                            }

                            if (_predictor == 2 && _compression is 5 or 8 or 32946)
                            {
                                UndoPredictor(target, chunkStride, rows, perPixel);
                            }

                            Place(target, chunkStride, rows, raw.AsSpan((int)(p * rawPlane), (int)rawPlane), rawStride, cx, cy, perPixel);
                        }
                    }
                }

                ImportedImage image = Convert(raw, rawStride, rawPlane, planes, shortChunks, out handed);
                ReadProfile(image);
                return image;
            }
            finally
            {
                if (!handed)
                {
                    ArrayPool<byte>.Shared.Return(raw);
                }

                ArrayPool<byte>.Shared.Return(chunk);
                if (scratch is not null)
                {
                    ArrayPool<byte>.Shared.Return(scratch);
                }

                lzw?.Dispose();
            }
        }

        private readonly long Stride(int width, int samples) => (((long)width * samples * _bits) + 7) / 8;

        /// <summary>Decodes one strip or tile; returns the bytes it produced.</summary>
        private readonly int DecodeChunk(ReadOnlySpan<byte> data, byte[] chunk, Span<byte> target, int stride, int rows, ImportLzwDecoder? lzw, ref byte[]? scratch)
        {
            switch (_compression)
            {
                case 1:
                    int copied = Math.Min(data.Length, target.Length);
                    data[..copied].CopyTo(target);
                    return copied;
                case 32773:
                    return UnpackBits(data, target);
                case 5:
                    bool old = ImportLzwDecoder.IsOldStyle(data);
                    return lzw!.Decode(data, target, 8, msbFirst: !old, earlyChange: !old, out _);
                case 8 or 32946:
                    return Inflate(data, target, ref scratch);
                case 7:
                    return DecodeJpeg(data, chunk, stride, rows);
                default:
                    return DecodeCcitt(data, target, stride, rows);
            }
        }

        private static int UnpackBits(ReadOnlySpan<byte> data, Span<byte> target)
        {
            int read = 0;
            int written = 0;
            while (read < data.Length && written < target.Length)
            {
                int n = (sbyte)data[read++];
                if (n >= 0)
                {
                    int count = Math.Min(Math.Min(n + 1, data.Length - read), target.Length - written);
                    data.Slice(read, count).CopyTo(target[written..]);
                    read += n + 1;
                    written += count;
                }
                else if (n != -128 && read < data.Length)
                {
                    int count = Math.Min(1 - n, target.Length - written);
                    target.Slice(written, count).Fill(data[read++]);
                    written += count;
                }
            }

            return written;
        }

        private static int Inflate(ReadOnlySpan<byte> data, Span<byte> target, ref byte[]? scratch)
        {
            scratch = Grow(scratch, data.Length);
            data.CopyTo(scratch);
            using var source = new MemoryStream(scratch, 0, data.Length, writable: false);
            using var inflater = new ZLibStream(source, CompressionMode.Decompress);
            int written = 0;
            try
            {
                while (written < target.Length)
                {
                    int read = inflater.Read(target[written..]);
                    if (read == 0)
                    {
                        break;
                    }

                    written += read;
                }
            }
            catch (InvalidDataException)
            {
                // Damaged Deflate data keeps what inflated; the caller reports the short strip.
            }

            return written;
        }

        private readonly int DecodeCcitt(ReadOnlySpan<byte> data, Span<byte> target, int stride, int rows)
        {
            ImportedCcitt options = CcittParameters();
            var parameters = new CcittParameters(options.K, options.EndOfLine, options.EncodedByteAlign, _chunkWidth, rows, EndOfBlock: true, BlackIs1: true);
            int work = CcittLineDecoder.WorkLength(_chunkWidth);
            int[] first = ArrayPool<int>.Shared.Rent(work);
            int[] second = ArrayPool<int>.Shared.Rent(work);
            try
            {
                var decoder = new CcittFaxDecoder(data, parameters, rows, first, second);
                int y = 0;
                while (y < rows && decoder.MoveNext())
                {
                    decoder.WriteRow(target.Slice(y * stride, stride));
                    y++;
                }

                return y * stride;
            }
            finally
            {
                ArrayPool<int>.Shared.Return(first);
                ArrayPool<int>.Shared.Return(second);
            }
        }

        private readonly int DecodeJpeg(ReadOnlySpan<byte> data, byte[] chunk, int stride, int rows)
        {
            byte[] merged = MergeJpegTables(data);
            JpegDecoder decoder = JpegDecoder.Rent();
            byte[]? row = null;
            try
            {
                var context = new FilterContext();
                if (!decoder.ReadHeaders(merged, context, silent: true, rows) || decoder.ComponentCount != _components)
                {
                    return 0;
                }

                decoder.SelectColorTransform(_photometric == 6 ? 1 : 0);
                int length = decoder.Width * decoder.ComponentCount;
                row = ArrayPool<byte>.Shared.Rent(length);
                var sink = new ChunkSink(chunk, stride, rows, row, length);
                decoder.Decode(merged, ref sink, Math.Max(16L * stride * rows, 1 << 22));
                return Math.Min(sink.Rows, rows) * stride;
            }
            finally
            {
                JpegDecoder.Return(decoder);
                if (row is not null)
                {
                    ArrayPool<byte>.Shared.Return(row);
                }
            }
        }

        /// <summary>Undoes horizontal differencing (TIFF 6.0 §14) row by row, in the file's byte order for 16-bit samples.</summary>
        private readonly void UndoPredictor(Span<byte> target, int stride, int rows, int perPixel)
        {
            for (int y = 0; y < rows; y++)
            {
                Span<byte> row = target.Slice(y * stride, stride);
                if (_bits == 8)
                {
                    for (int i = perPixel; i < row.Length; i++)
                    {
                        row[i] += row[i - perPixel];
                    }
                }
                else
                {
                    int step = perPixel * 2;
                    for (int i = step; i + 1 < row.Length; i += 2)
                    {
                        if (_little)
                        {
                            ushort value = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(row[i..]) + BinaryPrimitives.ReadUInt16LittleEndian(row[(i - step)..]));
                            BinaryPrimitives.WriteUInt16LittleEndian(row[i..], value);
                        }
                        else
                        {
                            ushort value = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(row[i..]) + BinaryPrimitives.ReadUInt16BigEndian(row[(i - step)..]));
                            BinaryPrimitives.WriteUInt16BigEndian(row[i..], value);
                        }
                    }
                }
            }
        }

        /// <summary>Copies a decoded strip or tile into its place in the raw plane, clipped to the image.</summary>
        private readonly void Place(ReadOnlySpan<byte> chunk, int chunkStride, int rows, Span<byte> plane, int rawStride, int cx, int cy, int perPixel)
        {
            int top = cy * _chunkHeight;
            int left = (int)((long)cx * _chunkWidth * perPixel * _bits / 8);
            int bytes = Math.Min(chunkStride, rawStride - left);
            for (int y = 0; y < rows && top + y < _height; y++)
            {
                chunk.Slice(y * chunkStride, bytes).CopyTo(plane.Slice(((top + y) * rawStride) + left, bytes));
            }
        }

        /// <summary>Turns the raw planes into the PDF color and alpha planes.</summary>
        private readonly ImportedImage Convert(byte[] raw, int rawStride, long rawPlane, int planes, int shortChunks, out bool handed)
        {
            handed = false;
            ImportedColor color = ColorFor(_photometric);
            var image = new ImportedImage(_width, _height, _bits, color);
            if (shortChunks > 0)
            {
                _repairs.Add(Invariant($"{shortChunks} strips or tiles decoded to fewer bytes than their rows need; the missing samples are 0."));
            }

            int total = (int)(rawPlane * planes);
            if (_bits == 16 && _little)
            {
                Span<byte> all = raw.AsSpan(0, total);
                for (int i = 0; i + 1 < all.Length; i += 2)
                {
                    (all[i], all[i + 1]) = (all[i + 1], all[i]);
                }
            }

            if (planes == 1 && _samples == _components)
            {
                image.AdoptColor(raw);
                handed = true;
            }
            else
            {
                Span<byte> target = image.RentColor();
                Span<byte> alpha = _alpha ? image.RentAlpha() : [];
                int colorStride = image.ColorStride;
                int alphaStride = image.AlphaStride;
                for (int y = 0; y < _height; y++)
                {
                    Span<byte> colorRow = target.Slice(y * colorStride, colorStride);
                    Span<byte> alphaRow = _alpha ? alpha.Slice(y * alphaStride, alphaStride) : [];
                    if (planes == 1)
                    {
                        Split(raw.AsSpan(y * rawStride, rawStride), colorRow, alphaRow);
                    }
                    else
                    {
                        for (int c = 0; c < _components; c++)
                        {
                            Interleave(raw.AsSpan((int)((c * rawPlane) + (y * rawStride)), rawStride), colorRow, c);
                        }

                        if (_alpha)
                        {
                            raw.AsSpan((int)((_components * rawPlane) + (y * rawStride)), alphaStride).CopyTo(alphaRow);
                        }
                    }
                }
            }

            if (_photometric == 0)
            {
                Span<byte> plane = image.ColorPlane;
                for (int i = 0; i < plane.Length; i++)
                {
                    plane[i] = (byte)~plane[i];
                }
            }

            if (_photometric == 3)
            {
                image.Palette = Palette(image.Repairs);
            }

            image.Premultiplied = _premultiplied;
            image.DropOpaqueAlpha();
            return image;
        }

        /// <summary>Splits a chunky row of <see cref="_samples"/> samples per pixel into color samples and the first extra sample.</summary>
        private readonly void Split(ReadOnlySpan<byte> raw, Span<byte> color, Span<byte> alpha)
        {
            for (int x = 0; x < _width; x++)
            {
                int pixel = x * _samples;
                for (int c = 0; c < _components; c++)
                {
                    Set(color, (x * _components) + c, Get(raw, pixel + c));
                }

                if (_alpha)
                {
                    Set(alpha, x, Get(raw, pixel + _components));
                }
            }
        }

        /// <summary>Puts one plane's row of samples into component <paramref name="c"/> of a chunky row.</summary>
        private readonly void Interleave(ReadOnlySpan<byte> plane, Span<byte> color, int c)
        {
            for (int x = 0; x < _width; x++)
            {
                Set(color, (x * _components) + c, Get(plane, x));
            }
        }

        private readonly int Get(ReadOnlySpan<byte> row, int index)
        {
            switch (_bits)
            {
                case 8:
                    return row[index];
                case 16:
                    return (row[2 * index] << 8) | row[(2 * index) + 1];
                default:
                    int bit = index * _bits;
                    return (row[bit >> 3] >> (8 - _bits - (bit & 7))) & ((1 << _bits) - 1);
            }
        }

        private readonly void Set(Span<byte> row, int index, int value)
        {
            switch (_bits)
            {
                case 8:
                    row[index] = (byte)value;
                    break;
                case 16:
                    row[2 * index] = (byte)(value >> 8);
                    row[(2 * index) + 1] = (byte)value;
                    break;
                default:
                    int bit = index * _bits;
                    int shift = 8 - _bits - (bit & 7);
                    int mask = ((1 << _bits) - 1) << shift;
                    row[bit >> 3] = (byte)((row[bit >> 3] & ~mask) | ((value << shift) & mask));
                    break;
            }
        }

        /// <summary>Converts the ColorMap (all reds, then greens, then blues, 16 bits each; TIFF 6.0 §5) to an 8-bit RGB table.</summary>
        private readonly byte[] Palette(List<string> repairs)
        {
            int entries = 1 << _bits;
            long[] map = _d.Values(_tiff, TiffTag.ColorMap) ?? throw new InvalidDataException("A palette image has no ColorMap (TIFF 6.0 §5).");
            if (map.Length < 3 * entries)
            {
                throw new InvalidDataException(Invariant($"The ColorMap has {map.Length} values; a {_bits}-bit palette needs {3 * entries} (TIFF 6.0 §5)."));
            }

            // Some writers store 8-bit values; libtiff reads a map with no value above 255 that way.
            bool eightBit = true;
            for (int i = 0; i < 3 * entries; i++)
            {
                if (map[i] > 255)
                {
                    eightBit = false;
                    break;
                }
            }

            if (eightBit)
            {
                repairs.Add("The ColorMap holds 8-bit values instead of 16-bit ones; they are read as 8-bit.");
            }

            byte[] palette = new byte[3 * entries];
            for (int i = 0; i < entries; i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    long value = map[(c * entries) + i];
                    palette[(3 * i) + c] = (byte)(eightBit ? value : value >> 8);
                }
            }

            return palette;
        }

        private static byte[] Grow(byte[]? buffer, int length)
        {
            if (buffer is not null && buffer.Length >= length)
            {
                return buffer;
            }

            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return ArrayPool<byte>.Shared.Rent(Math.Max(length, 1));
        }

        private static void Reverse(Span<byte> data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = ReversedBits[data[i]];
            }
        }
    }

    /// <summary>Writes the rows of a JPEG strip or tile into its chunk buffer, clipped to the chunk's rows and width.</summary>
    private struct ChunkSink(byte[] target, int stride, int rows, byte[] row, int rowLength) : IJpegRowSink
    {
        private int _y;

        public readonly int Rows => _y;

        public readonly Span<byte> BeginRow(int y) => row.AsSpan(0, rowLength);

        public void EndRow()
        {
            if (_y < rows)
            {
                int bytes = Math.Min(stride, rowLength);
                row.AsSpan(0, bytes).CopyTo(target.AsSpan(_y * stride, bytes));
            }

            _y++;
        }
    }
}
