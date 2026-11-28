#!/usr/bin/env python3
"""Generates the TIFF, GIF and BMP files next to this script for the image import tests (issue #642).

Run with ``python3 -I make_import_images.py`` from any directory. The uncompressed TIFF files, every GIF and every BMP are written
by this script (stdlib only); the compressed TIFF variants are made from them by libtiff's ``tiffcp`` (4.x), an encoder independent
of Broadside's decoders, so that the tests check the importer against real-world output. The pixel patterns are the functions
below; ``ImportSamples`` in the tests computes the same ones.
"""

from __future__ import annotations

import os
import struct
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


# ---------------------------------------------------------------------------
# Patterns (mirrored in ImportSamples.cs)
# ---------------------------------------------------------------------------

def black(x: int, y: int) -> bool:
    return ((x // 5 + y // 3) % 3 == 0) or ((x * x + y * y) % 97 < 20)


def gray8(x: int, y: int) -> int:
    return (x * 4 + y * 3) & 255


def rgb8(x: int, y: int) -> tuple[int, int, int]:
    return ((x * 3 + y) & 255, (y * 5) & 255, (x * y) & 255)


def smooth(x: int, y: int) -> tuple[int, int, int]:
    return (x * 3, y * 5, (x + y) * 2)


def alpha8(x: int, y: int) -> int:
    return (x * 7 + y * 11) & 255


def rgb16(x: int, y: int) -> tuple[int, int, int]:
    return ((x * 1000 + y * 7) & 0xFFFF, (y * 2000 + x) & 0xFFFF, (x * y * 37) & 0xFFFF)


def cmyk8(x: int, y: int) -> tuple[int, int, int, int]:
    return ((x * 8) & 255, (y * 15) & 255, ((x + y) * 4) & 255, (x * y) & 255)


def index(x: int, y: int, entries: int) -> int:
    return (x + y * 3) % entries


def palette_rgb(i: int) -> tuple[int, int, int]:
    return (i, 255 - i, (i * 7) & 255)


# ---------------------------------------------------------------------------
# TIFF (TIFF 6.0): one uncompressed strip
# ---------------------------------------------------------------------------

SHORT, LONG, RATIONAL = 3, 4, 5


def tiff(path: str, width: int, height: int, bits: list[int], photometric: int, data: bytes,
         extra: list[int] | None = None, colormap: list[int] | None = None, big: bool = False,
         resolution: tuple[int, int] | None = None, orientation: int | None = None) -> None:
    e = ">" if big else "<"
    entries: list[tuple[int, int, list[int]]] = []

    def add(tag: int, kind: int, values: list[int]) -> None:
        entries.append((tag, kind, values))

    add(256, LONG, [width])
    add(257, LONG, [height])
    add(258, SHORT, bits)
    add(259, SHORT, [1])
    add(262, SHORT, [photometric])
    add(273, LONG, [0])  # patched
    if orientation is not None:
        add(274, SHORT, [orientation])
    add(277, SHORT, [len(bits)])
    add(278, LONG, [height])
    add(279, LONG, [len(data)])
    if resolution is not None:
        add(282, RATIONAL, [resolution[0], 1])
        add(283, RATIONAL, [resolution[1], 1])
        add(296, SHORT, [2])
    add(284, SHORT, [1])
    if colormap is not None:
        add(320, SHORT, colormap)
    if extra is not None:
        add(338, SHORT, extra)
    entries.sort()

    header = (b"MM" if big else b"II") + struct.pack(e + "HI", 42, 8)
    ifd_size = 2 + 12 * len(entries) + 4
    blob = bytearray()
    blob_start = 8 + ifd_size
    values: list[bytes] = []
    for tag, kind, vals in entries:
        if kind == RATIONAL:
            payload = struct.pack(e + "II", *vals)
        else:
            payload = b"".join(struct.pack(e + ("H" if kind == SHORT else "I"), v) for v in vals)
        values.append(payload)
    offsets = {}
    for (tag, kind, vals), payload in zip(entries, values):
        if len(payload) > 4:
            offsets[tag] = blob_start + len(blob)
            blob += payload
            if len(blob) % 2:
                blob += b"\0"
    data_offset = blob_start + len(blob)
    ifd = bytearray(struct.pack(e + "H", len(entries)))
    for (tag, kind, vals), payload in zip(entries, values):
        count = len(vals) // 2 if kind == RATIONAL else len(vals)
        if tag == 273:
            payload = struct.pack(e + "I", data_offset)
        if len(payload) > 4:
            field = struct.pack(e + "I", offsets[tag])
        else:
            field = payload + b"\0" * (4 - len(payload))
        ifd += struct.pack(e + "HHI", tag, kind, count) + field
    ifd += struct.pack(e + "I", 0)
    with open(path, "wb") as f:
        f.write(header + ifd + blob + data)


def packed(width: int, height: int, bits: int, samples: int, value) -> bytes:
    out = bytearray()
    for y in range(height):
        acc = 0
        n = 0
        row = bytearray()
        for x in range(width):
            vals = value(x, y)
            if not isinstance(vals, tuple):
                vals = (vals,)
            for v in vals:
                if bits == 16:
                    row += struct.pack(">H", v)
                    continue
                acc = (acc << bits) | v
                n += bits
                if n == 8:
                    row.append(acc)
                    acc = n = 0
        if n:
            row.append(acc << (8 - n))
        out += row
    return bytes(out)


def swap16(data: bytes) -> bytes:
    return b"".join(data[i + 1:i + 2] + data[i:i + 1] for i in range(0, len(data), 2))


def write_tiffs() -> None:
    w, h = 203, 97
    tiff(p("bilevel.tif"), w, h, [1], 0, packed(w, h, 1, 1, lambda x, y: 1 if black(x, y) else 0), resolution=(200, 100))
    tiff(p("bilevel-orientation6.tif"), w, h, [1], 0, packed(w, h, 1, 1, lambda x, y: 1 if black(x, y) else 0),
         resolution=(200, 100), orientation=6)
    tiff(p("gray8.tif"), 64, 40, [8], 1, packed(64, 40, 8, 1, gray8))
    tiff(p("gray4.tif"), 29, 19, [4], 1, packed(29, 19, 4, 1, lambda x, y: (x + y) % 16))
    tiff(p("rgb8.tif"), 67, 45, [8, 8, 8], 2, packed(67, 45, 8, 3, rgb8))
    tiff(p("smooth8.tif"), 67, 45, [8, 8, 8], 2, packed(67, 45, 8, 3, smooth))
    tiff(p("rgba8.tif"), 67, 45, [8, 8, 8, 8], 2, packed(67, 45, 8, 4, lambda x, y: rgb8(x, y) + (alpha8(x, y),)), extra=[2])
    tiff(p("rgb16.tif"), 33, 21, [16, 16, 16], 2, swap16(packed(33, 21, 16, 3, rgb16)))
    tiff(p("cmyk8.tif"), 31, 17, [8, 8, 8, 8], 5, packed(31, 17, 8, 4, cmyk8))
    cmap8 = [palette_rgb(i)[c] * 257 for c in range(3) for i in range(256)]
    tiff(p("palette8.tif"), 40, 30, [8], 3, packed(40, 30, 8, 1, lambda x, y: index(x, y, 256)), colormap=cmap8)
    cmap4 = [palette_rgb(i * 17)[c] * 257 for c in range(3) for i in range(16)]
    tiff(p("palette4.tif"), 37, 23, [4], 3, packed(37, 23, 4, 1, lambda x, y: index(x, y, 16)), colormap=cmap4, big=True)

    variants = [
        ("bilevel.tif", "bilevel-g3.tif", ["-c", "g3"]),
        ("bilevel.tif", "bilevel-g3-2d.tif", ["-c", "g3:2d"]),
        ("bilevel.tif", "bilevel-g3-2d-fill.tif", ["-c", "g3:2d:fill"]),
        ("bilevel.tif", "bilevel-g4.tif", ["-c", "g4"]),
        ("bilevel.tif", "bilevel-g4-fillorder2.tif", ["-c", "g4", "-f", "lsb2msb"]),
        ("bilevel.tif", "bilevel-g4-strips.tif", ["-c", "g4", "-r", "16"]),
        ("bilevel.tif", "bilevel-packbits.tif", ["-c", "packbits"]),
        ("bilevel.tif", "bilevel-tiles.tif", ["-t", "-w", "64", "-l", "32"]),
        ("gray8.tif", "gray8-lzw-predictor.tif", ["-c", "lzw:2"]),
        ("gray4.tif", "gray4-packbits.tif", ["-c", "packbits"]),
        ("rgb8.tif", "rgb8-lzw.tif", ["-c", "lzw", "-r", "8"]),
        ("rgb8.tif", "rgb8-lzw-predictor.tif", ["-c", "lzw:2"]),
        ("rgb8.tif", "rgb8-deflate-predictor-be.tif", ["-c", "zip:2", "-B"]),
        ("rgb8.tif", "rgb8-packbits.tif", ["-c", "packbits"]),
        ("rgb8.tif", "rgb8-tiles-lzw.tif", ["-t", "-w", "32", "-l", "16", "-c", "lzw"]),
        ("rgb8.tif", "rgb8-planar-lzw.tif", ["-p", "separate", "-c", "lzw:2"]),
        ("smooth8.tif", "smooth8-jpeg.tif", ["-c", "jpeg:90", "-r", "45"]),
        ("smooth8.tif", "smooth8-jpeg-rgb.tif", ["-c", "jpeg:r:90", "-r", "45"]),
        ("smooth8.tif", "smooth8-jpeg-strips.tif", ["-c", "jpeg:90", "-r", "16"]),
        ("rgba8.tif", "rgba8-deflate.tif", ["-c", "zip"]),
        ("rgb16.tif", "rgb16-lzw-predictor.tif", ["-c", "lzw:2"]),
        ("rgb16.tif", "rgb16-lzw-predictor-be.tif", ["-c", "lzw:2", "-B"]),
        ("cmyk8.tif", "cmyk8-lzw.tif", ["-c", "lzw"]),
        ("palette8.tif", "palette8-lzw.tif", ["-c", "lzw"]),
    ]
    for source, target, options in variants:
        subprocess.run(["tiffcp", *options, p(source), p(target)], check=True)
    subprocess.run(["tiffcp", p("bilevel-g4.tif"), p("gray8-lzw-predictor.tif"), p("smooth8-jpeg.tif"), p("multipage.tif")], check=True)


# ---------------------------------------------------------------------------
# GIF89a
# ---------------------------------------------------------------------------

def lzw_gif(indices: bytes, min_size: int) -> bytes:
    """GIF89a Appendix F: variable-width LSB-first codes, a clear code first and whenever the table fills."""
    clear = 1 << min_size
    end = clear + 1
    out = bytearray()
    acc = 0
    nbits = 0

    def emit(code: int, width: int) -> None:
        nonlocal acc, nbits
        acc |= code << nbits
        nbits += width
        while nbits >= 8:
            out.append(acc & 255)
            acc >>= 8
            nbits -= 8

    def reset():
        return {bytes([i]): i for i in range(clear)}, clear + 2, min_size + 1

    table, nxt, width = reset()
    emit(clear, width)
    w = b""
    for b in indices:
        wc = w + bytes([b])
        if wc in table:
            w = wc
            continue
        emit(table[w], width)
        if nxt < 4096:
            table[wc] = nxt
            nxt += 1
            if nxt > (1 << width) and width < 12:
                width += 1
        else:
            emit(clear, width)
            table, nxt, width = reset()
        w = bytes([b])
    if w:
        emit(table[w], width)
    emit(end, width)
    if nbits:
        out.append(acc & 255)
    return bytes(out)


def gif(path: str, width: int, height: int, entries: int, value, interlaced: bool = False,
        transparent: int | None = None, local: bool = False) -> None:
    bits = max(1, (entries - 1).bit_length())
    table = b"".join(bytes(palette_rgb(i * (255 // max(entries - 1, 1)))) for i in range(1 << bits))
    rows = list(range(height))
    if interlaced:
        rows = list(range(0, height, 8)) + list(range(4, height, 8)) + list(range(2, height, 4)) + list(range(1, height, 2))
    indices = bytes(value(x, y) for y in rows for x in range(width))
    min_size = max(2, bits)
    data = lzw_gif(indices, min_size)
    out = bytearray(b"GIF89a" + struct.pack("<HH", width, height))
    out += bytes([(0 if local else 0x80) | ((bits - 1) << 4) | (bits - 1), 0, 0])
    if not local:
        out += table
    if transparent is not None:
        out += bytes([0x21, 0xF9, 4, 1, 0, 0, transparent, 0])
    out += b"\x2C" + struct.pack("<HHHH", 0, 0, width, height)
    out += bytes([(0x80 | (bits - 1) if local else 0) | (0x40 if interlaced else 0)])
    if local:
        out += table
    out.append(min_size)
    for i in range(0, len(data), 255):
        chunk = data[i:i + 255]
        out += bytes([len(chunk)]) + chunk
    out += b"\x00\x3B"
    with open(path, "wb") as f:
        f.write(out)


def write_gifs() -> None:
    gif(p("gif-256.gif"), 61, 37, 256, lambda x, y: index(x, y, 256))
    gif(p("gif-2.gif"), 45, 21, 2, lambda x, y: 1 if black(x, y) else 0)
    gif(p("gif-16-interlaced.gif"), 33, 27, 16, lambda x, y: index(x, y, 16), interlaced=True)
    gif(p("gif-transparent-local.gif"), 20, 10, 8, lambda x, y: index(x, y, 8), transparent=3, local=True)


# ---------------------------------------------------------------------------
# BMP (Windows GDI: BITMAPINFOHEADER, BITMAPV5HEADER)
# ---------------------------------------------------------------------------

def bmp(path: str, width: int, height: int, bits: int, rows: list[bytes], compression: int = 0,
        palette: list[tuple[int, int, int]] | None = None, top_down: bool = False, v5_masks: tuple[int, int, int, int] | None = None,
        masks: tuple[int, int, int] | None = None, data: bytes | None = None) -> None:
    if data is None:
        stride = ((width * bits + 31) // 32) * 4
        body = bytearray()
        order = rows if top_down else list(reversed(rows))
        for row in order:
            body += row + b"\0" * (stride - len(row))
        data = bytes(body)
    header_size = 124 if v5_masks else 40
    table = b"".join(bytes([b, g, r, 0]) for r, g, b in (palette or []))
    extra = struct.pack("<III", *masks) if masks and not v5_masks else b""
    offset = 14 + header_size + len(extra) + len(table)
    info = struct.pack("<IiiHHIIiiII", header_size, width, -height if top_down else height, 1, bits, compression,
                       len(data), 3780, 3780, len(palette or []), 0)
    if v5_masks:
        # Masks, CSType, endpoints (36), gamma (12), intent, profile data, profile size, reserved: 124 bytes in all.
        info += struct.pack("<IIII", *v5_masks) + b"BGRs" + b"\0" * 48 + struct.pack("<IIII", 4, 0, 0, 0)
    with open(path, "wb") as f:
        f.write(b"BM" + struct.pack("<IHHI", offset + len(data), 0, 0, offset) + info + extra + table + data)


def rle8(rows: list[bytes]) -> bytes:
    out = bytearray()
    for row in reversed(rows):
        i = 0
        while i < len(row):
            j = i
            while j < len(row) and j - i < 255 and row[j] == row[i]:
                j += 1
            if j - i >= 2:
                out += bytes([j - i, row[i]])
                i = j
            else:
                k = i
                while k < len(row) and k - i < 255 and (k + 1 >= len(row) or row[k + 1] != row[k]):
                    k += 1
                n = k - i
                if n >= 3:
                    out += bytes([0, n]) + row[i:k] + (b"\0" if n % 2 else b"")
                else:
                    for v in row[i:k]:
                        out += bytes([1, v])
                i = k
        out += b"\0\0"
    out += b"\0\1"
    return bytes(out)


def rle4(rows: list[list[int]]) -> bytes:
    out = bytearray()
    for row in reversed(rows):
        for i in range(0, len(row), 2):
            pair = row[i:i + 2]
            out += bytes([len(pair), (pair[0] << 4) | (pair[1] if len(pair) > 1 else 0)])
        out += b"\0\0"
    out += b"\0\1"
    return bytes(out)


def write_bmps() -> None:
    w, h = 45, 21
    pal2 = [(255, 255, 255), (0, 0, 0)]
    rows1 = [packed(w, 1, 1, 1, lambda x, _y, y=y: 1 if black(x, y) else 0) for y in range(h)]
    bmp(p("bmp-1.bmp"), w, h, 1, rows1, palette=pal2)
    pal16 = [palette_rgb(i * 17) for i in range(16)]
    rows4 = [packed(37, 1, 4, 1, lambda x, _y, y=y: index(x, y, 16)) for y in range(23)]
    bmp(p("bmp-4.bmp"), 37, 23, 4, rows4, palette=pal16)
    pal256 = [palette_rgb(i) for i in range(256)]
    rows8 = [bytes(index(x, y, 256) for x in range(40)) for y in range(30)]
    bmp(p("bmp-8.bmp"), 40, 30, 8, rows8, palette=pal256)
    bmp(p("bmp-8-topdown.bmp"), 40, 30, 8, rows8, palette=pal256, top_down=True)
    bmp(p("bmp-rle8.bmp"), 40, 30, 8, rows8, compression=1, palette=pal256, data=rle8([bytes((x // 6 + y) % 256 for x in range(40)) for y in range(30)]))
    bmp(p("bmp-rle4.bmp"), 37, 23, 4, rows4, compression=2, palette=pal16, data=rle4([[index(x, y, 16) for x in range(37)] for y in range(23)]))
    rows24 = [b"".join(bytes(reversed(rgb8(x, y))) for x in range(67)) for y in range(45)]
    bmp(p("bmp-24.bmp"), 67, 45, 24, rows24)
    rows32 = [b"".join(bytes(reversed(rgb8(x, y))) + bytes([alpha8(x, y)]) for x in range(67)) for y in range(45)]
    bmp(p("bmp-32-alpha.bmp"), 67, 45, 32, rows32, compression=3, v5_masks=(0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000))
    bmp(p("bmp-32.bmp"), 67, 45, 32, rows32)

    def rgb565(x: int, y: int) -> bytes:
        r, g, b = rgb8(x, y)
        return struct.pack("<H", ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3))

    rows16 = [b"".join(rgb565(x, y) for x in range(67)) for y in range(45)]
    bmp(p("bmp-16-565.bmp"), 67, 45, 16, rows16, compression=3, masks=(0xF800, 0x07E0, 0x001F))


def p(name: str) -> str:
    return os.path.join(HERE, name)


def main() -> int:
    write_tiffs()
    write_gifs()
    write_bmps()
    return 0


if __name__ == "__main__":
    sys.exit(main())
