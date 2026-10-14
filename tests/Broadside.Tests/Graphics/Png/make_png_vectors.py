"""PNG vectors for placing PNG images on a canvas (issue #153) that the PngSuite subset in pngsuite/ does not cover.

    qpdf --show-object=5 --filtered-stream-data tests/Corpus/icc-rgb-matrix.pdf > rgb.icc
    qpdf --show-object=7 --filtered-stream-data tests/Corpus/icc-rgb-matrix.pdf > gray.icc
    python3 -I tests/Broadside.Tests/Graphics/Png/make_png_vectors.py rgb.icc gray.icc [output dir]

Written with nothing but zlib and struct, deterministically (fixed pixels, fixed zlib level), so the same bytes come out every
run. Each image is 13 x 11 (odd, so every Adam7 pass has a partial last row or column) unless its name says otherwise. Covered:

- i-<type><depth>.png: every colour type and bit depth of ISO/IEC 15948 Table 11.1, Adam7 interlaced, the five filter types
  cycling row by row inside each pass, and the IDAT data split into 7-byte chunks. Palette images carry a tRNS shorter than
  their palette.
- f-<type><depth>.png: the same images not interlaced, filter type = row number mod 5, one IDAT chunk.
- tiny-*.png: 1 x 1 and 3 x 2 interlaced images, where most Adam7 passes are empty.
- trns-*.png: gray and truecolour images with a tRNS colour that occurs in the image.
- iccp-*.png: an iCCP chunk with the RGB profile (truecolour and palette images) and a gray image with the RGB profile, which
  does not fit it and is ignored.
- opaque-rgba8.png: an RGBA image whose alpha is 255 everywhere.

The expected pixels are not written here: make_png_references.go decodes every PNG in this directory and pngsuite/ with Go's
image/png, an independent decoder, into <name>.rgba64.gz.
"""

import os
import struct
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
RGB_PROFILE = open(sys.argv[1], "rb").read()
GRAY_PROFILE = open(sys.argv[2], "rb").read()
OUT = sys.argv[3] if len(sys.argv) > 3 else os.path.join(HERE, "generated")

CHANNELS = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}
NAMES = {0: "g", 2: "c", 3: "p", 4: "ga", 6: "rgba"}
DEPTHS = {0: [1, 2, 4, 8, 16], 2: [8, 16], 3: [1, 2, 4, 8], 4: [8, 16], 6: [8, 16]}
ADAM7 = [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)]


def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)


def sample(x, y, c, depth, seed):
    """A deterministic sample: a gradient with a pseudo-random component, so every filter type has work to do."""
    value = (x * 37 + y * 101 + c * 59 + seed * 13) ^ ((x * y * 7 + c) * 2654435761 >> 7)
    return value & ((1 << depth) - 1)


def pixels(width, height, channels, depth, seed, palette_size=None):
    rows = []
    for y in range(height):
        row = []
        for x in range(width):
            if palette_size is not None:
                row.append([sample(x, y, 0, 8, seed) % palette_size])
            else:
                row.append([sample(x, y, c, depth, seed) for c in range(channels)])
        rows.append(row)
    return rows


def pack_row(row, depth):
    if depth >= 8:
        out = bytearray()
        for pixel in row:
            for value in pixel:
                out += value.to_bytes(depth // 8, "big")
        return bytes(out)
    out = bytearray()
    bits = 0
    count = 0
    for pixel in row:
        for value in pixel:
            bits = (bits << depth) | value
            count += depth
            if count == 8:
                out.append(bits)
                bits = 0
                count = 0
    if count:
        out.append(bits << (8 - count))
    return bytes(out)


def paeth(a, b, c):
    p = a + b - c
    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
    if pa <= pb and pa <= pc:
        return a
    return b if pb <= pc else c


def filter_row(kind, row, previous, bpp):
    out = bytearray([kind])
    for i, value in enumerate(row):
        left = row[i - bpp] if i >= bpp else 0
        up = previous[i] if previous is not None else 0
        upper_left = previous[i - bpp] if previous is not None and i >= bpp else 0
        predicted = [0, left, up, (left + up) // 2, paeth(left, up, upper_left)][kind]
        out.append((value - predicted) & 0xFF)
    return bytes(out)


def scanlines(rows, depth, channels, filter_of_row):
    bpp = max(1, channels * depth // 8)
    out = bytearray()
    previous = None
    for index, row in enumerate(rows):
        packed = pack_row(row, depth)
        out += filter_row(filter_of_row(index), packed, previous, bpp)
        previous = packed
    return bytes(out)


def encode(width, height, depth, color_type, rows, interlaced, filter_of_row, idat_size=None, extra_chunks=()):
    channels = CHANNELS[color_type]
    if interlaced:
        raw = bytearray()
        for x0, y0, dx, dy in ADAM7:
            sub = [[rows[y][x] for x in range(x0, width, dx)] for y in range(y0, height, dy)]
            if sub and sub[0]:
                raw += scanlines(sub, depth, channels, filter_of_row)
        raw = bytes(raw)
    else:
        raw = scanlines(rows, depth, channels, filter_of_row)
    data = zlib.compress(raw, 9)
    out = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, depth, color_type, 0, 0, 1 if interlaced else 0))
    for kind, payload in extra_chunks:
        out += chunk(kind, payload)
    size = idat_size or len(data)
    for start in range(0, len(data), size):
        out += chunk(b"IDAT", data[start:start + size])
    return out + chunk(b"IEND", b"")


def palette_chunks(size, seed, transparent):
    plte = bytes(sample(i, 0, c, 8, seed + 5) for i in range(size) for c in range(3))
    chunks = [(b"PLTE", plte)]
    if transparent:
        chunks.append((b"tRNS", bytes((i * 47) & 0xFF for i in range(max(1, size - 1)))))
    return chunks


def iccp(profile):
    return (b"iCCP", b"test profile\x00\x00" + zlib.compress(profile, 9))


def write(name, data):
    with open(os.path.join(OUT, name), "wb") as f:
        f.write(data)


def main():
    os.makedirs(OUT, exist_ok=True)
    seed = 0
    for color_type, depths in DEPTHS.items():
        for depth in depths:
            seed += 1
            name = f"{NAMES[color_type]}{depth}"
            palette_size = min(1 << depth, 200) if color_type == 3 else None
            rows = pixels(13, 11, CHANNELS[color_type], depth, seed, palette_size)
            extra = palette_chunks(palette_size, seed, transparent=True) if color_type == 3 else []
            write(f"i-{name}.png", encode(13, 11, depth, color_type, rows, True, lambda r: r % 5, idat_size=7, extra_chunks=extra))
            extra = palette_chunks(palette_size, seed, transparent=False) if color_type == 3 else []
            write(f"f-{name}.png", encode(13, 11, depth, color_type, rows, False, lambda r: r % 5, extra_chunks=extra))

    write("tiny-1x1-rgba16.png", encode(1, 1, 16, 6, pixels(1, 1, 4, 16, 40), True, lambda r: 4))
    write("tiny-3x2-p2.png", encode(3, 2, 2, 3, pixels(3, 2, 1, 2, 41, 4), True, lambda r: r % 5, extra_chunks=palette_chunks(4, 41, True)))

    rows = pixels(13, 11, 1, 16, 50)
    key = rows[3][4][0]
    write("trns-g16.png", encode(13, 11, 16, 0, rows, False, lambda r: r % 5, extra_chunks=[(b"tRNS", struct.pack(">H", key))]))
    rows = pixels(13, 11, 3, 8, 51)
    key = rows[5][6]
    write("trns-c8.png", encode(13, 11, 8, 2, rows, False, lambda r: r % 5, extra_chunks=[(b"tRNS", struct.pack(">HHH", *key))]))
    rows = pixels(13, 11, 1, 2, 52)
    write("trns-g2-i.png", encode(13, 11, 2, 0, rows, True, lambda r: r % 5, extra_chunks=[(b"tRNS", struct.pack(">H", rows[0][0][0]))]))

    write("iccp-c8.png", encode(13, 11, 8, 2, pixels(13, 11, 3, 8, 60), False, lambda r: r % 5, extra_chunks=[iccp(RGB_PROFILE)]))
    write("iccp-g8.png", encode(13, 11, 8, 0, pixels(13, 11, 1, 8, 61), False, lambda r: r % 5, extra_chunks=[iccp(GRAY_PROFILE)]))
    write("iccp-p4.png", encode(13, 11, 4, 3, pixels(13, 11, 1, 4, 62, 16), False, lambda r: r % 5,
                                extra_chunks=[iccp(RGB_PROFILE)] + palette_chunks(16, 62, True)))
    write("iccp-g8-mismatch.png", encode(13, 11, 8, 0, pixels(13, 11, 1, 8, 63), False, lambda r: r % 5, extra_chunks=[iccp(RGB_PROFILE)]))

    rows = pixels(13, 11, 4, 8, 70)
    for row in rows:
        for pixel in row:
            pixel[3] = 255
    write("opaque-rgba8.png", encode(13, 11, 8, 6, rows, False, lambda r: r % 5))


main()
